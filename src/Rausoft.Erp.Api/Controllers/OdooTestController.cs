using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Api.Dienste;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Odoo-Testdatenbank und Ziel schreibender Odoo-Funktionen — nur für Admins.
/// Odoo stellt Testdatenbanken für 14 Tage bereit; danach wechseln Adresse, Datenbank
/// und API-Key. Hier wird der ganze Satz in einem Schritt neu eingetragen und geprüft.
/// Die Produktiv-Anbindung (Administration → Odoo-Anbindung) bleibt davon unberührt.
/// </summary>
[ApiController]
[Route("api/odoo-test")]
[Authorize(Policy = "Admin")]
public class OdooTestController(ErpDbContext db, OdooSchreibZugang zugang, GeheimnisDienst geheimnisse,
    InstanzOptionen instanz, ILogger<OdooTestController> log) : ControllerBase
{
    [HttpGet]
    public async Task<OdooTestStatus> Status(CancellationToken ct) => NachStatus(await zugang.StandAsync(ct));

    /// <summary>Neue Testdatenbank eintragen: Adresse, Datenbank, Benutzer, API-Key und Ablaufdatum in einem Schritt.</summary>
    [HttpPut]
    public async Task<ActionResult<OdooTestStatus>> Speichern(OdooTestSpeichern daten, CancellationToken ct)
    {
        var bisher = await zugang.StandAsync(ct);
        var (url, datenbank, benutzer) = (daten.Url?.Trim() ?? "", daten.Datenbank?.Trim() ?? "", daten.Benutzer?.Trim() ?? "");
        var neuerKey = string.IsNullOrWhiteSpace(daten.ApiKey) ? null : daten.ApiKey.Trim();

        if (OdooSchreibRegeln.TestZugangFehler(url, datenbank, benutzer, bisher.ProdUrl, bisher.ProdDatenbank) is { } fehler)
            return BadRequest(fehler);
        if (url.Length > 300 || datenbank.Length > 100 || benutzer.Length > 200 || neuerKey?.Length > 500)
            return BadRequest("Eine der Angaben ist zu lang.");
        // Ein alter Key gilt nur für die alte Datenbank — bei einem Wechsel muss der neue mitkommen
        var gewechselt = !string.Equals(url, bisher.TestUrl, StringComparison.OrdinalIgnoreCase)
                         || !string.Equals(datenbank, bisher.TestDatenbank, StringComparison.OrdinalIgnoreCase);
        if (neuerKey is null && (bisher.TestKeyGesetztAm is null || gewechselt))
            return BadRequest("Für eine neue Testdatenbank wird auch ihr API-Key gebraucht.");
        var gueltigBis = daten.GueltigBis ?? OdooSchreibZugang.Heute().AddDays(OdooSchreibRegeln.TestGueltigkeitTage);

        await using var transaktion = await db.Database.BeginTransactionAsync(ct);
        await SetzeAsync(EinstellungsSchluessel.OdooTestUrl, url, ct);
        await SetzeAsync(EinstellungsSchluessel.OdooTestDatenbank, datenbank, ct);
        await SetzeAsync(EinstellungsSchluessel.OdooTestBenutzer, benutzer, ct);
        await SetzeAsync(EinstellungsSchluessel.OdooTestGueltigBis, OdooSchreibRegeln.GueltigBisText(gueltigBis), ct);
        await db.SaveChangesAsync(ct);
        if (neuerKey is not null)
            await geheimnisse.SetzenAsync(GeheimnisSchluessel.OdooTestApiKey, neuerKey, ct);
        await transaktion.CommitAsync(ct);

        return NachStatus(await zugang.StandAsync(ct));
    }

    /// <summary>Wohin schreibende Odoo-Funktionen gehen: „Aus", „Test" oder „Produktiv".</summary>
    [HttpPut("schreibziel")]
    public async Task<ActionResult<OdooTestStatus>> SchreibZielSetzen(OdooSchreibZielSetzen daten, CancellationToken ct)
    {
        if (!Enum.TryParse<OdooSchreibZiel>(daten.Ziel?.Trim(), ignoreCase: true, out var ziel) || !Enum.IsDefined(ziel))
            return BadRequest("Das Ziel muss „Aus“, „Test“ oder „Produktiv“ sein.");
        if (ziel == OdooSchreibZiel.Produktiv && instanz.Testsystem)
            return BadRequest("Ein Testsystem der Kundenzentrale schreibt nie in die Odoo-Produktiv-Datenbank.");
        await SetzeAsync(EinstellungsSchluessel.OdooSchreibZiel, ziel.ToString(), ct);
        await db.SaveChangesAsync(ct);
        return NachStatus(await zugang.StandAsync(ct));
    }

    /// <summary>
    /// Meldet sich an der Testdatenbank an. Die Fehlertexte sind feste Kategorien — Odoos
    /// Antwort und technische Einzelheiten stehen nur im Server-Log.
    /// </summary>
    [HttpPost("pruefen")]
    public async Task<OdooTestPruefung> Pruefen(CancellationToken ct)
    {
        try
        {
            var odoo = await zugang.TestClientAsync(ct);
            var version = await odoo.VersionAsync().WaitAsync(TimeSpan.FromSeconds(20), ct);
            await odoo.AnmeldenAsync().WaitAsync(TimeSpan.FromSeconds(20), ct);
            return new(true, $"Anmeldung erfolgreich (Odoo {version.GetValueOrDefault("server_version")}).");
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Odoo-Anmeldung fehlgeschlagen"))
        {
            log.LogWarning("Odoo-Testdatenbank: Anmeldung abgelehnt");
            return new(false, "Anmeldung abgelehnt — Datenbank, Benutzer oder API-Key stimmen nicht.");
        }
        catch (InvalidOperationException ex) when (!ex.Message.StartsWith("Odoo-Fehler"))
        {
            return new(false, ex.Message); // eigene Klartext-Meldung (unvollständig, Produktiv-Datenbank …)
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException
                                       or InvalidOperationException or System.Text.Json.JsonException)
        {
            log.LogWarning(ex, "Odoo-Testdatenbank nicht erreichbar");
            return new(false, "Die Odoo-Testdatenbank antwortet nicht — vermutlich ist sie abgelaufen oder die Adresse stimmt nicht.");
        }
    }

    private async Task SetzeAsync(string schluessel, string wert, CancellationToken ct)
    {
        var einstellung = await db.Einstellungen.FirstOrDefaultAsync(e => e.Schluessel == schluessel, ct)
            ?? throw new InvalidOperationException($"Einstellung {schluessel} fehlt — Migration nicht angewendet?");
        einstellung.Wert = wert;
    }

    private OdooTestStatus NachStatus(OdooSchreibZugang.Stand s)
    {
        var heute = OdooSchreibZugang.Heute();
        return new OdooTestStatus(
            s.TestUrl, s.TestDatenbank, s.TestBenutzer, s.TestGueltigBis,
            OdooSchreibRegeln.TageRest(s.TestGueltigBis, heute), OdooSchreibRegeln.Abgelaufen(s.TestGueltigBis, heute),
            s.TestKeyGesetztAm, s.Ziel.ToString(), instanz.Testsystem, zugang.SchreibSperre(s));
    }
}
