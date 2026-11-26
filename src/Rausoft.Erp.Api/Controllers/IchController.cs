using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Api.Dienste;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Der angemeldete Mitarbeiter über sich selbst — für die Seite „Meine Einstellungen“. Nur
/// lesen: Name, Adresse, Bereich und Rollen pflegt ein Administrator (Benutzer &amp; Rollen).
/// </summary>
[ApiController]
[Route("api/ich")]
public class IchController(ErpDbContext db, AngemeldeterMitarbeiter angemeldet) : ControllerBase
{
    private static readonly (MitarbeiterRolle Rolle, string Name)[] Rollennamen =
    [
        (MitarbeiterRolle.Admin, "Admin"),
        (MitarbeiterRolle.Vertrieb, "Vertrieb"),
        (MitarbeiterRolle.Support, "Support"),
        (MitarbeiterRolle.Buchhaltung, "Buchhaltung"),
        (MitarbeiterRolle.Compliance, "Compliance"),
        (MitarbeiterRolle.VertraulicheVertraege, "Vertrauliche Verträge"),
        (MitarbeiterRolle.WikiRedaktion, "Wiki-Redaktion"),
        (MitarbeiterRolle.Margenrechner, "Margenrechner"),
    ];

    [HttpGet]
    public async Task<ActionResult<IchInfo>> Ich(CancellationToken ct)
    {
        if (await angemeldet.IdAsync(ct) is not { } id)
            return Forbid();
        var ich = await db.Mitarbeiter.AsNoTracking().Where(m => m.Id == id)
            .Select(m => new { m.Anzeigename, m.Email, m.WindowsKonto, m.Rollen, Bereich = m.Bereich != null ? m.Bereich.Name : null })
            .FirstOrDefaultAsync(ct);
        if (ich is null)
            return Forbid();
        // Automatisch angelegte Konten tragen eine Platzhalter-Adresse — das ist keine, an die man schreiben kann
        var adresse = MailRegeln.Adressen(ich.Email).Gueltig.FirstOrDefault();
        var echt = adresse is not null && !adresse.EndsWith("@ad.lokal", StringComparison.Ordinal) && !adresse.EndsWith("@localhost", StringComparison.Ordinal);
        return Ok(new IchInfo(ich.Anzeigename, echt ? adresse : null, ich.WindowsKonto, ich.Bereich,
            Rollennamen.Where(r => ich.Rollen.HasFlag(r.Rolle)).Select(r => r.Name).ToList()));
    }
}
