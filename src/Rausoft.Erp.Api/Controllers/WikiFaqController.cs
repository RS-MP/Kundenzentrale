using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Wiki;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Wiki: FAQ-Datenbank für den Support — Supportfälle (Symptom → Ursache →
/// Lösung), schnell erfasst und durchsuchbar. Lesen und anlegen darf jeder
/// aktive Mitarbeiter; eigene Einträge ändert der Ersteller; freigeben,
/// veraltet setzen, fremde Einträge ändern und löschen dürfen Support, Admin,
/// Compliance und die Wiki-Redaktion mit Freigabe für das Thema „faq"
/// (<see cref="FaqRegeln"/>, serverseitig geprüft).
/// </summary>
[ApiController]
[Route("api/wiki/faq")]
public class WikiFaqController(ErpDbContext db, Dienste.WikiBerechtigung berechtigung,
    Dienste.AngemeldeterMitarbeiter angemeldet) : ControllerBase
{
    /// <summary>
    /// Liste mit Suche (alle Begriffe müssen vorkommen — in Titel, Symptom,
    /// Ursache, Lösung oder Schlagwörtern), Filter nach Produkt, Kategorie und
    /// Status (<c>aktuell</c> = Entwurf + Freigegeben, <c>alle</c>, oder ein
    /// Statusname) sowie Sortierung (<c>aenderung</c>, <c>haeufigkeit</c>, <c>titel</c>).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<FaqListe>> Liste([FromQuery] string? suche, [FromQuery] string? produkt,
        [FromQuery] string? kategorie, [FromQuery] string? status, [FromQuery] string? sortierung, CancellationToken ct)
    {
        var kontext = await angemeldet.HolenAsync(ct);
        var darfFreigeben = await berechtigung.DarfFaqFreigebenAsync(ct);

        IQueryable<FaqEintrag> abfrage = db.FaqEintraege.AsNoTracking()
            .Include(f => f.ErstelltVon).Include(f => f.GeaendertVon).Include(f => f.Kunde);

        foreach (var begriff in FaqSuche.Begriffe(suche))
        {
            var muster = FaqSuche.LikeMuster(begriff);
            abfrage = abfrage.Where(f =>
                EF.Functions.Like(f.Titel, muster, "\\")
                || EF.Functions.Like(f.Symptom, muster, "\\")
                || (f.Ursache != null && EF.Functions.Like(f.Ursache, muster, "\\"))
                || EF.Functions.Like(f.Loesung, muster, "\\")
                || (f.Schlagwoerter != null && EF.Functions.Like(f.Schlagwoerter, muster, "\\")));
        }

        abfrage = status switch
        {
            null or "" or "aktuell" => abfrage.Where(f => f.Status != FaqStatus.Veraltet),
            "alle" => abfrage,
            _ when Enum.TryParse<FaqStatus>(status, true, out var s) => abfrage.Where(f => f.Status == s),
            _ => abfrage.Where(f => f.Status != FaqStatus.Veraltet),
        };

        // Zählungen für die Filterchips: nach Suche und Status, aber vor dem jeweils eigenen Filter
        var produktZahlen = await abfrage
            .Where(f => string.IsNullOrEmpty(kategorie) || f.Kategorie == kategorie)
            .GroupBy(f => f.Produkt).Select(g => new { g.Key, Anzahl = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Anzahl, ct);
        var kategorieZahlen = await abfrage
            .Where(f => string.IsNullOrEmpty(produkt) || f.Produkt == produkt)
            .GroupBy(f => f.Kategorie).Select(g => new { g.Key, Anzahl = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Anzahl, ct);

        if (!string.IsNullOrEmpty(produkt))
            abfrage = abfrage.Where(f => f.Produkt == produkt);
        if (!string.IsNullOrEmpty(kategorie))
            abfrage = abfrage.Where(f => f.Kategorie == kategorie);

        abfrage = sortierung switch
        {
            "haeufigkeit" => abfrage.OrderByDescending(f => f.Vorkommen).ThenByDescending(f => f.GeaendertAm ?? f.ErstelltAm),
            "titel" => abfrage.OrderBy(f => f.Titel),
            _ => abfrage.OrderByDescending(f => f.GeaendertAm ?? f.ErstelltAm),
        };

        var eintraege = await abfrage.Take(200).ToListAsync(ct);
        return Ok(new FaqListe(
            eintraege.Select(f => NachDto(f, kontext, darfFreigeben)).ToList(),
            eintraege.Count,
            darfFreigeben,
            FaqKatalog.Produkte.Select(p => new FaqAuswahl(p.Schluessel, p.Name, produktZahlen.GetValueOrDefault(p.Schluessel))).ToList(),
            FaqKatalog.Kategorien.Select(k => new FaqAuswahl(k.Schluessel, k.Name, kategorieZahlen.GetValueOrDefault(k.Schluessel))).ToList()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<FaqEintragDto>> Einzeln(int id, CancellationToken ct)
    {
        var f = await db.FaqEintraege.AsNoTracking()
            .Include(x => x.ErstelltVon).Include(x => x.GeaendertVon).Include(x => x.Kunde)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null)
            return NotFound();
        return Ok(NachDto(f, await angemeldet.HolenAsync(ct), await berechtigung.DarfFaqFreigebenAsync(ct)));
    }

    /// <summary>Anlegen darf jeder aktive Mitarbeiter — Erfassen soll schnell gehen. Status: Entwurf, außer der Benutzer darf freigeben und will es sofort.</summary>
    [HttpPost]
    public async Task<ActionResult<int>> Anlegen(FaqSpeichern daten, CancellationToken ct)
    {
        var eigeneId = await angemeldet.IdAsync(ct);
        if (eigeneId is null)
            return StatusCode(StatusCodes.Status403Forbidden, "Kein Mitarbeiter-Datensatz für dieses Konto.");
        if (await PruefenAsync(daten, ct) is { } fehler)
            return BadRequest(fehler);

        var f = new FaqEintrag { Titel = "", Produkt = "", Kategorie = "", Symptom = "", Loesung = "" };
        Uebernehmen(f, daten);
        f.Status = daten.SofortFreigeben && await berechtigung.DarfFaqFreigebenAsync(ct) ? FaqStatus.Freigegeben : FaqStatus.Entwurf;
        f.ErstelltAm = DateTime.UtcNow;
        f.ErstelltVonId = eigeneId;
        f.ZuletztVorgekommenAm = f.ErstelltAm;
        db.FaqEintraege.Add(f);
        await db.SaveChangesAsync(ct);
        return Ok(f.Id);
    }

    /// <summary>Ändern darf der Ersteller oder wer freigeben darf; der Status bleibt dabei unverändert.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Aendern(int id, FaqSpeichern daten, CancellationToken ct)
    {
        var f = await db.FaqEintraege.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null)
            return NotFound();
        if (!await DarfBearbeitenAsync(f, ct))
            return StatusCode(StatusCodes.Status403Forbidden,
                "Fremde FAQ-Einträge ändern dürfen nur Support, Admin, Compliance und die Redaktion des Themas FAQ.");
        if (await PruefenAsync(daten, ct) is { } fehler)
            return BadRequest(fehler);

        Uebernehmen(f, daten);
        f.GeaendertAm = DateTime.UtcNow;
        f.GeaendertVonId = await angemeldet.IdAsync(ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Entwurf → Freigegeben → Veraltet (und zurück): nur wer freigeben darf.</summary>
    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> StatusSetzen(int id, FaqStatusSetzen daten, CancellationToken ct)
    {
        if (!await berechtigung.DarfFaqFreigebenAsync(ct))
            return KeinFreigaberecht();
        if (!Enum.TryParse<FaqStatus>(daten.Status, true, out var status))
            return BadRequest($"Unbekannter Status '{daten.Status}'.");
        var f = await db.FaqEintraege.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null)
            return NotFound();
        f.Status = status;
        f.GeaendertAm = DateTime.UtcNow;
        f.GeaendertVonId = await angemeldet.IdAsync(ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>„Kam wieder vor" — jeder Mitarbeiter zählt hoch, die Häufigkeit sortiert die Liste.</summary>
    [HttpPost("{id:int}/vorgekommen")]
    public async Task<ActionResult<int>> Vorgekommen(int id, CancellationToken ct)
    {
        var f = await db.FaqEintraege.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null)
            return NotFound();
        f.Vorkommen++;
        f.ZuletztVorgekommenAm = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(f.Vorkommen);
    }

    /// <summary>Löschen: wer freigeben darf, oder der Ersteller bei eigenem Entwurf. Erledigte Fälle besser auf „Veraltet" setzen.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Loeschen(int id, CancellationToken ct)
    {
        var f = await db.FaqEintraege.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null)
            return NotFound();
        if (await angemeldet.HolenAsync(ct) is not { Aktiv: true } m
            || !FaqRegeln.DarfLoeschen(m.Rollen, Dienste.WikiBerechtigung.Themen(m.WikiThemen), m.Id, f.ErstelltVonId, f.Status))
            return StatusCode(StatusCodes.Status403Forbidden,
                "Löschen dürfen Support, Admin, Compliance und die Redaktion des Themas FAQ — oder der Ersteller bei einem eigenen Entwurf.");
        db.FaqEintraege.Remove(f);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // --- Hilfen ----------------------------------------------------------------

    private async Task<bool> DarfBearbeitenAsync(FaqEintrag f, CancellationToken ct) =>
        await angemeldet.HolenAsync(ct) is { Aktiv: true } m
        && FaqRegeln.DarfBearbeiten(m.Rollen, Dienste.WikiBerechtigung.Themen(m.WikiThemen), m.Id, f.ErstelltVonId);

    private ObjectResult KeinFreigaberecht() => StatusCode(StatusCodes.Status403Forbidden,
        "FAQ-Einträge freigeben, veraltet setzen oder löschen dürfen Support, Admin, Compliance und Wiki-Redakteure mit Freigabe für das Thema FAQ.");

    private async Task<string?> PruefenAsync(FaqSpeichern d, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(d.Titel))
            return "Bitte einen Titel angeben — kurz, wie der Fall im Team heißt.";
        if (d.Titel.Trim().Length > 200)
            return "Der Titel ist zu lang (höchstens 200 Zeichen).";
        if (!FaqKatalog.IstProdukt(d.Produkt))
            return "Bitte ein Produkt bzw. einen Bereich aus der Liste wählen.";
        if (!FaqKatalog.IstKategorie(d.Kategorie))
            return "Bitte eine Kategorie aus der Liste wählen.";
        if (string.IsNullOrWhiteSpace(d.Symptom))
            return "Bitte das Symptom beschreiben — was meldet der Kunde?";
        if (string.IsNullOrWhiteSpace(d.Loesung))
            return "Bitte die Lösung bzw. die Schritte beschreiben.";
        if ((FaqSuche.SchlagwoerterSpeichern(d.Schlagwoerter)?.Length ?? 0) > 500)
            return "Die Schlagwörter sind zu lang (höchstens 500 Zeichen).";
        if (d.KundeId is { } kundeId && !await db.Kunden.AnyAsync(k => k.Id == kundeId, ct))
            return "Der gewählte Kunde existiert nicht.";
        return null;
    }

    private static void Uebernehmen(FaqEintrag f, FaqSpeichern d)
    {
        f.Titel = d.Titel.Trim();
        f.Produkt = d.Produkt;
        f.Kategorie = d.Kategorie;
        f.Symptom = d.Symptom.Trim();
        f.Ursache = Leer(d.Ursache);
        f.Loesung = d.Loesung.Trim();
        f.Schlagwoerter = FaqSuche.SchlagwoerterSpeichern(d.Schlagwoerter);
        f.KundeId = d.KundeId;
    }

    private static string? Leer(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static FaqEintragDto NachDto(FaqEintrag f, Dienste.MitarbeiterKontext? m, bool darfFreigeben)
    {
        var themen = m is null ? [] : Dienste.WikiBerechtigung.Themen(m.WikiThemen);
        return new FaqEintragDto(
            f.Id, f.Titel, f.Produkt, FaqKatalog.ProduktName(f.Produkt), f.Kategorie, FaqKatalog.KategorieName(f.Kategorie),
            f.Symptom, f.Ursache, f.Loesung, FaqSuche.Schlagwoerter(f.Schlagwoerter), f.Status.ToString(),
            f.KundeId, f.Kunde is null ? null : $"{f.Kunde.Kundennummer} · {f.Kunde.Firmenname}",
            f.Vorkommen, f.ZuletztVorgekommenAm,
            f.ErstelltAm, f.ErstelltVon?.Anzeigename, f.GeaendertAm, f.GeaendertVon?.Anzeigename,
            darfFreigeben || (m is { Aktiv: true } && f.ErstelltVonId == m.Id),
            m is { Aktiv: true } && FaqRegeln.DarfLoeschen(m.Rollen, themen, m.Id, f.ErstelltVonId, f.Status));
    }
}
