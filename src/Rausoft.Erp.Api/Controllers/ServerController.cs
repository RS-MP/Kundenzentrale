using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Api.Dienste;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Server-Verwaltung + Status-Meldung ans Service Portal. Lesen dürfen alle
/// Mitarbeiter (Server-Übersicht), Verwalten/Melden nur Admins. URLs und
/// Mandanten-Kennungen sind interne Daten und verlassen die API nur Richtung
/// Frontend (interne Nutzer), nie Richtung Portal.
/// </summary>
[ApiController]
[Route("api/server")]
public class ServerController(ErpDbContext db, ServerStatusDienst statusDienst, GeheimnisDienst geheimnisse)
    : ControllerBase
{
    /// <summary>Übersicht für alle Mitarbeiter (Verwaltung nur für Admins).</summary>
    [HttpGet]
    public async Task<ActionResult<List<ServerEintrag>>> Liste(CancellationToken ct)
    {
        var kundenProServer = await db.Kunden
            .Where(k => k.ServerId != null)
            .GroupBy(k => k.ServerId!.Value)
            .Select(g => new { ServerId = g.Key, Anzahl = g.Count() })
            .ToDictionaryAsync(g => g.ServerId, g => g.Anzahl, ct);
        return Ok((await db.Server.AsNoTracking().OrderBy(s => s.Alias).ToListAsync(ct))
            .Select(s => NachEintrag(s, kundenProServer.GetValueOrDefault(s.Id)))
            .ToList());
    }

    /// <summary>URL des Service Portals — für den Absprung-Link in der Server-Übersicht.</summary>
    [HttpGet("portal-url")]
    public async Task<ActionResult<string>> PortalUrl(CancellationToken ct) =>
        Ok((await db.Einstellungen.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Schluessel == EinstellungsSchluessel.ServicePortalUrl, ct))?.Wert ?? "");

    /// <summary>Zugeordnete Kunden eines Servers (interne Sicht).</summary>
    [HttpGet("{id:int}/kunden")]
    public async Task<ActionResult<List<ServerKunde>>> Kunden(int id, CancellationToken ct)
    {
        var roh = await db.Kunden.AsNoTracking()
            .Where(k => k.ServerId == id)
            .OrderBy(k => k.Firmenname)
            .Select(k => new
            {
                k.Id, k.Kundennummer, k.Firmenname, k.MandantenKennung,
                HatAktives = db.Abos.Any(a => a.KundeId == k.Id && a.Status == Domain.Subscriptions.AboStatus.Laufend)
                    || db.Vertraege.Any(v => v.KundeId == k.Id
                        && v.Status == Domain.Contracts.VertragsStatus.Aktiv && v.MindestlaufzeitMonate != null),
            })
            .ToListAsync(ct);
        return Ok(roh.Select(k => new ServerKunde(
            k.Id, k.Kundennummer, k.Firmenname, k.MandantenKennung,
            k.HatAktives ? "Kunde" : "kein aktiver Vertrag")).ToList());
    }

    [Authorize(Policy = "Admin")]
    [HttpPost]
    public async Task<ActionResult<ServerEintrag>> Anlegen(ServerAendern anlage, CancellationToken ct)
    {
        if (Pruefe(anlage) is { } fehler)
            return BadRequest(fehler);
        if (await db.Server.AnyAsync(s => s.Alias == anlage.Alias.Trim(), ct))
            return BadRequest("Ein Server mit diesem Alias existiert bereits.");

        var server = new Domain.Servers.Server { Alias = "", Url = "" };
        Uebernehmen(server, anlage);
        db.Server.Add(server);
        await db.SaveChangesAsync(ct);
        return Ok(NachEintrag(server));
    }

    [Authorize(Policy = "Admin")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Aendern(int id, ServerAendern aenderung, CancellationToken ct)
    {
        if (Pruefe(aenderung) is { } fehler)
            return BadRequest(fehler);
        var server = await db.Server.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (server is null)
            return NotFound();
        if (await db.Server.AnyAsync(s => s.Id != id && s.Alias == aenderung.Alias.Trim(), ct))
            return BadRequest("Ein Server mit diesem Alias existiert bereits.");

        Uebernehmen(server, aenderung);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [Authorize(Policy = "Admin")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Loeschen(int id, CancellationToken ct)
    {
        var server = await db.Server.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (server is null)
            return NotFound();
        db.Server.Remove(server);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Healthchecks abfragen und Ergebnis als Batch ans Portal melden.</summary>
    [Authorize(Policy = "Admin")]
    [HttpPost("status-melden")]
    public async Task<ActionResult<MeldeBericht>> StatusMelden(CancellationToken ct)
    {
        try
        {
            await statusDienst.PruefeAlleAsync(ct);
            var (verarbeitet, unbekannt) = await statusDienst.PushAsync(ct);
            var server = (await db.Server.AsNoTracking().OrderBy(s => s.Alias).ToListAsync(ct))
                .Select(NachEintrag).ToList();
            return Ok(new MeldeBericht(DateTime.Now, verarbeitet, unbekannt, server));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (HttpRequestException)
        {
            return BadRequest("Das Service Portal ist nicht erreichbar.");
        }
    }

    /// <summary>Nur Healthchecks abfragen (ohne Portal-Push) — z. B. zum Testen der Server-Einträge.</summary>
    [Authorize(Policy = "Admin")]
    [HttpPost("status-pruefen")]
    public async Task<ActionResult<List<ServerEintrag>>> StatusPruefen(CancellationToken ct)
    {
        await statusDienst.PruefeAlleAsync(ct);
        return Ok((await db.Server.AsNoTracking().OrderBy(s => s.Alias).ToListAsync(ct))
            .Select(NachEintrag).ToList());
    }

    // --- Geheimnisse (write-only) -------------------------------------------

    [Authorize(Policy = "Admin")]
    [HttpGet("geheimnisse")]
    public async Task<ActionResult<List<GeheimnisStatus>>> Geheimnisse(CancellationToken ct) =>
        Ok((await geheimnisse.StatusAsync(ct))
            .Select(g => new GeheimnisStatus(g.Schluessel, g.GesetztAm)).ToList());

    [Authorize(Policy = "Admin")]
    [HttpPut("geheimnisse/{schluessel}")]
    public async Task<IActionResult> GeheimnisSetzen(string schluessel, Dtos.GeheimnisSetzen setzen, CancellationToken ct)
    {
        if (schluessel is not (GeheimnisSchluessel.ServicePortalPushToken
            or GeheimnisSchluessel.OdooApiKey or GeheimnisSchluessel.SignaturPfxPasswort))
            return BadRequest("Unbekannter Geheimnis-Schlüssel.");
        if (string.IsNullOrWhiteSpace(setzen.Wert))
            return BadRequest("Der Wert darf nicht leer sein.");
        var wert = setzen.Wert.Trim();
        // „Bearer " setzt die Kundenzentrale selbst — mitkopiertes Präfix entfernen,
        // sonst ginge „Bearer Bearer …" raus und das Portal antwortet mit 404
        if (schluessel == GeheimnisSchluessel.ServicePortalPushToken
            && wert.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            wert = wert["Bearer ".Length..].Trim();
        await geheimnisse.SetzenAsync(schluessel, wert, ct);
        return NoContent();
    }

    // --- Hilfen ----------------------------------------------------------------

    private static string? Pruefe(ServerAendern daten) =>
        string.IsNullOrWhiteSpace(daten.Alias) ? "Der Alias darf nicht leer sein."
        : !Uri.TryCreate(daten.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            ? "Die URL muss eine gültige http(s)-Adresse sein."
        : null;

    private static void Uebernehmen(Domain.Servers.Server server, ServerAendern daten)
    {
        server.Alias = daten.Alias.Trim();
        server.Url = daten.Url.Trim();
        server.PruefMandant = daten.PruefMandant?.Trim() ?? "";
        server.Aktiv = daten.Aktiv;
    }

    private static ServerEintrag NachEintrag(Domain.Servers.Server s, int kundenAnzahl = 0) => new(
        s.Id, s.Alias, s.Url, s.PruefMandant, s.Aktiv,
        s.IstErreichbar, s.ConnectorAktiv, s.SanktionslistenStand, s.Fehlertext, s.GeprueftAm,
        s.ReleaseStand, kundenAnzahl);
}
