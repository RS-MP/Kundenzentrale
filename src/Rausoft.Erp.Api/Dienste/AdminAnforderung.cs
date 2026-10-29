using Microsoft.AspNetCore.Authorization;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Autorisierung "Admin": Die Rolle wird serverseitig aus der Mitarbeiter-
/// Tabelle geprüft (über Entra-Objekt-ID bzw. Windows-Konto), nie aus Claims
/// des Frontends — Berechtigungen werden nie nur im Frontend durchgesetzt.
/// Deaktivierte Konten haben keine Rollen (<see cref="AngemeldeterMitarbeiter.RollenAsync"/>).
/// </summary>
public class AdminAnforderung : IAuthorizationRequirement;

public class AdminAnforderungHandler(AngemeldeterMitarbeiter angemeldet)
    : AuthorizationHandler<AdminAnforderung>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AdminAnforderung anforderung)
    {
        if (await angemeldet.HatRolleAsync(MitarbeiterRolle.Admin))
            context.Succeed(anforderung);
    }
}

/// <summary>
/// Autorisierung "Compliance": Vorlagen verwalten/freigeben — Compliance-Rolle oder Admin
/// (Entscheidung E1 in SICHERHEIT.md: Admin ist hier bewusst eingeschlossen).
/// </summary>
public class ComplianceAnforderung : IAuthorizationRequirement;

public class ComplianceAnforderungHandler(AngemeldeterMitarbeiter angemeldet)
    : AuthorizationHandler<ComplianceAnforderung>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ComplianceAnforderung anforderung)
    {
        if (await angemeldet.HatRolleAsync(MitarbeiterRolle.Compliance | MitarbeiterRolle.Admin))
            context.Succeed(anforderung);
    }
}
