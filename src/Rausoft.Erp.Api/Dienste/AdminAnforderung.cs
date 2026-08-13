using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Autorisierung "Admin": Die Rolle wird serverseitig aus der Mitarbeiter-
/// Tabelle geprüft (über die Entra-Objekt-ID des Tokens), nie aus Claims
/// des Frontends — Berechtigungen werden nie nur im Frontend durchgesetzt.
/// </summary>
public class AdminAnforderung : IAuthorizationRequirement;

public class AdminAnforderungHandler(ErpDbContext db, IAktuellerBenutzer benutzer)
    : AuthorizationHandler<AdminAnforderung>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AdminAnforderung anforderung)
    {
        if (benutzer.EntraObjectId is not Guid oid)
            return;

        var rollen = await db.Mitarbeiter
            .Where(m => m.EntraObjectId == oid && m.Aktiv)
            .Select(m => m.Rollen)
            .FirstOrDefaultAsync();

        if (rollen.HasFlag(MitarbeiterRolle.Admin))
            context.Succeed(anforderung);
    }
}
