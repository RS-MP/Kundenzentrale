using Microsoft.AspNetCore.Authorization;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Autorisierung „Betrieb": SSL-Zertifikate pflegen und bestätigen — Rolle
/// Support oder Admin. Wie die anderen Anforderungen serverseitig aus der
/// Mitarbeiter-Tabelle geprüft (einmal je Request, <see cref="AngemeldeterMitarbeiter"/>).
/// </summary>
public class BetriebAnforderung : IAuthorizationRequirement;

public class BetriebAnforderungHandler(AngemeldeterMitarbeiter angemeldet)
    : AuthorizationHandler<BetriebAnforderung>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, BetriebAnforderung anforderung)
    {
        if (await angemeldet.HatRolleAsync(MitarbeiterRolle.Support | MitarbeiterRolle.Admin))
            context.Succeed(anforderung);
    }
}
