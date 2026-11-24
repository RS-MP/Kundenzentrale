using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Api.Dienste;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Margen;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Margenrechner (World-Check, Full File und Sanctions Only). Jeder Endpunkt
/// hier verlangt die Rolle „Margenrechner" (Policy an der Klasse) — ohne sie
/// gibt es weder Ergebnisse noch Daten, nur 403. Gerechnet wird auf dem
/// Server (<see cref="Margenrechnung"/>). Die Einkaufsstaffel wird von Hand
/// gepflegt und liegt verschlüsselt in der Datenbank; Verkaufspreise kommen aus
/// der Wiki-Preisliste und nur ersatzweise aus der Datenpflege
/// (<see cref="MargenVerkaufspreise"/>).
/// Antworten dürfen nicht im Browser-Cache landen.
/// </summary>
[ApiController]
[Route("api/margenrechner")]
[Authorize(Policy = MargenrechnerAnforderung.Policy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class MargenrechnerController(ErpDbContext db, MargenrechnerAblage ablage, AngemeldeterMitarbeiter angemeldet)
    : ControllerBase
{
    /// <summary>Für das Öffnen der Seite: Baskets der Eingabetabelle und Beispielbestand — keine Preise.</summary>
    [HttpGet]
    public async Task<ActionResult<MargenrechnerStart>> Start(CancellationToken ct)
    {
        var stand = await ablage.LadenAsync(ct);
        if (stand.Daten is not { } daten)
            return Ok(new MargenrechnerStart(false, stand.Hinweis, [], [], [], stand.GeaendertAm, stand.GeaendertVon));

        var baskets = daten.Baskets.Select(b => b.Cases).ToList();
        var (_, herkunft) = MargenVerkaufspreise.Anwenden(daten, await WikiPreiseAsync(ct));
        return Ok(new MargenrechnerStart(true, null, baskets,
            [.. MargenBeispiele.Beispiel(baskets, MargenBeispiele.BeispielSanctionsOnly)],
            [.. MargenBeispiele.Beispiel(baskets, MargenBeispiele.BeispielFullFile)],
            stand.GeaendertAm, stand.GeaendertVon, herkunft));
    }

    /// <summary>
    /// Rechnet mit den Verkaufspreisen der aktuellen Wiki-Preisliste; nur wo sie für Basket
    /// und Bereich keinen Preis hat, gilt der in der Datenpflege eingetragene.
    /// </summary>
    [HttpPost("berechnen")]
    [RequestSizeLimit(100_000)]
    public async Task<ActionResult<MargenErgebnis>> Berechnen(MargenEingabe eingabe, CancellationToken ct)
    {
        var stand = await ablage.LadenAsync(ct);
        if (stand.Daten is not { } gepflegt)
            return Conflict(stand.Hinweis);
        var (daten, _) = MargenVerkaufspreise.Anwenden(gepflegt, await WikiPreiseAsync(ct));
        return Ok(new Margenrechnung(daten).Berechnen(eingabe));
    }

    /// <summary>
    /// Verkaufte World-Check-Lizenzen aus den laufenden Abos (Odoo-Import) als Bestand:
    /// Bereich und Basket kommen aus dem Produktnamen der Position. Nur lesend.
    /// </summary>
    [HttpGet("lizenzen")]
    public async Task<ActionResult<MargenLizenzbestand>> Lizenzen(CancellationToken ct)
    {
        var stand = await ablage.LadenAsync(ct);
        if (stand.Daten is not { } daten)
            return Conflict(stand.Hinweis);

        // Grob in der Datenbank vorfiltern, genau erkennt MargenLizenzErkennung
        var positionen = await db.Abos.AsNoTracking()
            .Where(a => a.Status == Domain.Subscriptions.AboStatus.Laufend)
            .SelectMany(a => a.Positionen
                .Where(p => EF.Functions.Like(p.Produkt, "%World%Check%")
                            || EF.Functions.Like(p.Produkt, "%Sanction%Only%")
                            || EF.Functions.Like(p.Produkt, "%Full%File%"))
                .Select(p => new MargenLizenzbestandErmittlung.Position(
                    a.KundeId, a.Kunde!.Kundennummer, a.Kunde.Firmenname, a.Nummer, p.Produkt, p.Menge)))
            .ToListAsync(ct);
        var standImport = (await db.Einstellungen.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Schluessel == Domain.Shared.EinstellungsSchluessel.OdooLetzterImport, ct))?.Wert;

        return Ok(MargenLizenzbestandErmittlung.Ermitteln(
            daten.Baskets.Select(b => b.Cases).ToList(), positionen,
            string.IsNullOrWhiteSpace(standImport) ? null : standImport));
    }

    /// <summary>Datenpflege: der gespeicherte Stand (so wie eingetragen), dazu die Verkaufspreise der aktuellen Wiki-Preisliste, die Vorrang haben.</summary>
    [HttpGet("daten")]
    public async Task<ActionResult<MargenDatenAnsicht>> Daten(CancellationToken ct)
    {
        var stand = await ablage.LadenAsync(ct);
        return Ok(new MargenDatenAnsicht(stand.Daten, stand.Hinweis, stand.GeaendertAm, stand.GeaendertVon,
            await WikiPreiseAsync(ct)));
    }

    [HttpPut("daten")]
    [RequestSizeLimit(500_000)]
    public async Task<IActionResult> DatenSpeichern(MargenDaten daten, CancellationToken ct)
    {
        daten = daten with { Stand = Leer(daten.Stand), Hinweis = Leer(daten.Hinweis) };
        if (daten.Pruefen() is { Count: > 0 } fehler)
            return BadRequest(string.Join(" ", fehler));
        await ablage.SpeichernAsync(daten, await angemeldet.IdAsync(ct), ct);
        return NoContent();
    }

    /// <summary>
    /// Liest die Zahlen aus der HTML-Kalkulationsvorlage und gibt sie zur Kontrolle
    /// zurück. Gespeichert wird erst mit „Speichern" in der Datenpflege.
    /// </summary>
    [HttpPost("daten/aus-vorlage")]
    [RequestSizeLimit(MargenVorlageLeser.MaxLaenge + 100_000)]
    public async Task<ActionResult<MargenDaten>> AusVorlage(IFormFile datei, CancellationToken ct)
    {
        if (datei.Length == 0)
            return BadRequest("Die Datei ist leer.");
        if (datei.Length > MargenVorlageLeser.MaxLaenge)
            return BadRequest("Die Datei ist zu groß für eine Kalkulationsvorlage.");

        string html;
        using (var leser = new StreamReader(datei.OpenReadStream(), System.Text.Encoding.UTF8))
            html = await leser.ReadToEndAsync(ct);

        var daten = MargenVorlageLeser.Lesen(html, out var fehler);
        return daten is null ? BadRequest(fehler) : Ok(daten);
    }

    private static readonly System.Text.Json.JsonSerializerOptions PreislisteJson = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>World-Check-Verkaufspreise je Monat aus der aktuellen Wiki-Preisliste (Pakete „sanctions-only" und „full-file").</summary>
    private async Task<MargenWikiPreise?> WikiPreiseAsync(CancellationToken ct)
    {
        var aktuelle = await db.PreislistenVersionen.AsNoTracking()
            .OrderByDescending(p => p.Version)
            .Select(p => new { p.Version, p.Stand, p.Daten })
            .FirstOrDefaultAsync(ct);
        if (aktuelle is null)
            return null;

        PreislisteDaten? p;
        try { p = System.Text.Json.JsonSerializer.Deserialize<PreislisteDaten>(aktuelle.Daten, PreislisteJson); }
        catch (System.Text.Json.JsonException) { return null; }
        if (p?.Worldcheck?.Pakete is not { } pakete)
            return null;

        Dictionary<long, double> Staffel(string schluessel) =>
            pakete.FirstOrDefault(x => string.Equals(x.Schluessel, schluessel, StringComparison.OrdinalIgnoreCase))?.Staffel
                .Where(s => s.PreisMonat?.Zahl is not null)
                .GroupBy(s => s.Stammdaten)
                .ToDictionary(g => g.Key, g => (double)g.First().PreisMonat!.Zahl!.Value)
            ?? [];

        var sanctionsOnly = Staffel("sanctions-only");
        var fullFile = Staffel("full-file");
        var preise = sanctionsOnly.Keys.Union(fullFile.Keys).OrderBy(k => k)
            .Select(k => new MargenWikiPreis(k,
                sanctionsOnly.TryGetValue(k, out var s) ? s : null,
                fullFile.TryGetValue(k, out var f) ? f : null))
            .ToList();
        return preise.Count == 0 ? null : new MargenWikiPreise(aktuelle.Version, aktuelle.Stand, preise);
    }

    private static string? Leer(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>
/// Einziger Margenrechner-Endpunkt für alle Mitarbeiter: sagt der Navigation,
/// ob der Menüpunkt erscheint. Verrät nichts außer Ja/Nein für den Fragenden
/// selbst; die eigentliche Sperre ist die Policy am <see cref="MargenrechnerController"/>.
/// </summary>
[ApiController]
[Route("api/margenrechner-zugang")]
public class MargenrechnerZugangController(AngemeldeterMitarbeiter angemeldet) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MargenrechnerZugangInfo>> Zugang(CancellationToken ct) =>
        Ok(new MargenrechnerZugangInfo(MargenrechnerZugang.Darf(await angemeldet.RollenAsync(ct))));
}
