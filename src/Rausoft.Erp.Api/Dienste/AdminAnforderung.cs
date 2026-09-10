using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Autorisierung "Admin": Die Rolle wird serverseitig aus der Mitarbeiter-
/// Tabelle geprüft (über Entra-Objekt-ID bzw. Windows-Konto), nie aus Claims
/// des Frontends — Berechtigungen werden nie nur im Frontend durchgesetzt.
/// </summary>
public class AdminAnforderung : IAuthorizationRequirement;

public class AdminAnforderungHandler(ErpDbContext db, IAktuellerBenutzer benutzer)
    : AuthorizationHandler<AdminAnforderung>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AdminAnforderung anforderung)
    {
        if (benutzer.EntraObjectId is null && benutzer.WindowsKonto is null)
            return;

        var rollen = await db.Mitarbeiter
            .Where(Mitarbeiter.KontoFilter(benutzer.EntraObjectId, benutzer.WindowsKonto))
            .Where(m => m.Aktiv)
            .Select(m => m.Rollen)
            .FirstOrDefaultAsync();

        if (rollen.HasFlag(MitarbeiterRolle.Admin))
            context.Succeed(anforderung);
    }
}
