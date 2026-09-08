using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Api.Dienste;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Server-Verwaltung + Status-Meldung ans Service Portal — nur für Admins.
/// URLs und Prüf-Mandanten sind interne Daten und verlassen die API nur
/// Richtung Frontend (interne Nutzer), nie Richtung Portal.
/// </summary>
[ApiController]
[Route("api/server")]
[Authorize(Policy = "Admin")]
public class ServerController(ErpDbContext db, ServerStatusDienst statusDienst, GeheimnisDienst geheimnisse)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ServerEintrag>>> Liste(CancellationToken ct) =>
        Ok((await db.Server.AsNoTracking().OrderBy(s => s.Alias).ToListAsync(ct))
            .Select(NachEintrag).ToList());

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
    [HttpPost("status-pruefen")]
    public async Task<ActionResult<List<ServerEintrag>>> StatusPruefen(CancellationToken ct)
    {
        await statusDienst.PruefeAlleAsync(ct);
        return Ok((await db.Server.AsNoTracking().OrderBy(s => s.Alias).ToListAsync(ct))
            .Select(NachEintrag).ToList());
    }

    // --- Geheimnisse (write-only) -------------------------------------------

    [HttpGet("geheimnisse")]
    public async Task<ActionResult<List<GeheimnisStatus>>> Geheimnisse(CancellationToken ct) =>
        Ok((await geheimnisse.StatusAsync(ct))
            .Select(g => new GeheimnisStatus(g.Schluessel, g.GesetztAm)).ToList());

    [HttpPut("geheimnisse/{schluessel}")]
    public async Task<IActionResult> GeheimnisSetzen(string schluessel, Dtos.GeheimnisSetzen setzen, CancellationToken ct)
    {
        if (schluessel != GeheimnisSchluessel.ServicePortalPushToken)
            return BadRequest("Unbekannter Geheimnis-Schlüssel.");
        if (string.IsNullOrWhiteSpace(setzen.Wert))
            return BadRequest("Der Wert darf nicht leer sein.");
        await geheimnisse.SetzenAsync(schluessel, setzen.Wert.Trim(), ct);
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

    private static ServerEintrag NachEintrag(Domain.Servers.Server s) => new(
        s.Id, s.Alias, s.Url, s.PruefMandant, s.Aktiv,
        s.IstErreichbar, s.ConnectorAktiv, s.SanktionslistenStand, s.Fehlertext, s.GeprueftAm);
}
