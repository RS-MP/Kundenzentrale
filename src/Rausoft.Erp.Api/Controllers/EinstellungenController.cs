using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Systemeinstellungen (z. B. Ablage-Verzeichnisse) — nur für Admins.
/// Änderungen landen automatisch im Audit-Log.
/// </summary>
[ApiController]
[Route("api/einstellungen")]
[Authorize(Policy = "Admin")]
public class EinstellungenController(ErpDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<EinstellungEintrag>>> Liste(CancellationToken ct)
    {
        var einstellungen = await db.Einstellungen
            .AsNoTracking()
            .OrderBy(e => e.Schluessel)
            .Select(e => new EinstellungEintrag(e.Schluessel, e.Wert, e.Beschreibung))
            .ToListAsync(ct);
        return Ok(einstellungen);
    }

    [HttpPut("{schluessel}")]
    public async Task<IActionResult> Aendern(string schluessel, EinstellungAenderung aenderung, CancellationToken ct)
    {
        var einstellung = await db.Einstellungen.FirstOrDefaultAsync(e => e.Schluessel == schluessel, ct);
        if (einstellung is null)
            return NotFound();

        einstellung.Wert = aenderung.Wert.Trim();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
