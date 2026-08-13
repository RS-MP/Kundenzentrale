using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Customers;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Erster fachlicher Endpunkt als Durchstich API → EF Core → SQL Server.
/// Wird in den nächsten Schritten um Anlage/Pflege und DTOs erweitert.
/// </summary>
[ApiController]
[Route("api/kunden")]
public class KundenController(ErpDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Kunde>>> Liste(CancellationToken ct)
    {
        var kunden = await db.Kunden
            .AsNoTracking()
            .OrderBy(k => k.Kundennummer)
            .ToListAsync(ct);
        return Ok(kunden);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Kunde>> Einzeln(int id, CancellationToken ct)
    {
        var kunde = await db.Kunden
            .AsNoTracking()
            .Include(k => k.Ansprechpartner.Where(a => a.Aktiv))
            .Include(k => k.Adressen.Where(a => a.Aktiv))
            .FirstOrDefaultAsync(k => k.Id == id, ct);
        return kunde is null ? NotFound() : Ok(kunde);
    }
}
