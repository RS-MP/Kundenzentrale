using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Api.Dienste;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Dtos;
using Rausoft.Erp.OdooImport;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// E-Mail-Textbausteine: lesen darf jeder aktive Mitarbeiter (nur aktive Bausteine), pflegen
/// und aus Odoo übernehmen nur ein Admin. Der formatierte Text wird beim Speichern und bei
/// der Übernahme gesäubert (<see cref="OdooMailvorlage.Saeubern"/>).
/// </summary>
[ApiController]
[Route("api/textbausteine")]
public class TextbausteineController(ErpDbContext db, AngemeldeterMitarbeiter angemeldet) : ControllerBase
{
    private const int MaxHtml = 500_000;

    [HttpGet]
    public async Task<ActionResult<TextbausteinUebersicht>> Liste(CancellationToken ct)
    {
        var admin = await angemeldet.HatRolleAsync(MitarbeiterRolle.Admin, ct);
        var bausteine = await db.Textbausteine.AsNoTracking()
            .Where(t => admin || t.Aktiv)
            .OrderBy(t => t.Verwendung).ThenBy(t => t.Name)
            .Select(t => new TextbausteinZeile(t.Id, t.Name, t.Betreff, t.Verwendung, t.Aktiv, t.OdooId != null,
                t.LokalGeaendert, t.Hinweise != null, t.GeaendertAm))
            .ToListAsync(ct);
        return Ok(new TextbausteinUebersicht(bausteine, admin));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TextbausteinDetail>> Detail(int id, CancellationToken ct)
    {
        var admin = await angemeldet.HatRolleAsync(MitarbeiterRolle.Admin, ct);
        var t = await db.Textbausteine.AsNoTracking().Include(x => x.GeaendertVon)
            .FirstOrDefaultAsync(x => x.Id == id && (admin || x.Aktiv), ct);
        if (t is null)
            return NotFound();
        return Ok(new TextbausteinDetail(t.Id, t.Name, t.Betreff, t.InhaltHtml, t.Verwendung,
            (t.Hinweise ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList(),
            t.Aktiv, t.OdooId is not null, t.LokalGeaendert, t.GeaendertAm, t.GeaendertVon?.Anzeigename));
    }

    [HttpPost]
    [Authorize(Policy = "Admin")]
    public async Task<ActionResult<int>> Anlegen(TextbausteinSpeichern daten, CancellationToken ct)
    {
        if (Pruefen(daten) is { } fehler)
            return BadRequest(fehler);
        var t = new Textbaustein { Name = daten.Name.Trim() };
        Uebernehmen(t, daten, await angemeldet.IdAsync(ct));
        db.Textbausteine.Add(t);
        await db.SaveChangesAsync(ct);
        return Ok(t.Id);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Aendern(int id, TextbausteinSpeichern daten, CancellationToken ct)
    {
        if (Pruefen(daten) is { } fehler)
            return BadRequest(fehler);
        var t = await db.Textbausteine.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null)
            return NotFound();
        var inhaltGeaendert = t.Betreff != (daten.Betreff ?? "").Trim() || t.InhaltHtml != OdooMailvorlage.Saeubern(daten.InhaltHtml);
        Uebernehmen(t, daten, await angemeldet.IdAsync(ct));
        // Ab jetzt gilt der hier gepflegte Text — die Übernahme aus Odoo überschreibt ihn nicht mehr
        if (t.OdooId is not null && inhaltGeaendert)
            t.LokalGeaendert = true;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Loeschen(int id, CancellationToken ct)
    {
        var t = await db.Textbausteine.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null)
            return NotFound();
        if (t.OdooId is not null)
            return BadRequest("Aus Odoo übernommene Bausteine kämen bei der nächsten Übernahme wieder — bitte stattdessen deaktivieren.");
        db.Textbausteine.Remove(t);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Mailvorlagen aus Odoo übernehmen (nur lesend, wiederholbar über die Odoo-Nummer der Vorlage).
    /// Hier bearbeitete Bausteine bleiben unberührt. Mit <paramref name="probe"/> wird nur gezählt.
    /// </summary>
    [HttpPost("odoo-uebernahme")]
    [Authorize(Policy = "Admin")]
    public async Task<ActionResult<TextbausteinUebernahme>> OdooUebernahme(
        [FromQuery] bool probe, [FromServices] IServiceScopeFactory scopeFactory, [FromServices] IConfiguration konfiguration,
        [FromServices] ILogger<TextbausteineController> log, CancellationToken ct)
    {
        List<OdooMailVorlage> vorlagen;
        try
        {
            var (odoo, _) = await OdooImportDienst.VerbindenAsync(scopeFactory, konfiguration);
            vorlagen = await MailVorlagenLeser.LadenAsync(odoo).WaitAsync(TimeSpan.FromSeconds(60), ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Odoo-Zugangsdaten unvollständig"))
        {
            return Ok(new TextbausteinUebernahme(probe, 0, 0, 0, 0, 0, 0, "Odoo ist nicht eingerichtet (Administration → Odoo-Anbindung)."));
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TimeoutException or TaskCanceledException)
        {
            log.LogWarning("Mailvorlagen: Odoo nicht erreichbar ({Art})", ex is OdooFehlerException o ? o.Art : ex.GetType().Name);
            return Ok(new TextbausteinUebernahme(probe, 0, 0, 0, 0, 0, 0,
                ex is OdooFehlerException { Art: "odoo.exceptions.AccessError" }
                    ? "Der Odoo-Benutzer der Anbindung darf die Mailvorlagen nicht lesen (Modell mail.template)."
                    : "Odoo ist nicht erreichbar oder hat die Abfrage abgelehnt. Einzelheiten stehen im Server-Log."));
        }

        var vorhanden = await db.Textbausteine.Where(t => t.OdooId != null).ToDictionaryAsync(t => t.OdooId!.Value, ct);
        var ich = await angemeldet.IdAsync(ct);
        int neu = 0, aktualisiert = 0, unveraendert = 0, lokal = 0, mitHinweisen = 0;
        foreach (var v in vorlagen)
        {
            vorhanden.TryGetValue(v.Id, out var t);
            if (t is { LokalGeaendert: true })
            {
                lokal++;
                continue;
            }
            if (t is not null && t.OdooStand == v.Stand)
            {
                unveraendert++;
                continue;
            }
            var umgesetzt = OdooMailvorlage.Umsetzen(v.Betreff, v.BodyHtml);
            if (umgesetzt.Hinweise.Count > 0)
                mitHinweisen++;
            if (t is null) neu++; else aktualisiert++;
            if (probe)
                continue;
            if (t is null)
                db.Textbausteine.Add(t = new Textbaustein { Name = "", OdooId = v.Id });
            t.Name = VorgangsHilfen.Kuerzen(v.Name, 200);
            t.Betreff = VorgangsHilfen.Kuerzen(umgesetzt.Betreff, 500);
            t.InhaltHtml = umgesetzt.Html.Length <= MaxHtml ? umgesetzt.Html : "";
            t.OdooModell = v.Modell is null ? null : VorgangsHilfen.Kuerzen(v.Modell, 100);
            t.Verwendung = OdooMailvorlage.Verwendung(v.Modell) is { } verwendung ? VorgangsHilfen.Kuerzen(verwendung, 100) : null;
            t.Hinweise = umgesetzt.Html.Length > MaxHtml
                ? "Der Text der Odoo-Vorlage ist zu groß und wurde nicht übernommen."
                : umgesetzt.Hinweise.Count > 0 ? string.Join('\n', umgesetzt.Hinweise) : null;
            t.OdooStand = v.Stand;
            t.GeaendertAm = DateTime.UtcNow;
            t.GeaendertVonId = ich;
        }
        if (!probe)
            await db.SaveChangesAsync(ct);
        return Ok(new TextbausteinUebernahme(probe, vorlagen.Count, neu, aktualisiert, unveraendert, lokal, mitHinweisen,
            vorlagen.Count >= MailVorlagenLeser.MaxVorlagen ? "Odoo hat mehr Vorlagen, als auf einmal gelesen werden — die Liste ist abgeschnitten." : null));
    }

    private static string? Pruefen(TextbausteinSpeichern daten) =>
        string.IsNullOrWhiteSpace(daten.Name) ? "Bitte einen Namen angeben."
        : daten.Name.Trim().Length > 200 ? "Der Name ist zu lang (höchstens 200 Zeichen)."
        : (daten.Betreff ?? "").Trim().Length > 500 ? "Der Betreff ist zu lang (höchstens 500 Zeichen)."
        : (daten.InhaltHtml ?? "").Length > MaxHtml ? "Der Text ist zu groß."
        : (daten.Verwendung ?? "").Trim().Length > 100 ? "Die Verwendung ist zu lang (höchstens 100 Zeichen)."
        : null;

    private static void Uebernehmen(Textbaustein t, TextbausteinSpeichern daten, int? ich)
    {
        t.Name = daten.Name.Trim();
        t.Betreff = (daten.Betreff ?? "").Trim();
        t.InhaltHtml = OdooMailvorlage.Saeubern(daten.InhaltHtml);
        t.Verwendung = string.IsNullOrWhiteSpace(daten.Verwendung) ? null : daten.Verwendung.Trim();
        t.Aktiv = daten.Aktiv;
        t.GeaendertAm = DateTime.UtcNow;
        t.GeaendertVonId = ich;
    }
}
