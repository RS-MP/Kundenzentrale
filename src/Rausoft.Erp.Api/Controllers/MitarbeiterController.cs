using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Benutzerverwaltung (nur Admins). Benutzer entstehen im Windows-Auth-Betrieb
/// automatisch beim ersten Zugriff (ohne Rollen) — hier werden Rollen vergeben,
/// Stammdaten gepflegt und Konten deaktiviert. Selbst-Aussperrung ist blockiert.
/// </summary>
[ApiController]
[Route("api/mitarbeiter")]
[Authorize(Policy = "Admin")]
public class MitarbeiterController(ErpDbContext db, IAktuellerBenutzer benutzer) : ControllerBase
{
    internal static readonly (MitarbeiterRolle Rolle, string Name)[] AlleRollen =
    [
        (MitarbeiterRolle.Admin, "Admin"),
        (MitarbeiterRolle.Compliance, "Compliance"),
        (MitarbeiterRolle.VertraulicheVertraege, "Vertrauliche Verträge"),
        (MitarbeiterRolle.Vertrieb, "Vertrieb"),
        (MitarbeiterRolle.Support, "Support"),
        (MitarbeiterRolle.Buchhaltung, "Buchhaltung"),
    ];

    [HttpGet]
    public async Task<ActionResult<List<MitarbeiterEintrag>>> Liste(CancellationToken ct)
    {
        var eigeneId = await EigeneId(ct);
        return Ok((await db.Mitarbeiter.AsNoTracking()
                .OrderByDescending(m => m.Aktiv).ThenBy(m => m.Anzeigename).ToListAsync(ct))
            .Select(m => NachEintrag(m, eigeneId)).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<MitarbeiterEintrag>> Anlegen(MitarbeiterAnlegen anlage, CancellationToken ct)
    {
        var konto = anlage.WindowsKonto?.Trim() ?? "";
        if (!konto.Contains('\\'))
            return BadRequest(@"Das Windows-Konto muss die Form DOMÄNE\benutzer haben (z. B. RS\mustermann).");
        if (string.IsNullOrWhiteSpace(anlage.Anzeigename))
            return BadRequest("Der Anzeigename darf nicht leer sein.");
        if (await db.Mitarbeiter.AnyAsync(m => m.WindowsKonto == konto, ct))
            return BadRequest("Für dieses Windows-Konto existiert bereits ein Benutzer.");

        var mitarbeiter = new Mitarbeiter
        {
            WindowsKonto = konto,
            Anzeigename = anlage.Anzeigename.Trim(),
            Email = konto.Replace('\\', '.').ToLowerInvariant() + "@ad.lokal",
        };
        db.Mitarbeiter.Add(mitarbeiter);
        await db.SaveChangesAsync(ct);
        return Ok(NachEintrag(mitarbeiter, await EigeneId(ct)));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Aendern(int id, MitarbeiterAendern aenderung, CancellationToken ct)
    {
        var mitarbeiter = await db.Mitarbeiter.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (mitarbeiter is null)
            return NotFound();
        if (string.IsNullOrWhiteSpace(aenderung.Anzeigename) || string.IsNullOrWhiteSpace(aenderung.Email))
            return BadRequest("Anzeigename und E-Mail dürfen nicht leer sein.");

        var email = aenderung.Email.Trim();
        if (await db.Mitarbeiter.AnyAsync(m => m.Id != id && m.Email == email, ct))
            return BadRequest("Diese E-Mail-Adresse ist bereits einem anderen Benutzer zugeordnet.");

        var rollen = NachRollen(aenderung.Rollen);
        var eigeneId = await EigeneId(ct);
        if (id == eigeneId && (!aenderung.Aktiv || !rollen.HasFlag(MitarbeiterRolle.Admin)))
            return BadRequest("Sie können sich nicht selbst deaktivieren oder die eigene Admin-Rolle entziehen.");

        mitarbeiter.Anzeigename = aenderung.Anzeigename.Trim();
        mitarbeiter.Email = email;
        mitarbeiter.Rollen = rollen;
        mitarbeiter.Aktiv = aenderung.Aktiv;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Löschen nur für nie benutzte Konten (keine Verlaufs-/Audit-Verweise) —
    /// alles andere wird deaktiviert, damit die Historie zuordenbar bleibt.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Loeschen(int id, CancellationToken ct)
    {
        var mitarbeiter = await db.Mitarbeiter.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (mitarbeiter is null)
            return NotFound();
        if (id == await EigeneId(ct))
            return BadRequest("Sie können sich nicht selbst löschen.");
        if (await db.Aktivitaeten.AnyAsync(a => a.MitarbeiterId == id, ct)
            || await db.AuditLog.AnyAsync(a => a.MitarbeiterId == id, ct))
            return BadRequest("Dieser Benutzer hat bereits Einträge in Verlauf oder Audit-Log — bitte deaktivieren statt löschen.");
        if (await db.Sanktionslisten.AnyAsync(s => s.VerantwortlichId == id || s.VertreterId == id || s.GeaendertVonId == id, ct)
            || await db.SanktionslistenStandardprozesse.AnyAsync(p => p.GeaendertVonId == id, ct))
            return BadRequest("Dieser Benutzer ist im Wiki als Verantwortlicher/Vertreter einer Sanktionsliste eingetragen — bitte dort zuerst umstellen oder deaktivieren statt löschen.");

        db.Mitarbeiter.Remove(mitarbeiter);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Task<int?> EigeneId(CancellationToken ct) =>
        benutzer.EntraObjectId is null && benutzer.WindowsKonto is null
            ? Task.FromResult<int?>(null)
            : db.Mitarbeiter.Where(Mitarbeiter.KontoFilter(benutzer.EntraObjectId, benutzer.WindowsKonto))
                .Select(m => (int?)m.Id).FirstOrDefaultAsync(ct);

    private static MitarbeiterEintrag NachEintrag(Mitarbeiter m, int? eigeneId) => new(
        m.Id, m.WindowsKonto, m.Anzeigename, m.Email,
        AlleRollen.Where(r => m.Rollen.HasFlag(r.Rolle)).Select(r => r.Name).ToList(),
        m.Aktiv, m.Id == eigeneId);

    internal static MitarbeiterRolle NachRollen(List<string> namen) =>
        AlleRollen.Where(r => namen.Contains(r.Name, StringComparer.OrdinalIgnoreCase))
            .Aggregate(MitarbeiterRolle.Keine, (summe, r) => summe | r.Rolle);
}
