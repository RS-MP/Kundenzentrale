using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Api.Dienste;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Servers;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// SSL-Zertifikate aller produktiven Systeme: Ablaufdaten, Erinnerung per
/// E-Mail ab dem Vorlauf (wiederholt bis zur Bestätigung), Live-Prüfung.
/// Lesen dürfen alle Mitarbeiter, pflegen und bestätigen Support und Admin.
/// </summary>
[ApiController]
[Route("api/zertifikate")]
public class ZertifikateController(ErpDbContext db, ZertifikatDienst dienst, MailDienst mail,
    IAuthorizationService autorisierung, IAktuellerBenutzer benutzer) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ZertifikatUebersicht>> Liste(CancellationToken ct)
    {
        var liste = await db.Zertifikate.AsNoTracking()
            .Include(z => z.Server).Include(z => z.ErneuertVon)
            .OrderBy(z => z.GueltigBis).ThenBy(z => z.Bezeichnung)
            .ToListAsync(ct);
        var server = await db.Server.AsNoTracking().OrderBy(s => s.Alias)
            .Select(s => new ServerAuswahl(s.Id, s.Alias)).ToListAsync(ct);
        var darf = (await autorisierung.AuthorizeAsync(User, null, new BetriebAnforderung())).Succeeded;
        return Ok(new ZertifikatUebersicht(
            liste.Select(NachEintrag).ToList(), darf, await mail.IstEingerichtetAsync(ct),
            await dienst.StandardEmpfaengerAsync(ct), await dienst.ErinnerungIntervallTageAsync(ct), server));
    }

    [Authorize(Policy = "Betrieb")]
    [HttpPost]
    public async Task<ActionResult<ZertifikatEintrag>> Anlegen(ZertifikatSpeichern daten, CancellationToken ct)
    {
        if (await PruefeAsync(daten, ct) is { } fehler)
            return BadRequest(fehler);
        var z = new Zertifikat { Bezeichnung = daten.Bezeichnung.Trim() };
        Uebernehmen(z, daten);
        db.Zertifikate.Add(z);
        await dienst.PruefeLiveAsync(z, ct);
        await db.SaveChangesAsync(ct);
        return Ok(NachEintrag(z));
    }

    [Authorize(Policy = "Betrieb")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Aendern(int id, ZertifikatSpeichern daten, CancellationToken ct)
    {
        if (await PruefeAsync(daten, ct) is { } fehler)
            return BadRequest(fehler);
        var z = await db.Zertifikate.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (z is null)
            return NotFound();
        var hostGeaendert = z.Hostname != daten.Hostname?.Trim() || z.Port != daten.Port;
        Uebernehmen(z, daten);
        if (hostGeaendert)
            await dienst.PruefeLiveAsync(z, ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [Authorize(Policy = "Betrieb")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Loeschen(int id, CancellationToken ct)
    {
        var z = await db.Zertifikate.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (z is null)
            return NotFound();
        db.Zertifikate.Remove(z);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Bestätigung „neues Zertifikat ist online" — beendet die Erinnerungen und setzt das neue Ablaufdatum.</summary>
    [Authorize(Policy = "Betrieb")]
    [HttpPost("{id:int}/erneuert")]
    public async Task<ActionResult<ZertifikatEintrag>> Erneuert(int id, ZertifikatErneuern daten, CancellationToken ct)
    {
        var z = await db.Zertifikate.Include(x => x.Server).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (z is null)
            return NotFound();
        if (daten.NeuesGueltigBis <= DateOnly.FromDateTime(DateTime.UtcNow))
            return BadRequest("Das neue Ablaufdatum muss in der Zukunft liegen.");
        var eigeneId = await db.Mitarbeiter
            .Where(Mitarbeiter.KontoFilter(benutzer.EntraObjectId, benutzer.WindowsKonto))
            .Select(m => (int?)m.Id).FirstOrDefaultAsync(ct);
        z.Erneuern(daten.NeuesGueltigBis, eigeneId, DateTime.UtcNow);
        await dienst.PruefeLiveAsync(z, ct);
        await db.SaveChangesAsync(ct);
        await db.Entry(z).Reference(x => x.ErneuertVon).LoadAsync(ct);
        return Ok(NachEintrag(z));
    }

    /// <summary>Live-Prüfung eines Zertifikats jetzt ausführen.</summary>
    [HttpPost("{id:int}/pruefen")]
    public async Task<ActionResult<ZertifikatEintrag>> Pruefen(int id, CancellationToken ct)
    {
        var z = await db.Zertifikate.Include(x => x.Server).Include(x => x.ErneuertVon).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (z is null)
            return NotFound();
        await dienst.PruefeLiveAsync(z, ct);
        await db.SaveChangesAsync(ct);
        return Ok(NachEintrag(z));
    }

    /// <summary>Erinnerungslauf sofort ausführen (sonst stündlich im Hintergrund).</summary>
    [Authorize(Policy = "Betrieb")]
    [HttpPost("erinnerungen")]
    public async Task<ActionResult<ErinnerungsLauf>> Erinnerungen(CancellationToken ct) =>
        Ok(await dienst.ErinnerungenAsync(ct));

    /// <summary>Testmail, um die SMTP-Einstellungen zu prüfen.</summary>
    [Authorize(Policy = "Admin")]
    [HttpPost("testmail")]
    public async Task<IActionResult> Testmail(TestmailSenden daten, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(daten.Empfaenger) || !daten.Empfaenger.Contains('@'))
            return BadRequest("Bitte eine gültige Empfängeradresse angeben.");
        try
        {
            await mail.SendenAsync([daten.Empfaenger.Trim()], "Testmail der Kundenzentrale",
                $"Diese Testmail wurde am {DateTime.Now:dd.MM.yyyy HH:mm} aus der Kundenzentrale gesendet. Der E-Mail-Versand funktioniert.", ct);
            return NoContent();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return BadRequest($"Versand fehlgeschlagen: {ex.Message}");
        }
    }

    // --- Hilfen ----------------------------------------------------------------

    private async Task<string?> PruefeAsync(ZertifikatSpeichern d, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(d.Bezeichnung))
            return "Die Bezeichnung darf nicht leer sein.";
        if (d.Port is < 1 or > 65535)
            return "Der Port muss zwischen 1 und 65535 liegen.";
        if (d.LaufzeitMonate is < 1 or > 120)
            return "Die Laufzeit muss zwischen 1 und 120 Monaten liegen.";
        if (d.VorlaufTage is < 1 or > 365)
            return "Der Vorlauf muss zwischen 1 und 365 Tagen liegen.";
        if (d.ServerId is { } sid && !await db.Server.AnyAsync(s => s.Id == sid, ct))
            return "Der gewählte Server existiert nicht.";
        var ungueltig = (d.Empfaenger ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(e => !e.Contains('@')).ToList();
        if (ungueltig.Count > 0)
            return $"Ungültige Empfängeradresse: {string.Join(", ", ungueltig)}";
        return null;
    }

    private static void Uebernehmen(Zertifikat z, ZertifikatSpeichern d)
    {
        z.Bezeichnung = d.Bezeichnung.Trim();
        z.Hostname = string.IsNullOrWhiteSpace(d.Hostname) ? null : d.Hostname.Trim();
        z.Port = d.Port;
        z.ServerId = d.ServerId;
        z.Anbieter = string.IsNullOrWhiteSpace(d.Anbieter) ? "IONOS" : d.Anbieter.Trim();
        z.LaufzeitMonate = d.LaufzeitMonate;
        z.GueltigBis = d.GueltigBis;
        z.VorlaufTage = d.VorlaufTage;
        z.Empfaenger = string.IsNullOrWhiteSpace(d.Empfaenger) ? null : d.Empfaenger.Trim();
        z.Notiz = string.IsNullOrWhiteSpace(d.Notiz) ? null : d.Notiz.Trim();
        z.Aktiv = d.Aktiv;
    }

    private static ZertifikatEintrag NachEintrag(Zertifikat z)
    {
        var heute = DateOnly.FromDateTime(DateTime.UtcNow);
        return new ZertifikatEintrag(
            z.Id, z.Bezeichnung, z.Hostname, z.Port, z.ServerId, z.Server?.Alias, z.Anbieter, z.LaufzeitMonate,
            z.GueltigBis, z.VorlaufTage, z.Empfaenger, z.Notiz, z.Aktiv,
            ZertifikatErinnerung.Status(z.GueltigBis, z.VorlaufTage, heute),
            ZertifikatErinnerung.TageBisAblauf(z.GueltigBis, heute),
            z.ErneuertAm, z.ErneuertVon?.Anzeigename,
            z.LetzteErinnerungAm, z.ErinnerungenGesendet, z.LetzterMailFehler,
            z.LiveGeprueftAm, z.LiveGueltigBis, z.LiveAussteller, z.LiveFehler,
            ZertifikatErinnerung.VorschlagNeuesGueltigBis(z.GueltigBis, z.LaufzeitMonate, z.LiveGueltigBis, heute));
    }
}
