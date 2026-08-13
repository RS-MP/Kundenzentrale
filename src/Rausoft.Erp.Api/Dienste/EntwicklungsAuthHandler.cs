using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Authentifizierung für lokale Tests ohne Entra ID: Jeder Request gilt als
/// angemeldeter Entwicklungs-Benutzer. Wird ausschließlich aktiviert, wenn
/// Environment = Development UND "Entwicklung:AuthDeaktiviert" = true —
/// in Produktion validiert die API immer Entra-ID-Tokens.
/// </summary>
public class EntwicklungsAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Schema = "Entwicklung";

    /// <summary>Feste Pseudo-Objekt-ID des Entwicklungs-Benutzers (auch fürs Seeding).</summary>
    public static readonly Guid EntraObjectId = new("11111111-1111-1111-1111-111111111111");

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim("oid", EntraObjectId.ToString()),
            new Claim(ClaimTypes.Name, "Entwicklung (lokal)"),
            new Claim("preferred_username", "entwicklung@localhost"),
        };
        var identity = new ClaimsIdentity(claims, Schema);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Schema);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
