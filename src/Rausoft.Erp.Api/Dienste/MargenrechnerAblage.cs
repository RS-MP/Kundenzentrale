using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Margen;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Autorisierung „Margenrechner": nur die einzeln vergebene Rolle
/// (<see cref="MargenrechnerZugang"/>). Admin, Vertrieb und Compliance genügen
/// bewusst nicht — Einkaufspreise und Margen sehen nur ausgewählte Personen.
/// Wie die anderen Anforderungen serverseitig aus der Mitarbeiter-Tabelle
/// geprüft; deaktivierte Konten haben keine Rollen.
/// </summary>
public class MargenrechnerAnforderung : IAuthorizationRequirement
{
    public const string Policy = "Margenrechner";
}

public class MargenrechnerAnforderungHandler(AngemeldeterMitarbeiter angemeldet)
    : AuthorizationHandler<MargenrechnerAnforderung>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, MargenrechnerAnforderung anforderung)
    {
        if (MargenrechnerZugang.Darf(await angemeldet.RollenAsync()))
            context.Succeed(anforderung);
    }
}

/// <summary>
/// Ablage der Margenrechner-Daten (Verkaufspreise, Einkaufsstaffel): als JSON
/// mit ASP.NET Core Data Protection verschlüsselt, jeder Stand eine neue Zeile.
/// Eigener Zweck — ein anderer Protector kann die Daten nicht lesen. Wie bei
/// den Geheimnissen gilt: ein anderes System (Testsystem, neuer Server) hat
/// einen anderen Schlüsselbund und liest den Stand nicht; dort werden die
/// Daten neu eingetragen.
/// </summary>
public class MargenrechnerAblage(ErpDbContext db, IDataProtectionProvider schutz)
{
    public const string Zweck = "Kundenzentrale.Margenrechner.Daten";

    public sealed record Stand(MargenDaten? Daten, string? Hinweis, DateTime? GeaendertAm, string? GeaendertVon);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private IDataProtector Protector => schutz.CreateProtector(Zweck);

    /// <summary>Jüngster Stand; ohne Daten erklärt <see cref="Stand.Hinweis"/>, warum nicht gerechnet werden kann.</summary>
    public async Task<Stand> LadenAsync(CancellationToken ct)
    {
        var zeile = await db.MargenrechnerStaende.AsNoTracking()
            .OrderByDescending(s => s.Id)
            .Select(s => new
            {
                s.DatenVerschluesselt,
                s.GeaendertAm,
                Von = s.GeaendertVon != null ? s.GeaendertVon.Anzeigename : null,
            })
            .FirstOrDefaultAsync(ct);
        if (zeile is null)
            return new Stand(null, "Für den Margenrechner sind noch keine Daten eingetragen.", null, null);

        try
        {
            var daten = JsonSerializer.Deserialize<MargenDaten>(Protector.Unprotect(zeile.DatenVerschluesselt), Json);
            if (daten is null || daten.Pruefen().Count > 0)
                return new Stand(null, "Der gespeicherte Stand ist unvollständig — bitte die Daten neu eintragen.",
                    zeile.GeaendertAm, zeile.Von);
            return new Stand(daten, null, zeile.GeaendertAm, zeile.Von);
        }
        catch (CryptographicException)
        {
            return new Stand(null,
                "Der gespeicherte Stand wurde auf einem anderen System verschlüsselt und ist hier nicht lesbar — bitte die Daten neu eintragen.",
                zeile.GeaendertAm, zeile.Von);
        }
        catch (JsonException)
        {
            return new Stand(null, "Der gespeicherte Stand ist nicht lesbar — bitte die Daten neu eintragen.",
                zeile.GeaendertAm, zeile.Von);
        }
    }

    /// <summary>Legt einen neuen Stand an. Die Daten müssen vorher mit <see cref="MargenDaten.Pruefen"/> geprüft sein.</summary>
    public async Task SpeichernAsync(MargenDaten daten, int? mitarbeiterId, CancellationToken ct)
    {
        db.MargenrechnerStaende.Add(new MargenrechnerStand
        {
            DatenVerschluesselt = Protector.Protect(JsonSerializer.Serialize(daten, Json)),
            GeaendertAm = DateTime.UtcNow,
            GeaendertVonId = mitarbeiterId,
        });
        await db.SaveChangesAsync(ct);
    }
}
