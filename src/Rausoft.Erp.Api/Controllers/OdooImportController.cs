using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Api.Dienste;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>Manueller Odoo-Import aus der Administration (nur Admins).</summary>
[ApiController]
[Route("api/odoo-import")]
[Authorize(Policy = "Admin")]
public class OdooImportController(OdooImportDienst dienst, ChatterImportDienst chatter, ErpDbContext db) : ControllerBase
{
    [HttpGet]
    public Task<OdooImportStatus> Status(CancellationToken ct) => StatusErmitteln(ct);

    // --- Chatter → Kunden-Timeline (Testbetrieb: manuell) -----------------------

    [HttpGet("chatter")]
    public ChatterImportStatus ChatterStatus() => ChatterStatusErmitteln();

    [HttpPost("chatter/start")]
    public ActionResult<ChatterImportStatus> ChatterStarten(ChatterImportStart start)
    {
        if (start.Modus is not (ChatterImportDienst.ModusAnalyse or ChatterImportDienst.ModusDryRun
            or ChatterImportDienst.ModusImport))
            return BadRequest("Unbekannter Modus — erlaubt sind analyse, dryrun und import.");
        if (start.Modus == ChatterImportDienst.ModusAnalyse && string.IsNullOrWhiteSpace(start.Kunde))
            return BadRequest("Für die Analyse bitte einen Kunden angeben (KundenNr oder Odoo-ID); " +
                              "der Gesamtüberblick läuft über das Konsolen-Tool.");
        if (!chatter.Starten(start.Modus, start.Kunde))
            return BadRequest("Es läuft bereits ein Chatter-Lauf.");
        return Ok(ChatterStatusErmitteln());
    }

    private ChatterImportStatus ChatterStatusErmitteln() => new(
        chatter.Laeuft, chatter.Modus, chatter.Kunde, chatter.GestartetAm, chatter.BeendetAm,
        chatter.Fehler, chatter.Bericht);

    [HttpPost("start")]
    public async Task<ActionResult<OdooImportStatus>> Starten(CancellationToken ct)
    {
        if (!dienst.Starten())
            return BadRequest("Es läuft bereits ein Import.");
        return Ok(await StatusErmitteln(ct));
    }

    private async Task<OdooImportStatus> StatusErmitteln(CancellationToken ct) => new(
        dienst.Laeuft,
        dienst.GestartetAm,
        dienst.BeendetAm,
        dienst.Fehler,
        (await db.Einstellungen.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Schluessel == EinstellungsSchluessel.OdooLetzterImport, ct))?.Wert ?? "");
}
