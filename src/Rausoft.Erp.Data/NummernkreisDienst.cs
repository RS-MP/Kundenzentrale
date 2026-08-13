using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Data;

/// <summary>
/// Zentrale Nummernvergabe (Leitplanke). Zieht die nächste Nummer aus dem
/// Nummernkreis und schützt sich per Rowversion-Concurrency-Check gegen
/// parallele Vergabe — bei Konflikt wird neu geladen und erneut versucht.
/// Hinweis: SaveChanges speichert auch andere anstehende Änderungen des
/// Kontexts; die Nummer daher vor den fachlichen Änderungen ziehen.
/// </summary>
public class NummernkreisDienst(ErpDbContext db)
{
    private const int MaxVersuche = 5;

    public async Task<string> NaechsteNummerAsync(string schluessel, CancellationToken ct = default)
    {
        for (var versuch = 1; versuch <= MaxVersuche; versuch++)
        {
            var kreis = await db.Nummernkreise
                    .FirstOrDefaultAsync(n => n.Schluessel == schluessel, ct)
                ?? throw new InvalidOperationException($"Nummernkreis '{schluessel}' ist nicht angelegt.");

            var nummer = kreis.Formatieren(kreis.NaechsterWert);
            kreis.NaechsterWert++;

            try
            {
                await db.SaveChangesAsync(ct);
                return nummer;
            }
            catch (DbUpdateConcurrencyException) when (versuch < MaxVersuche)
            {
                // Parallel vergeben — Stand neu laden und erneut versuchen.
                await db.Entry(kreis).ReloadAsync(ct);
            }
        }

        throw new InvalidOperationException(
            $"Nummernkreis '{schluessel}': Vergabe nach {MaxVersuche} Versuchen abgebrochen.");
    }
}
