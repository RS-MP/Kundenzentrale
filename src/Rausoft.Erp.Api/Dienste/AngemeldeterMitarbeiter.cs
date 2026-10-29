using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>Was die Anwendung je Request über den angemeldeten Mitarbeiter wissen muss.</summary>
public sealed record MitarbeiterKontext(int Id, MitarbeiterRolle Rollen, bool Aktiv, string? WikiThemen);

/// <summary>
/// Der Mitarbeiter-Datensatz zum angemeldeten Konto, <b>einmal</b> je Request
/// geladen (SICHERHEIT.md Q3): Fallback-Policy, Rollen-Policies, Controller und
/// Wiki-Berechtigung fragten denselben Datensatz bisher drei- bis fünfmal ab.
/// Scoped registriert; die Windows-Auth-Middleware legt unbekannte Konten vor der
/// Autorisierung an, daher ist „nicht gefunden" hier ein verlässliches Ergebnis.
/// </summary>
public class AngemeldeterMitarbeiter(ErpDbContext db, IAktuellerBenutzer benutzer)
{
    private Task<MitarbeiterKontext?>? _laden;

    public Task<MitarbeiterKontext?> HolenAsync(CancellationToken ct = default) => _laden ??= LadenAsync(ct);

    /// <summary>Mitarbeiter-Id des Kontos (auch wenn deaktiviert — die Fallback-Policy sperrt vorher).</summary>
    public async Task<int?> IdAsync(CancellationToken ct = default) => (await HolenAsync(ct))?.Id;

    /// <summary>Rollen des Kontos; <see cref="MitarbeiterRolle.Keine"/>, wenn unbekannt oder deaktiviert.</summary>
    public async Task<MitarbeiterRolle> RollenAsync(CancellationToken ct = default) =>
        await HolenAsync(ct) is { Aktiv: true } m ? m.Rollen : MitarbeiterRolle.Keine;

    public async Task<bool> AktivAsync(CancellationToken ct = default) => await HolenAsync(ct) is { Aktiv: true };

    public async Task<bool> HatRolleAsync(MitarbeiterRolle rolle, CancellationToken ct = default) =>
        (await RollenAsync(ct) & rolle) != 0;

    private Task<MitarbeiterKontext?> LadenAsync(CancellationToken ct) =>
        benutzer.EntraObjectId is null && benutzer.WindowsKonto is null
            ? Task.FromResult<MitarbeiterKontext?>(null)
            : db.Mitarbeiter.AsNoTracking()
                .Where(Mitarbeiter.KontoFilter(benutzer.EntraObjectId, benutzer.WindowsKonto))
                .Select(m => new MitarbeiterKontext(m.Id, m.Rollen, m.Aktiv, m.WikiThemen))
                .FirstOrDefaultAsync(ct);
}
