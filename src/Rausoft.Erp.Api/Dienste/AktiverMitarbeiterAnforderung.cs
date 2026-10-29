using Microsoft.AspNetCore.Authorization;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Grundberechtigung für JEDEN Endpunkt (Fallback-/Default-Policy): Das
/// angemeldete Konto muss einem <b>aktiven</b> Mitarbeiter-Datensatz gehören.
/// Damit sperrt „Deaktivieren" in der Benutzerverwaltung wirklich aus — vorher
/// verlor ein deaktivierter Benutzer nur die Rollenfunktionen, konnte aber
/// weiterhin Kunden, Verträge und Preisliste lesen (SICHERHEIT.md, V3).
/// Rollen-Policies (Admin, Compliance, Betrieb) prüfen zusätzlich ihre Rolle.
/// Der Datensatz kommt aus <see cref="AngemeldeterMitarbeiter"/> (einmal je Request).
/// </summary>
public class AktiverMitarbeiterAnforderung : IAuthorizationRequirement;

public class AktiverMitarbeiterAnforderungHandler(AngemeldeterMitarbeiter angemeldet)
    : AuthorizationHandler<AktiverMitarbeiterAnforderung>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AktiverMitarbeiterAnforderung anforderung)
    {
        if (await angemeldet.AktivAsync())
            context.Succeed(anforderung);
    }
}
