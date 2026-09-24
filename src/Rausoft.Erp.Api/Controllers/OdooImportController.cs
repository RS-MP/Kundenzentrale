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
public class OdooImportController(OdooImportDienst dienst, ErpDbContext db) : ControllerBase
{
    [HttpGet]
    public Task<OdooImportStatus> Status(CancellationToken ct) => StatusErmitteln(ct);

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
