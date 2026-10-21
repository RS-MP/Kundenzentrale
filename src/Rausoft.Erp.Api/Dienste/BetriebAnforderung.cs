using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Autorisierung „Betrieb": SSL-Zertifikate pflegen und bestätigen — Rolle
/// Support oder Admin. Wie die anderen Anforderungen serverseitig aus der
/// Mitarbeiter-Tabelle geprüft.
/// </summary>
public class BetriebAnforderung : IAuthorizationRequirement;

public class BetriebAnforderungHandler(ErpDbContext db, IAktuellerBenutzer benutzer)
    : AuthorizationHandler<BetriebAnforderung>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, BetriebAnforderung anforderung)
    {
        if (benutzer.EntraObjectId is null && benutzer.WindowsKonto is null)
            return;

        var rollen = await db.Mitarbeiter
            .Where(Mitarbeiter.KontoFilter(benutzer.EntraObjectId, benutzer.WindowsKonto))
            .Where(m => m.Aktiv)
            .Select(m => m.Rollen)
            .FirstOrDefaultAsync();

        if (rollen.HasFlag(MitarbeiterRolle.Support) || rollen.HasFlag(MitarbeiterRolle.Admin))
            context.Succeed(anforderung);
    }
}
