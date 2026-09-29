using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Customers;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Partner-Akte (FEEDBACK Nr. 20 Stufe 2): Vertriebs-/Tipp-Partner mit ihren
/// Kunden und Partnerverträgen. Stammdaten kommen aus dem Odoo-Import und
/// sind hier nur lesbar; Verträge laufen über api/vertraege (partnerId).
/// Ein Partner kann zugleich Kunde sein (nutzt die Software selbst) — dann
/// verweist er auf den Kunden-Datensatz.
/// </summary>
[ApiController]
[Route("api/partner")]
public class PartnerController(ErpDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PartnerEintrag>>> Liste(CancellationToken ct)
    {
        var partner = await db.Partner.AsNoTracking()
            .OrderByDescending(p => p.Aktiv).ThenBy(p => p.Name)
            .Select(p => new
            {
                p.Id, p.Name, p.Ort, p.Land, p.Provisionsmodell, p.ProvisionProzent, p.Aktiv, p.KundeId,
                Kunden = db.Kunden.Count(k => k.VermitteltDurchPartnerId == p.Id || k.RechnungsempfaengerPartnerId == p.Id),
                Vertraege = db.Vertraege.Count(v => v.PartnerId == p.Id),
            })
            .ToListAsync(ct);
        return Ok(partner.Select(p => new PartnerEintrag(
            p.Id, p.Name, p.Ort, p.Land, ModellText(p.Provisionsmodell), p.ProvisionProzent, p.Aktiv,
            p.Kunden, p.Vertraege, p.KundeId is not null)).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PartnerDetail>> Einzeln(int id, CancellationToken ct)
    {
        var p = await db.Partner.AsNoTracking().Include(x => x.Kunde).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null)
            return NotFound();
        var kunden = await db.Kunden.AsNoTracking()
            .Where(k => k.VermitteltDurchPartnerId == id || k.RechnungsempfaengerPartnerId == id)
            .OrderBy(k => k.Firmenname)
            .Select(k => new PartnerKunde(k.Id, k.Kundennummer, k.Firmenname,
                k.VermitteltDurchPartnerId == id, k.RechnungsempfaengerPartnerId == id))
            .ToListAsync(ct);

        // Vorschläge für „ist auch Kunde": Firmenname enthält den Partnernamen
        // (Odoo schreibt z. B. „Finesolutions" am Partner, „FineSolutions AG" am Kunden)
        var vorschlaege = p.KundeId is not null || p.Name.Trim().Length < 3
            ? []
            : await db.Kunden.AsNoTracking()
                .Where(k => k.Firmenname.Contains(p.Name.Trim()) && !db.Partner.Any(x => x.KundeId == k.Id))
                .OrderBy(k => k.Firmenname)
                .Take(5)
                .Select(k => new PartnerKunde(k.Id, k.Kundennummer, k.Firmenname, false, false))
                .ToListAsync(ct);

        return Ok(new PartnerDetail(p.Id, p.Name, p.Strasse, p.Plz, p.Ort, p.Land, p.Email, p.Telefon,
            ModellText(p.Provisionsmodell), p.ProvisionProzent, p.Aktiv, kunden,
            p.Kunde is null ? null : new PartnerKunde(p.Kunde.Id, p.Kunde.Kundennummer, p.Kunde.Firmenname, false, false),
            vorschlaege));
    }

    /// <summary>„Partner ist auch Kunde" setzen oder lösen.</summary>
    [HttpPut("{id:int}/kunde")]
    public async Task<IActionResult> KundeVerknuepfen(int id, PartnerKundeVerknuepfen verknuepfung, CancellationToken ct)
    {
        var partner = await db.Partner.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (partner is null)
            return NotFound();
        if (verknuepfung.KundeId is { } kundeId)
        {
            if (!await db.Kunden.AnyAsync(k => k.Id == kundeId, ct))
                return BadRequest("Der angegebene Kunde existiert nicht.");
            if (await db.Partner.Where(p => p.KundeId == kundeId && p.Id != id).Select(p => p.Name).FirstOrDefaultAsync(ct)
                is { } anderer)
                return BadRequest($"Dieser Kunde ist bereits mit dem Partner „{anderer}“ verknüpft.");
        }
        partner.KundeId = verknuepfung.KundeId;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static string ModellText(Provisionsmodell modell) => modell switch
    {
        Provisionsmodell.TippProvision => "Tipp-Provision",
        Provisionsmodell.Vertriebspartner => "Vertriebspartner",
        _ => modell.ToString(),
    };
}
