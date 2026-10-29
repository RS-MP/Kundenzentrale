using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Domain.Wiki;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Wer darf welches Wiki-Thema pflegen? Admin und Compliance alles; die Rolle
/// Wiki-Redaktion nur die Themen, die dem Benutzer einzeln freigegeben sind
/// (FEEDBACK Nr. 22). Serverseitig geprüft, das Frontend blendet nur aus.
/// </summary>
public class WikiBerechtigung(ErpDbContext db, AngemeldeterMitarbeiter angemeldet)
{
    /// <summary>Themen, die der angemeldete Benutzer bearbeiten darf (leer = keines).</summary>
    public async Task<IReadOnlySet<string>> BearbeitbareThemenAsync(CancellationToken ct)
    {
        if (await angemeldet.HolenAsync(ct) is not { Aktiv: true } m)
            return new HashSet<string>();

        if (m.Rollen.HasFlag(MitarbeiterRolle.Admin) || m.Rollen.HasFlag(MitarbeiterRolle.Compliance))
            return WikiThema.Alle.Select(t => t.Schluessel).ToHashSet();

        if (!m.Rollen.HasFlag(MitarbeiterRolle.WikiRedaktion))
            return new HashSet<string>();

        return Themen(m.WikiThemen).Where(WikiThema.IstBekannt).ToHashSet();
    }

    public async Task<bool> DarfBearbeitenAsync(string thema, CancellationToken ct) =>
        (await BearbeitbareThemenAsync(ct)).Contains(thema);

    /// <summary>Mitarbeiter-Id des angemeldeten Kontos (null = kein Datensatz).</summary>
    public async Task<int?> EigeneIdAsync(CancellationToken ct) =>
        await angemeldet.HolenAsync(ct) is { Aktiv: true } m ? m.Id : null;

    /// <summary>
    /// Zuständig für Meldungen und Lesestand eines Themas: Verantwortlicher und
    /// Vertretung (WikiThemaInfo) sowie alle, die das Thema pflegen dürfen.
    /// </summary>
    public async Task<bool> IstZustaendigAsync(string thema, CancellationToken ct)
    {
        if (await DarfBearbeitenAsync(thema, ct))
            return true;
        var id = await EigeneIdAsync(ct);
        return id is not null && await db.WikiThemaInfos.AsNoTracking()
            .AnyAsync(i => i.Thema == thema && (i.VerantwortlichId == id || i.VertreterId == id), ct);
    }

    /// <summary>Themen, für die der Benutzer zuständig ist (siehe <see cref="IstZustaendigAsync"/>).</summary>
    public async Task<IReadOnlySet<string>> ZustaendigeThemenAsync(CancellationToken ct)
    {
        var themen = new HashSet<string>(await BearbeitbareThemenAsync(ct));
        var id = await EigeneIdAsync(ct);
        if (id is not null)
            themen.UnionWith(await db.WikiThemaInfos.AsNoTracking()
                .Where(i => i.VerantwortlichId == id || i.VertreterId == id)
                .Select(i => i.Thema).ToListAsync(ct));
        return themen;
    }

    /// <summary>Kommagetrennte Themenliste am Mitarbeiter → Schlüssel.</summary>
    public static IEnumerable<string> Themen(string? wikiThemen) =>
        (wikiThemen ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
