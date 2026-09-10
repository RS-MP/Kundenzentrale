using Microsoft.Identity.Web;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Liest die Identität des angemeldeten Kontos aus dem aktuellen Request:
/// Entra-Objekt-ID (oid-Claim) bei Token-Anmeldung, Windows-Konto
/// ("DOMÄNE\benutzer") bei Windows-Authentifizierung gegen die lokale AD.
/// </summary>
public class AktuellerBenutzer(IHttpContextAccessor httpContextAccessor) : IAktuellerBenutzer
{
    public Guid? EntraObjectId
    {
        get
        {
            var oid = httpContextAccessor.HttpContext?.User.GetObjectId();
            return Guid.TryParse(oid, out var guid) ? guid : null;
        }
    }

    public string? WindowsKonto
    {
        get
        {
            var identitaet = httpContextAccessor.HttpContext?.User.Identity;
            return identitaet is { IsAuthenticated: true, Name: { } name } && name.Contains('\\')
                ? name : null;
        }
    }
}
