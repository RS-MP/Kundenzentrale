using Microsoft.Identity.Web;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Liest die Entra-Objekt-ID (oid-Claim) des angemeldeten Kontos aus dem
/// validierten Token des aktuellen Requests.
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
}
