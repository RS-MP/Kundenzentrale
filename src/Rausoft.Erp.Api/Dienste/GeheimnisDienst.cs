using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Verschlüsselte Ablage von Geheimnissen (Sicherheitsregel: Tokens & Co.
/// nie im Klartext persistieren). Verschlüsselung über ASP.NET Core Data
/// Protection — die Schlüssel liegen DPAPI-geschützt auf der Maschine;
/// nach einem Umzug auf einen anderen Rechner müssen Geheimnisse neu
/// eingetragen werden (bewusst so).
/// </summary>
public class GeheimnisDienst(ErpDbContext db, IDataProtectionProvider schutz)
{
    private IDataProtector Protector => schutz.CreateProtector("Kundenzentrale.GeheimeEinstellungen");

    public async Task SetzenAsync(string schluessel, string wert, CancellationToken ct = default)
    {
        var eintrag = await db.GeheimeEinstellungen.FirstOrDefaultAsync(g => g.Schluessel == schluessel, ct);
        if (eintrag is null)
        {
            eintrag = new GeheimeEinstellung { Schluessel = schluessel, WertVerschluesselt = "" };
            db.GeheimeEinstellungen.Add(eintrag);
        }
        eintrag.WertVerschluesselt = Protector.Protect(wert);
        eintrag.GesetztAm = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Entschlüsselter Wert oder null (nicht gesetzt / mit fremdem Schlüssel verschlüsselt).</summary>
    public async Task<string?> HolenAsync(string schluessel, CancellationToken ct = default)
    {
        var eintrag = await db.GeheimeEinstellungen.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Schluessel == schluessel, ct);
        if (eintrag is null)
            return null;
        try
        {
            return Protector.Unprotect(eintrag.WertVerschluesselt);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Auf anderer Maschine gesetzt — gilt als nicht vorhanden
            return null;
        }
    }

    public Task<List<(string Schluessel, DateTime GesetztAm)>> StatusAsync(CancellationToken ct = default) =>
        db.GeheimeEinstellungen.AsNoTracking()
            .OrderBy(g => g.Schluessel)
            .Select(g => new ValueTuple<string, DateTime>(g.Schluessel, g.GesetztAm))
            .ToListAsync(ct);
}
