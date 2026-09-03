using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Customers;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Stammliste der vergebbaren Kunden-Merkmale. Jeder Mitarbeiter kann neue
/// Merkmale ergänzen (stehen dann allen zur Auswahl); Änderungen laufen
/// durchs Audit-Log.
/// </summary>
[ApiController]
[Route("api/merkmale")]
public class MerkmaleController(ErpDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<string>>> Liste(CancellationToken ct) =>
        Ok(await db.Merkmale
            .AsNoTracking()
            .Where(m => m.Aktiv)
            .OrderBy(m => m.Name)
            .Select(m => m.Name)
            .ToListAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Anlegen(MerkmalAnlegen anlage, CancellationToken ct)
    {
        var name = anlage.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return BadRequest("Der Name darf nicht leer sein.");
        if (name.Length > 100)
            return BadRequest("Der Name darf höchstens 100 Zeichen lang sein.");
        if (name.Contains('·'))
            return BadRequest("Der Name darf kein „·“ enthalten.");

        var vorhanden = await db.Merkmale.FirstOrDefaultAsync(m => m.Name == name, ct);
        if (vorhanden is not null)
        {
            if (!vorhanden.Aktiv)
            {
                vorhanden.Aktiv = true;
                await db.SaveChangesAsync(ct);
            }
            return NoContent();
        }

        db.Merkmale.Add(new Merkmal { Name = name });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
