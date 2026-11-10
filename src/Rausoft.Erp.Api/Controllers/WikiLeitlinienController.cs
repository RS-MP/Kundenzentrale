using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Domain.Wiki;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Wiki: Kommunikationsleitlinien — strukturierte Sprachregeln, die Vertrieb und
/// Support lesen und bestätigen und die Claude als Vorgabe bekommt (Export).
/// Lesen und vorschlagen darf jeder aktive Mitarbeiter; eigene Entwürfe ändert
/// und löscht der Ersteller; freigeben, zurückziehen, fremde Entwürfe ändern und
/// löschen ausschließlich Admin und Compliance (<see cref="SprachregelRegeln"/>,
/// serverseitig geprüft). Freigegebene Regeln werden nie überschrieben.
/// </summary>
[ApiController]
[Route("api/wiki/leitlinien")]
public class WikiLeitlinienController(ErpDbContext db, Dienste.AngemeldeterMitarbeiter angemeldet,
    ILogger<WikiLeitlinienController> log) : ControllerBase
{
    private const int HoechstTreffer = 500;

    /// <summary>
    /// Liste mit Suche (alle Begriffe müssen vorkommen — in Aussage, Alternative,
    /// Anlass, Begründung, Beispielen, Varianten oder KI-Hinweis) und Filtern nach
    /// Art, Zielgruppe (<c>vertrieb</c>, <c>support</c>, <c>ki</c>), Thema und Status
    /// (<c>aktuell</c> = Entwurf + Freigegeben, <c>alle</c>, oder ein Statusname).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<LeitlinienListe>> Liste([FromQuery] string? suche, [FromQuery] string? art,
        [FromQuery] string? zielgruppe, [FromQuery] string? thema, [FromQuery] string? status, CancellationToken ct)
    {
        var m = await angemeldet.HolenAsync(ct);
        var abfrage = Basis();

        foreach (var begriff in FaqSuche.Begriffe(suche))
        {
            var muster = FaqSuche.LikeMuster(begriff);
            abfrage = abfrage.Where(r =>
                EF.Functions.Like(r.Aussage, muster, "\\")
                || (r.Stattdessen != null && EF.Functions.Like(r.Stattdessen, muster, "\\"))
                || (r.Anlass != null && EF.Functions.Like(r.Anlass, muster, "\\"))
                || (r.Begruendung != null && EF.Functions.Like(r.Begruendung, muster, "\\"))
                || (r.BeispielGut != null && EF.Functions.Like(r.BeispielGut, muster, "\\"))
                || (r.BeispielSchlecht != null && EF.Functions.Like(r.BeispielSchlecht, muster, "\\"))
                || (r.Varianten != null && EF.Functions.Like(r.Varianten, muster, "\\"))
                || (r.KiHinweis != null && EF.Functions.Like(r.KiHinweis, muster, "\\")));
        }

        switch (status)
        {
            case null or "" or "aktuell":
                abfrage = abfrage.Where(r => r.Status != SprachregelStatus.Zurueckgezogen);
                break;
            case "alle":
                break;
            default:
                if (!Enum.TryParse<SprachregelStatus>(status, true, out var s) || !Enum.IsDefined(s))
                    return BadRequest($"Unbekannter Status '{status}'.");
                abfrage = abfrage.Where(r => r.Status == s);
                break;
        }

        if (!string.IsNullOrEmpty(art))
        {
            if (!Enum.TryParse<SprachregelArt>(art, true, out var a) || !Enum.IsDefined(a))
                return BadRequest($"Unbekannte Art '{art}'.");
            abfrage = abfrage.Where(r => r.Art == a);
        }

        if (!string.IsNullOrEmpty(zielgruppe))
        {
            if (SprachregelKatalog.Zielgruppe(zielgruppe) is not { } z)
                return BadRequest($"Unbekannte Zielgruppe '{zielgruppe}'.");
            abfrage = abfrage.Where(r => (r.Zielgruppen & z) != SprachregelZielgruppe.Keine);
        }

        if (!string.IsNullOrEmpty(thema) && !SprachregelKatalog.IstThema(thema))
            return BadRequest($"Unbekanntes Thema '{thema}'.");

        // Zählungen für die Themen-Chips: nach Suche, Status, Art und Zielgruppe, aber vor dem Themenfilter
        var treffer = await abfrage.OrderBy(r => r.Id).Take(HoechstTreffer).ToListAsync(ct);
        var themaZahlen = treffer.GroupBy(r => r.Thema).ToDictionary(g => g.Key, g => g.Count());
        if (!string.IsNullOrEmpty(thema))
            treffer = treffer.Where(r => r.Thema == thema).ToList();

        // Regeln, die der Benutzer noch bestätigen muss — unabhängig von Suche und Filtern
        var offeneRegelIds = m is { Aktiv: true }
            ? await db.WikiNeuerungen.AsNoTracking()
                .Where(n => n.SprachregelId != null)
                .Where(WikiNeuerung.OffeneBestaetigung(m.Id, m.Rollen))
                .Select(n => n.SprachregelId!.Value).ToListAsync(ct)
            : [];
        var zuBestaetigen = offeneRegelIds.Count == 0
            ? []
            : await Basis().Where(r => offeneRegelIds.Contains(r.Id) && r.Status == SprachregelStatus.Freigegeben).ToListAsync(ct);

        var lesestand = await LesestandLadenAsync(treffer.Concat(zuBestaetigen).Select(r => r.Id).Distinct().ToList(), ct);
        var darfFreigeben = m is { Aktiv: true } && SprachregelRegeln.DarfFreigeben(m.Rollen);

        return Ok(new LeitlinienListe(
            Sortiert(treffer).Select(r => NachDto(r, m, lesestand)).ToList(),
            Sortiert(zuBestaetigen).Select(r => NachDto(r, m, lesestand)).ToList(),
            darfFreigeben,
            m is { Aktiv: true }
                ? SprachregelKatalog.Zielgruppen.Where(z => SprachregelRegeln.DarfExportieren(m.Rollen, z.Wert)).Select(z => z.Schluessel).ToList()
                : [],
            MeineZielgruppe(m),
            SprachregelKatalog.Themen.Select(t => new LeitlinienAuswahl(t.Schluessel, t.Name, themaZahlen.GetValueOrDefault(t.Schluessel))).ToList(),
            FaqKatalog.Produkte.Select(p => new LeitlinienAuswahl(p.Schluessel, p.Name, 0)).ToList()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<LeitlinieDto>> Einzeln(int id, CancellationToken ct)
    {
        var r = await Basis().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null)
            return NotFound();
        return Ok(NachDto(r, await angemeldet.HolenAsync(ct), await LesestandLadenAsync([r.Id], ct)));
    }

    /// <summary>Vorschlagen darf jeder aktive Mitarbeiter — die Regel entsteht immer als Entwurf.</summary>
    [HttpPost]
    public async Task<ActionResult<int>> Anlegen(LeitlinieSpeichern daten, CancellationToken ct)
    {
        if (await angemeldet.HolenAsync(ct) is not { Aktiv: true } m)
            return StatusCode(StatusCodes.Status403Forbidden, "Kein Mitarbeiter-Datensatz für dieses Konto.");
        var r = new Sprachregel { Aussage = "", Thema = "" };
        if (Uebernehmen(r, daten) is { } fehler)
            return BadRequest(fehler);
        r.Status = SprachregelStatus.Entwurf;
        r.ErstelltAm = DateTime.UtcNow;
        r.ErstelltVonId = m.Id;
        db.Sprachregeln.Add(r);
        await db.SaveChangesAsync(ct);
        return Ok(r.Id);
    }

    /// <summary>
    /// Ändern geht nur am Entwurf: der Ersteller oder Admin/Compliance. Eine
    /// freigegebene Regel wird nicht überschrieben — sie wird neu gefasst
    /// (<see cref="Neufassung"/>), damit der freigegebene Wortlaut nachvollziehbar bleibt.
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Aendern(int id, LeitlinieSpeichern daten, CancellationToken ct)
    {
        var r = await db.Sprachregeln.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null)
            return NotFound();
        if (r.Status != SprachregelStatus.Entwurf)
            return StatusCode(StatusCodes.Status409Conflict,
                "Freigegebene und zurückgezogene Regeln werden nicht überschrieben — bitte „Neu fassen“ wählen und den neuen Entwurf freigeben lassen.");
        if (await angemeldet.HolenAsync(ct) is not { Aktiv: true } m
            || !SprachregelRegeln.DarfBearbeiten(m.Rollen, m.Id, r.ErstelltVonId, r.Status))
            return StatusCode(StatusCodes.Status403Forbidden,
                "Fremde Entwürfe ändern dürfen nur Admin und Compliance.");
        if (!GesehenenStandSetzen(r, daten.Version))
            return BadRequest("Der Stand des Datensatzes fehlt — bitte die Seite neu laden.");
        if (Uebernehmen(r, daten) is { } fehler)
            return BadRequest(fehler);
        r.GeaendertAm = DateTime.UtcNow;
        r.GeaendertVonId = m.Id;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Freigeben (Entwurf → Freigegeben) und zurückziehen (Freigegeben → Zurückgezogen):
    /// ausschließlich Admin und Compliance, für genau den Stand, den sie gesehen haben.
    /// In derselben Transaktion entstehen die Wiki-Neuerungen: bei „Niemals“ und
    /// Pflichthinweisen mit Lesebestätigung für die betroffenen Rollen, bei der
    /// Rücknahme als Hinweis „gilt nicht mehr“.
    /// </summary>
    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> StatusSetzen(int id, LeitlinieStatusSetzen daten, CancellationToken ct)
    {
        if (await angemeldet.HolenAsync(ct) is not { Aktiv: true } m || !SprachregelRegeln.DarfFreigeben(m.Rollen))
            return StatusCode(StatusCodes.Status403Forbidden,
                "Kommunikationsleitlinien freigeben und zurückziehen dürfen ausschließlich Admin und Compliance.");
        if (!Enum.TryParse<SprachregelStatus>(daten.Status, true, out var status) || !Enum.IsDefined(status))
            return BadRequest($"Unbekannter Status '{daten.Status}'.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var r = await db.Sprachregeln.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null)
            return NotFound();
        if (!SprachregelRegeln.IstErlaubterWechsel(r.Status, status))
            return StatusCode(StatusCodes.Status409Conflict,
                "Dieser Statuswechsel ist nicht möglich: Ein Entwurf wird freigegeben, eine freigegebene Regel zurückgezogen — zurück geht es nicht, dafür gibt es „Neu fassen“.");
        if (!GesehenenStandSetzen(r, daten.Version))
            return BadRequest("Der Stand des Datensatzes fehlt — bitte die Seite neu laden.");

        var jetzt = DateTime.UtcNow;
        if (status == SprachregelStatus.Freigegeben)
        {
            // Entwürfe können älter sein als die heutigen Pflichtfelder — vor der Freigabe erneut prüfen
            if (SprachregelRegeln.Pruefen(r.Art, r.Aussage, r.Stattdessen, r.Anlass, r.Begruendung, r.Zielgruppen) is { } fehler)
                return BadRequest(fehler);
            r.Status = SprachregelStatus.Freigegeben;
            r.FreigegebenAm = jetzt;
            r.FreigegebenVonId = m.Id;

            // Neufassung: Die bisherige Regel gilt ab jetzt nicht mehr
            var vorgaenger = r.VorgaengerId is { } vorgaengerId
                ? await db.Sprachregeln.FirstOrDefaultAsync(x => x.Id == vorgaengerId && x.Status == SprachregelStatus.Freigegeben, ct)
                : null;
            var rollen = SprachregelRegeln.BetroffeneRollen(r.Zielgruppen);
            if (vorgaenger is not null)
            {
                await ZurueckziehenAsync(vorgaenger, m.Id, jetzt, ct);
                // Wen nur die alte Fassung betraf, der erfährt von der Rücknahme
                var nurAlt = SprachregelRegeln.BetroffeneRollen(vorgaenger.Zielgruppen) & ~rollen;
                if (nurAlt != MitarbeiterRolle.Keine)
                    NeuerungAnlegen(vorgaenger, SprachregelNeuerung.Ruecknahme(vorgaenger), nurAlt, bestaetigung: false, m.Id, jetzt);
            }

            // Lesebestätigung für „Niemals“ und Pflichthinweise; ist nur KI angekreuzt, betrifft die Regel keinen Mitarbeiter
            var pflicht = SprachregelRegeln.IstBestaetigungspflichtig(r.Art);
            if (rollen != MitarbeiterRolle.Keine && (pflicht || vorgaenger is not null))
                NeuerungAnlegen(r, SprachregelNeuerung.Freigabe(r, vorgaenger is not null), rollen, pflicht, m.Id, jetzt);
        }
        else
        {
            await ZurueckziehenAsync(r, m.Id, jetzt, ct);
            var rollen = SprachregelRegeln.BetroffeneRollen(r.Zielgruppen);
            if (rollen != MitarbeiterRolle.Keine)
                NeuerungAnlegen(r, SprachregelNeuerung.Ruecknahme(r), rollen, bestaetigung: false, m.Id, jetzt);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Neu fassen: legt aus einer freigegebenen oder zurückgezogenen Regel einen
    /// neuen Entwurf an (jeder darf vorschlagen). Wird er freigegeben, löst er die
    /// bisherige Regel ab — die alte Fassung bleibt unverändert erhalten.
    /// </summary>
    [HttpPost("{id:int}/neufassung")]
    public async Task<ActionResult<int>> Neufassung(int id, CancellationToken ct)
    {
        if (await angemeldet.HolenAsync(ct) is not { Aktiv: true } m)
            return StatusCode(StatusCodes.Status403Forbidden, "Kein Mitarbeiter-Datensatz für dieses Konto.");
        var alt = await db.Sprachregeln.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (alt is null)
            return NotFound();
        if (alt.Status == SprachregelStatus.Entwurf)
            return StatusCode(StatusCodes.Status409Conflict, "Ein Entwurf wird direkt bearbeitet — neu fassen lassen sich nur freigegebene oder zurückgezogene Regeln.");

        var neu = new Sprachregel
        {
            Art = alt.Art, Aussage = alt.Aussage, Stattdessen = alt.Stattdessen, Anlass = alt.Anlass,
            Begruendung = alt.Begruendung, BegruendungIntern = alt.BegruendungIntern,
            BeispielGut = alt.BeispielGut, BeispielSchlecht = alt.BeispielSchlecht, Varianten = alt.Varianten,
            Zielgruppen = alt.Zielgruppen, Thema = alt.Thema, Produkt = alt.Produkt, Kanaele = alt.Kanaele,
            KiHinweis = alt.KiHinweis,
            Status = SprachregelStatus.Entwurf,
            VorgaengerId = alt.Id,
            ErstelltAm = DateTime.UtcNow,
            ErstelltVonId = m.Id,
        };
        db.Sprachregeln.Add(neu);
        await db.SaveChangesAsync(ct);
        return Ok(neu.Id);
    }

    /// <summary>
    /// Löschen: Entwürfe der Ersteller oder Admin/Compliance; zurückgezogene Regeln
    /// nur Admin/Compliance. Eine geltende Regel wird zurückgezogen, nicht gelöscht.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Loeschen(int id, CancellationToken ct)
    {
        var r = await db.Sprachregeln.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null)
            return NotFound();
        if (r.Status == SprachregelStatus.Freigegeben)
            return StatusCode(StatusCodes.Status409Conflict,
                "Eine geltende Regel wird nicht gelöscht — bitte zurückziehen, damit die Betroffenen davon erfahren.");
        if (await angemeldet.HolenAsync(ct) is not { Aktiv: true } m
            || !SprachregelRegeln.DarfLoeschen(m.Rollen, m.Id, r.ErstelltVonId, r.Status))
            return StatusCode(StatusCodes.Status403Forbidden,
                "Löschen dürfen Admin und Compliance — oder der Ersteller bei einem eigenen Entwurf.");
        db.Sprachregeln.Remove(r);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Export der freigegebenen, gültigen Regeln einer Zielgruppe:
    /// <c>format=prompt</c> (Standard) liefert den fertigen Textblock für den
    /// Systemprompt bzw. die Projektanweisungen von Claude, <c>format=json</c>
    /// dieselben Regeln strukturiert mit derselben Version. Der Text ist
    /// byte-stabil (<see cref="SprachregelExport"/>). KI-Export nur Admin und
    /// Compliance; <c>vertrieb</c>/<c>support</c> zusätzlich die jeweilige Rolle.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? zielgruppe, [FromQuery] string? format, CancellationToken ct)
    {
        if (SprachregelKatalog.Zielgruppe(zielgruppe) is not { } z)
            return BadRequest("Bitte eine Zielgruppe angeben: ki, vertrieb oder support.");
        if (format is not (null or "" or "prompt" or "json"))
            return BadRequest("Unbekanntes Format — möglich sind prompt und json.");
        if (await angemeldet.HolenAsync(ct) is not { Aktiv: true } m || !SprachregelRegeln.DarfExportieren(m.Rollen, z))
            return StatusCode(StatusCodes.Status403Forbidden,
                "Den Export dürfen Admin und Compliance abrufen — Vertrieb und Support nur den der eigenen Zielgruppe.");

        var regeln = await db.Sprachregeln.AsNoTracking()
            .Where(r => r.Status == SprachregelStatus.Freigegeben).ToListAsync(ct);
        var ergebnis = SprachregelExport.Erzeugen(regeln, z, DateOnly.FromDateTime(DateTime.Now));

        // Der Text verlässt mit dem Export das Haus — festhalten, wer ihn abgerufen hat
        log.LogInformation("Leitlinien-Export {Zielgruppe}/{Format}, Version {Version}, {Anzahl} Regeln, Mitarbeiter {MitarbeiterId}",
            zielgruppe, format ?? "prompt", ergebnis.Version, ergebnis.Anzahl, m.Id);
        Response.Headers.CacheControl = "no-store";

        if (format == "json")
            return Ok(new LeitlinienExport(
                SprachregelKatalog.ZielgruppenSchluessel(z).First(), ergebnis.Stand, ergebnis.Anzahl, ergebnis.Version,
                ergebnis.Regeln.Select(r => new LeitlinienExportRegel(
                    r.Id, r.Art.ToString(), r.Thema, SprachregelKatalog.ThemaName(r.Thema),
                    r.Produkt, r.Produkt is null ? null : FaqKatalog.ProduktName(r.Produkt),
                    SprachregelKatalog.KanalSchluessel(r.Kanaele), r.Aussage, LeitlinienSuche.Varianten(r.Varianten),
                    r.Stattdessen, r.Anlass, SprachregelExport.ExportBegruendung(r), r.BeispielGut, r.BeispielSchlecht,
                    r.KiHinweis, r.GueltigAb, r.FreigegebenAm)).ToList()));
        return Content(ergebnis.Text, "text/plain", Encoding.UTF8);
    }

    // --- Hilfen ----------------------------------------------------------------

    private IQueryable<Sprachregel> Basis() => db.Sprachregeln.AsNoTracking()
        .Include(r => r.ErstelltVon).Include(r => r.GeaendertVon)
        .Include(r => r.FreigegebenVon).Include(r => r.ZurueckgezogenVon);

    private static List<Sprachregel> Sortiert(IEnumerable<Sprachregel> regeln) => regeln
        .OrderBy(r => SprachregelKatalog.ArtRang(r.Art))
        .ThenBy(r => SprachregelKatalog.ThemaRang(r.Thema))
        .ThenBy(r => r.Status)
        .ThenBy(r => r.Id)
        .ToList();

    private static string? MeineZielgruppe(Dienste.MitarbeiterKontext? m)
    {
        if (m is not { Aktiv: true })
            return null;
        var vertrieb = m.Rollen.HasFlag(MitarbeiterRolle.Vertrieb);
        var support = m.Rollen.HasFlag(MitarbeiterRolle.Support);
        return vertrieb == support ? null : vertrieb ? "vertrieb" : "support";
    }

    /// <summary>
    /// Optimistische Sperre: Der Aufrufer nennt den Stand, den er gesehen hat. Hat
    /// sich der Datensatz seither geändert, scheitert das Speichern als 409
    /// (DatenbankFehlerHandler) — niemand gibt einen Wortlaut frei, den er nicht kennt.
    /// </summary>
    private bool GesehenenStandSetzen(Sprachregel r, string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return false;
        try
        {
            db.Entry(r).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(version);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Regel zurückziehen; die Bestätigungspflicht ihrer Freigabe-Neuerung endet damit.</summary>
    private async Task ZurueckziehenAsync(Sprachregel r, int vonId, DateTime jetzt, CancellationToken ct)
    {
        r.Status = SprachregelStatus.Zurueckgezogen;
        r.ZurueckgezogenAm = jetzt;
        r.ZurueckgezogenVonId = vonId;
        var offene = await db.WikiNeuerungen
            .Where(n => n.SprachregelId == r.Id && n.BestaetigungErforderlich).ToListAsync(ct);
        foreach (var n in offene)
        {
            n.BestaetigungErforderlich = false;
            n.GeaendertAm = jetzt;
        }
    }

    private void NeuerungAnlegen(Sprachregel r, (string Titel, string Text) inhalt, MitarbeiterRolle zielrollen,
        bool bestaetigung, int vonId, DateTime jetzt) =>
        db.WikiNeuerungen.Add(new WikiNeuerung
        {
            Thema = WikiThema.Leitlinien,
            Titel = inhalt.Titel,
            Text = inhalt.Text,
            BestaetigungErforderlich = bestaetigung,
            Zielrollen = zielrollen,
            Sprachregel = r,
            ErstelltAm = jetzt,
            ErstelltVonId = vonId,
        });

    /// <summary>Freigabe-Neuerungen (mit Bestätigungspflicht) der Regeln samt Lesungen und dem Kreis der Leser.</summary>
    private async Task<(Dictionary<int, WikiNeuerung> Neuerungen, List<Dienste.WikiLeser> Leser)> LesestandLadenAsync(
        List<int> regelIds, CancellationToken ct)
    {
        if (regelIds.Count == 0)
            return ([], []);
        var neuerungen = await db.WikiNeuerungen.AsNoTracking().Include(n => n.Lesungen)
            .Where(n => n.SprachregelId != null && regelIds.Contains(n.SprachregelId.Value) && n.BestaetigungErforderlich)
            .ToListAsync(ct);
        if (neuerungen.Count == 0)
            return ([], []);
        // Je Regel gibt es höchstens eine Freigabe mit Bestätigungspflicht; die jüngste zählt
        var jeRegel = neuerungen.GroupBy(n => n.SprachregelId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(n => n.ErstelltAm).First());
        return (jeRegel, await Dienste.WikiLesestand.LeserLadenAsync(db, ct));
    }

    private static string? Uebernehmen(Sprachregel r, LeitlinieSpeichern d)
    {
        if (!Enum.TryParse<SprachregelArt>(d.Art, true, out var art) || !Enum.IsDefined(art))
            return "Bitte eine Art wählen.";
        if (SprachregelKatalog.ZielgruppenAus(d.Zielgruppen) is not { } zielgruppen)
            return "Unbekannte Zielgruppe.";
        if (SprachregelKatalog.KanaeleAus(d.Kanaele) is not { } kanaele)
            return "Unbekannter Kanal.";
        if (!SprachregelKatalog.IstThema(d.Thema))
            return "Bitte ein Thema aus der Liste wählen.";
        var produkt = Leer(d.Produkt);
        if (produkt is not null && !FaqKatalog.IstProdukt(produkt))
            return "Bitte ein Produkt aus der Liste wählen oder das Feld leer lassen.";

        // Alternative gibt es nur bei „Besser vermeiden“ und „Niemals“, einen Anlass nur beim Pflichthinweis
        var stattdessen = art is SprachregelArt.Vermeiden or SprachregelArt.Verboten ? Leer(d.Stattdessen) : null;
        var anlass = art == SprachregelArt.Pflichthinweis ? Leer(d.Anlass) : null;
        var begruendung = Leer(d.Begruendung);
        if (SprachregelRegeln.Pruefen(art, d.Aussage, stattdessen, anlass, begruendung, zielgruppen) is { } fehler)
            return fehler;

        var varianten = LeitlinienSuche.VariantenSpeichern(d.Varianten);
        foreach (var (name, wert, max) in new (string, string?, int)[]
                 {
                     ("Die Aussage", d.Aussage.Trim(), 500), ("Die Alternative", stattdessen, 500), ("Der Anlass", anlass, 500),
                     ("Das gute Beispiel", Leer(d.BeispielGut), 1000), ("Das schlechte Beispiel", Leer(d.BeispielSchlecht), 1000),
                     ("Die Liste der Varianten", varianten, 500), ("Der Hinweis für Claude", Leer(d.KiHinweis), 500),
                 })
            if (wert is not null && wert.Length > max)
                return $"{name} ist zu lang (höchstens {max} Zeichen).";

        r.Art = art;
        r.Aussage = d.Aussage.Trim();
        r.Stattdessen = stattdessen;
        r.Anlass = anlass;
        r.Begruendung = begruendung;
        r.BegruendungIntern = d.BegruendungIntern && begruendung is not null;
        r.BeispielGut = Leer(d.BeispielGut);
        r.BeispielSchlecht = Leer(d.BeispielSchlecht);
        r.Varianten = varianten;
        r.Zielgruppen = zielgruppen;
        r.Thema = d.Thema;
        r.Produkt = produkt;
        r.Kanaele = kanaele;
        r.KiHinweis = Leer(d.KiHinweis);
        r.GueltigAb = d.GueltigAb;
        return null;
    }

    private static string? Leer(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static LeitlinieDto NachDto(Sprachregel r, Dienste.MitarbeiterKontext? m,
        (Dictionary<int, WikiNeuerung> Neuerungen, List<Dienste.WikiLeser> Leser) lesestand)
    {
        var aktiv = m is { Aktiv: true };
        var darfFreigeben = aktiv && SprachregelRegeln.DarfFreigeben(m!.Rollen);

        LeitlinieLesung? meine = null;
        LeitlinieBestaetigungsstand? stand = null;
        if (r.Status == SprachregelStatus.Freigegeben && lesestand.Neuerungen.TryGetValue(r.Id, out var n))
        {
            if (aktiv && WikiNeuerung.Betrifft(n.Zielrollen, m!.Rollen))
            {
                var l = n.Lesungen.FirstOrDefault(x => x.MitarbeiterId == m.Id);
                meine = new LeitlinieLesung(n.Id, l?.GesehenAm, l?.BestaetigtAm);
            }
            if (darfFreigeben)
            {
                var auswertung = Dienste.WikiLesestand.Auswerten(n, lesestand.Leser);
                stand = new LeitlinieBestaetigungsstand(n.Id, auswertung.Gesamt, auswertung.Bestaetigt, auswertung.Lesungen);
            }
        }

        return new LeitlinieDto(
            r.Id, r.Art.ToString(), r.Aussage, r.Stattdessen, r.Anlass, r.Begruendung, r.BegruendungIntern,
            r.BeispielGut, r.BeispielSchlecht, LeitlinienSuche.Varianten(r.Varianten),
            SprachregelKatalog.ZielgruppenSchluessel(r.Zielgruppen),
            r.Thema, SprachregelKatalog.ThemaName(r.Thema),
            r.Produkt, r.Produkt is null ? null : FaqKatalog.ProduktName(r.Produkt),
            SprachregelKatalog.KanalSchluessel(r.Kanaele), r.KiHinweis,
            r.Status.ToString(), r.GueltigAb, r.VorgaengerId,
            r.ErstelltAm, r.ErstelltVon?.Anzeigename, r.GeaendertAm, r.GeaendertVon?.Anzeigename,
            r.FreigegebenAm, r.FreigegebenVon?.Anzeigename, r.ZurueckgezogenAm, r.ZurueckgezogenVon?.Anzeigename,
            r.RowVersion is null ? "" : Convert.ToBase64String(r.RowVersion),
            aktiv && SprachregelRegeln.DarfBearbeiten(m!.Rollen, m.Id, r.ErstelltVonId, r.Status),
            aktiv && SprachregelRegeln.DarfLoeschen(m!.Rollen, m.Id, r.ErstelltVonId, r.Status),
            meine, stand);
    }
}
