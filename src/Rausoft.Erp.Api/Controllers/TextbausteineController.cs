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
            .Where(t => t.EntferntAm == null && (admin || t.Aktiv))
            .OrderBy(t => t.Verwendung).ThenBy(t => t.Name)
            .Select(t => new TextbausteinZeile(t.Id, t.Name, t.Betreff, t.Verwendung, t.Aktiv, t.OdooId != null,
                t.LokalGeaendert, t.Hinweise != null, t.GeaendertAm, t.Anhaenge.Count))
            .ToListAsync(ct);
        var entfernt = !admin ? null : await db.Textbausteine.AsNoTracking()
            .Where(t => t.EntferntAm != null)
            .OrderBy(t => t.Name)
            .Select(t => new TextbausteinEntfernt(t.Id, t.Name, t.Verwendung, t.EntferntAm!.Value,
                t.EntferntVon != null ? t.EntferntVon.Anzeigename : null))
            .ToListAsync(ct);
        return Ok(new TextbausteinUebersicht(bausteine, admin, entfernt));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TextbausteinDetail>> Detail(int id, CancellationToken ct)
    {
        var admin = await angemeldet.HatRolleAsync(MitarbeiterRolle.Admin, ct);
        var t = await db.Textbausteine.AsNoTracking().Include(x => x.GeaendertVon)
            .FirstOrDefaultAsync(x => x.Id == id && x.EntferntAm == null && (admin || x.Aktiv), ct);
        if (t is null)
            return NotFound();
        // Nur die Beschreibung der Dateien — der Inhalt wird erst beim Herunterladen gelesen
        var anhaenge = await db.TextbausteinAnhaenge.AsNoTracking()
            .Where(a => a.TextbausteinId == id)
            .OrderBy(a => a.Dateiname)
            .Select(a => new TextbausteinAnhangZeile(a.Id, a.Dateiname, a.Groesse))
            .ToListAsync(ct);
        return Ok(new TextbausteinDetail(t.Id, t.Name, t.Betreff, t.InhaltHtml, t.Verwendung,
            (t.Hinweise ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList(),
            t.Aktiv, t.OdooId is not null, t.LokalGeaendert, t.GeaendertAm, t.GeaendertVon?.Anzeigename, anhaenge));
    }

    /// <summary>
    /// Datei eines Bausteins. Immer als Download mit neutralem Inhaltstyp — der Browser stellt
    /// nichts davon selbst dar. Sichtbar für alle, die den Baustein sehen.
    /// </summary>
    [HttpGet("anhaenge/{anhangId:int}/datei")]
    public async Task<IActionResult> AnhangDatei(int anhangId, CancellationToken ct)
    {
        var admin = await angemeldet.HatRolleAsync(MitarbeiterRolle.Admin, ct);
        var a = await db.TextbausteinAnhaenge.AsNoTracking()
            .Where(x => x.Id == anhangId && x.Textbaustein!.EntferntAm == null && (admin || x.Textbaustein.Aktiv))
            .Select(x => new { x.Dateiname, x.Inhalt })
            .FirstOrDefaultAsync(ct);
        return a is null ? NotFound() : File(a.Inhalt, "application/octet-stream", a.Dateiname);
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
        var t = await db.Textbausteine.FirstOrDefaultAsync(x => x.Id == id && x.EntferntAm == null, ct);
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

    /// <summary>
    /// Baustein entfernen. Ein hier angelegter wird gelöscht. Ein aus Odoo übernommener bleibt
    /// als Merker stehen (unsichtbar, ohne seine Dateien), damit die nächste Übernahme ihn nicht
    /// wieder anlegt — ein Admin kann ihn wiederherstellen.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Loeschen(int id, CancellationToken ct)
    {
        var t = await db.Textbausteine.Include(x => x.Anhaenge).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null)
            return NotFound();
        if (t.OdooId is null)
        {
            db.Textbausteine.Remove(t);
        }
        else if (t.EntferntAm is null)
        {
            t.EntferntAm = DateTime.UtcNow;
            t.EntferntVonId = await angemeldet.IdAsync(ct);
            // Die Dateien kommen nach dem Wiederherstellen mit der nächsten Übernahme zurück
            db.TextbausteinAnhaenge.RemoveRange(t.Anhaenge);
        }
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:int}/wiederherstellen")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Wiederherstellen(int id, CancellationToken ct)
    {
        var t = await db.Textbausteine.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null)
            return NotFound();
        t.EntferntAm = null;
        t.EntferntVonId = null;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Mailvorlagen aus Odoo übernehmen (nur lesend, wiederholbar über die Odoo-Nummer der Vorlage).
    /// Hier bearbeitete Bausteine behalten ihren Text, entfernte bleiben entfernt. Die Dateien,
    /// die Odoo an eine Vorlage hängt, werden unabhängig vom Text abgeglichen
    /// (<see cref="AnhangAbgleich"/>). Mit <paramref name="probe"/> wird nur gezählt.
    /// </summary>
    [HttpPost("odoo-uebernahme")]
    [Authorize(Policy = "Admin")]
    public async Task<ActionResult<TextbausteinUebernahme>> OdooUebernahme(
        [FromQuery] bool probe, [FromServices] IServiceScopeFactory scopeFactory, [FromServices] IConfiguration konfiguration,
        [FromServices] ILogger<TextbausteineController> log, CancellationToken ct)
    {
        IOdooAufruf odoo;
        OdooMailVorlagen gelesen;
        try
        {
            (odoo, _) = await OdooImportDienst.VerbindenAsync(scopeFactory, konfiguration);
            gelesen = await MailVorlagenLeser.LadenAsync(odoo).WaitAsync(TimeSpan.FromSeconds(60), ct);
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
        var vorlagen = gelesen.Vorlagen;

        var vorhanden = await db.Textbausteine.Where(t => t.OdooId != null).ToDictionaryAsync(t => t.OdooId!.Value, ct);
        var anhaengeVorhanden = (await db.TextbausteinAnhaenge.AsNoTracking()
                .Select(a => new { a.Id, a.TextbausteinId, a.OdooId, a.OdooStand })
                .ToListAsync(ct))
            .ToLookup(a => a.TextbausteinId);
        var ich = await angemeldet.IdAsync(ct);
        int neu = 0, aktualisiert = 0, unveraendert = 0, lokal = 0, mitHinweisen = 0, entfernt = 0, anhaengeNeu = 0, anhaengeEntfernt = 0;
        var anhangHinweise = new List<string>();
        foreach (var v in vorlagen)
        {
            vorhanden.TryGetValue(v.Id, out var t);
            if (t?.EntferntAm is not null)
            {
                entfernt++;
                continue;
            }

            // Text
            if (t is { LokalGeaendert: true })
            {
                lokal++;
            }
            else if (t is not null && t.OdooStand == v.Stand)
            {
                unveraendert++;
            }
            else
            {
                var umgesetzt = OdooMailvorlage.Umsetzen(v.Betreff, v.BodyHtml);
                if (umgesetzt.Hinweise.Count > 0)
                    mitHinweisen++;
                if (t is null) neu++; else aktualisiert++;
                if (!probe)
                {
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
            }

            // Dateien — unabhängig davon, ob der Text angefasst wurde
            if (!gelesen.AnhaengeLesbar)
                continue;
            var plan = AnhangAbgleich.Planen(
                t is null || t.Id == 0 ? [] : anhaengeVorhanden[t.Id].Select(a => (a.Id, a.OdooId, a.OdooStand)),
                v.Anhaenge);
            anhangHinweise.AddRange(plan.Hinweise.Select(h => $"{v.Name}: {h}"));
            anhaengeEntfernt += plan.EntfernenIds.Count;
            if (probe || t is null)
            {
                anhaengeNeu += plan.Laden.Count;
                continue;
            }
            if (plan.EntfernenIds.Count > 0)
                db.TextbausteinAnhaenge.RemoveRange(
                    await db.TextbausteinAnhaenge.Where(a => plan.EntfernenIds.Contains(a.Id)).ToListAsync(ct));
            var geladen = new List<TextbausteinAnhang>();
            foreach (var a in plan.Laden)
            {
                byte[] inhalt;
                try
                {
                    inhalt = await MailVorlagenLeser.InhaltAsync(odoo, a.OdooId).WaitAsync(TimeSpan.FromSeconds(120), ct);
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TimeoutException or TaskCanceledException
                                           && !ct.IsCancellationRequested)
                {
                    log.LogWarning("Mailvorlagen: Anhang {Anhang} nicht ladbar ({Art})", a.OdooId, ex is OdooFehlerException f ? f.Art : ex.GetType().Name);
                    anhangHinweise.Add($"{v.Name}: „{a.Name}“ konnte nicht aus Odoo geladen werden.");
                    continue;
                }
                if (inhalt.Length == 0 || inhalt.LongLength > AnhangAbgleich.MaxBytes)
                {
                    anhangHinweise.Add(inhalt.Length == 0
                        ? $"{v.Name}: „{a.Name}“ hat in Odoo keinen Inhalt."
                        : $"{v.Name}: „{a.Name}“ ist zu groß und wurde nicht übernommen.");
                    continue;
                }
                var anhang = new TextbausteinAnhang
                {
                    Dateiname = AnhangAbgleich.Dateiname(a), Groesse = inhalt.LongLength, Inhalt = inhalt,
                    OdooId = a.OdooId, OdooStand = AnhangAbgleich.Stand(a), UebernommenAm = DateTime.UtcNow,
                };
                t.Anhaenge.Add(anhang);
                geladen.Add(anhang);
                anhaengeNeu++;
            }
            if (geladen.Count > 0)
            {
                // Je Baustein speichern und die Dateien wieder loslassen — sonst läge bis zum Ende alles im Speicher
                await db.SaveChangesAsync(ct);
                foreach (var anhang in geladen)
                    db.Entry(anhang).State = EntityState.Detached;
                t.Anhaenge.Clear();
            }
        }
        if (!probe)
            await db.SaveChangesAsync(ct);
        return Ok(new TextbausteinUebernahme(probe, vorlagen.Count, neu, aktualisiert, unveraendert, lokal, mitHinweisen,
            vorlagen.Count >= MailVorlagenLeser.MaxVorlagen ? "Odoo hat mehr Vorlagen, als auf einmal gelesen werden — die Liste ist abgeschnitten."
            : !gelesen.AnhaengeLesbar ? "Die Anhänge der Vorlagen darf der Odoo-Benutzer der Anbindung nicht lesen (Modell ir.attachment) — übernommen wurden nur die Texte."
            : null,
            entfernt, anhaengeNeu, anhaengeEntfernt, anhangHinweise));
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
