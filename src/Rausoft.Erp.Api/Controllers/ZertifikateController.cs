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
/// E-Mail ab dem Vorlauf mit Eskalation (wiederholt bis erneuert), Live-Prüfung
/// mit automatischer Erkennung der Erneuerung, Verlauf. Lesen dürfen alle
/// Mitarbeiter, pflegen und bestätigen Support und Admin.
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
        var e = await dienst.EinstellungenAsync(ct);
        return Ok(new ZertifikatUebersicht(
            liste.Select(z => NachEintrag(z, e.EskalationTage)).ToList(), darf, await mail.IstEingerichtetAsync(ct),
            e.StandardEmpfaenger, e.EskalationEmpfaenger, e.AbgelaufenEmpfaenger, e.IntervallTage, e.EskalationTage,
            e.ServerAutomatisch, server, e.ErlaubtePorts, e.EmpfaengerDomaenen));
    }

    /// <summary>Für Abzeichen in Navigation und Server-Übersicht.</summary>
    [HttpGet("hinweise")]
    public async Task<ActionResult<ZertifikatHinweise>> Hinweise(CancellationToken ct) => Ok(await dienst.HinweiseAsync(ct));

    [HttpGet("{id:int}/ereignisse")]
    public async Task<ActionResult<List<ZertifikatEreignisEintrag>>> Ereignisse(int id, CancellationToken ct) =>
        Ok(await db.ZertifikatEreignisse.AsNoTracking()
            .Where(x => x.ZertifikatId == id)
            .OrderByDescending(x => x.Zeitpunkt).Take(200)
            .Select(x => new ZertifikatEreignisEintrag(x.Id, x.Zeitpunkt, x.Art, x.Text, x.Von != null ? x.Von.Anzeigename : null))
            .ToListAsync(ct));

    [Authorize(Policy = "Betrieb")]
    [HttpPost]
    public async Task<ActionResult<ZertifikatEintrag>> Anlegen(ZertifikatSpeichern daten, CancellationToken ct)
    {
        if (await PruefeAsync(daten, ct) is { } fehler)
            return BadRequest(fehler);
        var z = new Zertifikat { Bezeichnung = daten.Bezeichnung.Trim() };
        Uebernehmen(z, daten);
        z.Protokoll(ZertifikatEreignisArt.Angelegt, $"Angelegt, gültig bis {z.GueltigBis:dd.MM.yyyy}", await EigeneIdAsync(ct), DateTime.UtcNow);
        db.Zertifikate.Add(z);
        var e = await dienst.EinstellungenAsync(ct);
        await dienst.PruefeLiveAsync(z, e, ct);
        await db.SaveChangesAsync(ct);
        return Ok(NachEintrag(z, e.EskalationTage));
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
        var hostGeaendert = z.Hostname != (string.IsNullOrWhiteSpace(daten.Hostname) ? null : daten.Hostname.Trim()) || z.Port != daten.Port;
        var datumGeaendert = z.GueltigBis != daten.GueltigBis;
        Uebernehmen(z, daten);
        z.Protokoll(ZertifikatEreignisArt.Geaendert,
            datumGeaendert ? $"Bearbeitet, Ablaufdatum jetzt {z.GueltigBis:dd.MM.yyyy}" : "Bearbeitet", await EigeneIdAsync(ct), DateTime.UtcNow);
        if (hostGeaendert)
        {
            z.LiveFehlerSeit = null;
            await dienst.PruefeLiveAsync(z, null, ct);
        }
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

    /// <summary>Manuelle Bestätigung „neues Zertifikat ist online" — für Zertifikate ohne Live-Prüfung oder als Abkürzung.</summary>
    [Authorize(Policy = "Betrieb")]
    [HttpPost("{id:int}/erneuert")]
    public async Task<ActionResult<ZertifikatEintrag>> Erneuert(int id, ZertifikatErneuern daten, CancellationToken ct)
    {
        var z = await db.Zertifikate.Include(x => x.Server).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (z is null)
            return NotFound();
        if (daten.NeuesGueltigBis <= DateOnly.FromDateTime(DateTime.UtcNow))
            return BadRequest("Das neue Ablaufdatum muss in der Zukunft liegen.");
        var eigeneId = await EigeneIdAsync(ct);
        var alt = z.GueltigBis;
        z.Erneuern(daten.NeuesGueltigBis, eigeneId, DateTime.UtcNow);
        z.Protokoll(ZertifikatEreignisArt.ErneuertBestaetigt, $"Bestätigt: gültig bis {z.GueltigBis:dd.MM.yyyy} (vorher {alt:dd.MM.yyyy})", eigeneId, DateTime.UtcNow);
        var e = await dienst.EinstellungenAsync(ct);
        await dienst.PruefeLiveAsync(z, e, ct);
        await db.SaveChangesAsync(ct);
        await db.Entry(z).Reference(x => x.ErneuertVon).LoadAsync(ct);
        return Ok(NachEintrag(z, e.EskalationTage));
    }

    /// <summary>
    /// Live-Prüfung eines Zertifikats jetzt ausführen (erkennt auch eine Erneuerung).
    /// Schreibt, übernimmt ggf. die Erneuerung und verschickt Mails — daher wie alle
    /// anderen schreibenden Endpunkte nur für Support/Admin (SICHERHEIT.md N1).
    /// </summary>
    [Authorize(Policy = "Betrieb")]
    [HttpPost("{id:int}/pruefen")]
    public async Task<ActionResult<ZertifikatEintrag>> Pruefen(int id, CancellationToken ct)
    {
        var z = await db.Zertifikate.Include(x => x.Server).Include(x => x.ErneuertVon).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (z is null)
            return NotFound();
        var e = await dienst.EinstellungenAsync(ct);
        await dienst.PruefeLiveAsync(z, e, ct);
        await db.SaveChangesAsync(ct);
        return Ok(NachEintrag(z, e.EskalationTage));
    }

    /// <summary>Erinnerungslauf sofort ausführen (sonst stündlich im Hintergrund).</summary>
    [Authorize(Policy = "Betrieb")]
    [HttpPost("erinnerungen")]
    public async Task<ActionResult<ErinnerungsLauf>> Erinnerungen(CancellationToken ct) =>
        Ok(await dienst.ErinnerungenAsync(ct));

    /// <summary>Zertifikate aus der Serverliste anlegen (sonst täglich im Hintergrund, wenn eingeschaltet).</summary>
    [Authorize(Policy = "Betrieb")]
    [HttpPost("server-anlegen")]
    public async Task<ActionResult<ServerZertifikateErgebnis>> ServerAnlegen(CancellationToken ct) =>
        Ok(await dienst.ServerZertifikateAnlegenAsync(await EigeneIdAsync(ct), ct));

    /// <summary>Wochenbericht jetzt senden (Test des Lebenszeichens).</summary>
    [Authorize(Policy = "Betrieb")]
    [HttpPost("wochenbericht")]
    public async Task<IActionResult> Wochenbericht(CancellationToken ct) =>
        await dienst.WochenberichtAsync(erzwingen: true, ct)
            ? NoContent()
            : BadRequest("Wochenbericht konnte nicht gesendet werden — E-Mail-Versand und Standardempfänger prüfen.");

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

    private Task<int?> EigeneIdAsync(CancellationToken ct) =>
        db.Mitarbeiter.Where(Mitarbeiter.KontoFilter(benutzer.EntraObjectId, benutzer.WindowsKonto))
            .Select(m => (int?)m.Id).FirstOrDefaultAsync(ct);

    private async Task<string?> PruefeAsync(ZertifikatSpeichern d, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(d.Bezeichnung))
            return "Die Bezeichnung darf nicht leer sein.";
        // Schutzregeln der Live-Prüfung (SICHERHEIT.md N1): kein Netz-Abtasten, keine Statusmails nach außen
        var e = await dienst.EinstellungenAsync(ct);
        if (ZertifikatPruefregeln.HostnameFehler(d.Hostname) is { } hostFehler)
            return hostFehler;
        if (!string.IsNullOrWhiteSpace(d.Hostname) && ZertifikatPruefregeln.PortFehler(d.Port, e.ErlaubtePorts) is { } portFehler)
            return portFehler;
        if (d.Port is < 1 or > 65535)
            return "Der Port muss zwischen 1 und 65535 liegen.";
        if (d.LaufzeitMonate is < 1 or > 120)
            return "Die Laufzeit muss zwischen 1 und 120 Monaten liegen.";
        if (d.VorlaufTage is < 1 or > 365)
            return "Der Vorlauf muss zwischen 1 und 365 Tagen liegen.";
        if (d.ServerId is { } sid && !await db.Server.AnyAsync(s => s.Id == sid, ct))
            return "Der gewählte Server existiert nicht.";
        var ungueltig = ZertifikatPruefregeln.UnzulaessigeEmpfaenger(d.Empfaenger, e.EmpfaengerDomaenen);
        if (ungueltig.Count > 0)
            return e.EmpfaengerDomaenen.Count == 0
                ? $"Ungültige Empfängeradresse: {string.Join(", ", ungueltig)}"
                : $"Ungültige oder nicht erlaubte Empfängeradresse: {string.Join(", ", ungueltig)} (erlaubt: @{string.Join(", @", e.EmpfaengerDomaenen)})";
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
        z.AutoErkennung = d.AutoErkennung;
    }

    private static ZertifikatEintrag NachEintrag(Zertifikat z, int eskalationTage)
    {
        var heute = DateOnly.FromDateTime(DateTime.UtcNow);
        return new ZertifikatEintrag(
            z.Id, z.Bezeichnung, z.Hostname, z.Port, z.ServerId, z.Server?.Alias, z.Anbieter, z.LaufzeitMonate,
            z.GueltigBis, z.VorlaufTage, z.Empfaenger, z.Notiz, z.Aktiv, z.AutoErkennung, z.AutomatischAngelegt,
            ZertifikatErinnerung.Status(z.GueltigBis, z.VorlaufTage, heute),
            ZertifikatErinnerung.Stufe(z.GueltigBis, z.VorlaufTage, eskalationTage, heute),
            ZertifikatErinnerung.TageBisAblauf(z.GueltigBis, heute),
            z.ErneuertAm, z.ErneuertVon?.Anzeigename,
            z.LetzteErinnerungAm, z.ErinnerungenGesendet, z.LetzterMailFehler,
            z.LiveGeprueftAm, z.LiveGueltigBis, z.LiveAussteller, z.LiveFehler, z.LiveFehlerSeit, z.LiveProtokoll, z.LiveWarnungen,
            ZertifikatErinnerung.VorschlagNeuesGueltigBis(z.GueltigBis, z.LaufzeitMonate, z.LiveGueltigBis, heute));
    }
}
