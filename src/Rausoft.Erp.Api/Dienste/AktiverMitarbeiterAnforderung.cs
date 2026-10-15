using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Grundberechtigung für JEDEN Endpunkt (Fallback-/Default-Policy): Das
/// angemeldete Konto muss einem <b>aktiven</b> Mitarbeiter-Datensatz gehören.
/// Damit sperrt „Deaktivieren" in der Benutzerverwaltung wirklich aus — vorher
/// verlor ein deaktivierter Benutzer nur die Rollenfunktionen, konnte aber
/// weiterhin Kunden, Verträge und Preisliste lesen (SICHERHEIT.md, V3).
/// Rollen-Policies (Admin, Compliance) prüfen zusätzlich ihre Rolle.
/// </summary>
public class AktiverMitarbeiterAnforderung : IAuthorizationRequirement;

public class AktiverMitarbeiterAnforderungHandler(ErpDbContext db, IAktuellerBenutzer benutzer)
    : AuthorizationHandler<AktiverMitarbeiterAnforderung>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AktiverMitarbeiterAnforderung anforderung)
    {
        if (benutzer.EntraObjectId is null && benutzer.WindowsKonto is null)
            return;

        var aktiv = await db.Mitarbeiter.AsNoTracking()
            .Where(Mitarbeiter.KontoFilter(benutzer.EntraObjectId, benutzer.WindowsKonto))
            .Select(m => m.Aktiv)
            .FirstOrDefaultAsync();

        if (aktiv)
            context.Succeed(anforderung);
    }
}
