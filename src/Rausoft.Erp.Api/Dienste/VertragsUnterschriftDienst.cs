using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Contracts;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Domain.Vorgaenge;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Vorgangsschritte, die sich mit der Unterschrift erledigen (MP 27.11.2026): Sobald die
/// unterschriebene Fassung des zugeordneten Vertrags vorliegt (<see cref="VertragsUnterschrift"/>),
/// hakt die Kundenzentrale den Schritt ab — und gibt damit frei, was auf ihn wartet, etwa die
/// Freischaltung der World-Check-Daten. Von Hand lässt sich ein solcher Schritt nicht erledigen.
/// Geprüft wird nach jeder Änderung am Vertrag, beim Öffnen des Vorgangs und im stündlichen Takt.
/// </summary>
public class VertragsUnterschriftDienst(ErpDbContext db, ILogger<VertragsUnterschriftDienst> log)
{
    private static DateOnly Heute => DateOnly.FromDateTime(DateTime.Now);

    /// <param name="vertragId">Nur Schritte zu diesem Vertrag; leer = alle offenen.</param>
    /// <returns>Zahl der abgehakten Schritte.</returns>
    public async Task<int> AbhakenAsync(int? vertragId, CancellationToken ct)
    {
        var offen = await db.Aufgaben.AsNoTracking()
            .Where(a => a.VorgangId != null && a.AutoAbhaken == AutoAbhaken.VertragUnterschrieben && a.VertragId != null
                        && a.Status != AufgabenStatus.Erledigt && a.Status != AufgabenStatus.Entfallen
                        && (vertragId == null || a.VertragId == vertragId))
            .Select(a => new { a.Id, VorgangId = a.VorgangId!.Value, VertragId = a.VertragId!.Value })
            .ToListAsync(ct);
        if (offen.Count == 0)
            return 0;

        var vertragIds = offen.Select(o => o.VertragId).Distinct().ToList();
        var vertraege = await db.Vertraege.AsNoTracking()
            .Include(v => v.Bestandteile).ThenInclude(b => b.Dokumente)
            .Where(v => vertragIds.Contains(v.Id))
            .AsSplitQuery()
            .ToListAsync(ct);
        var unterschrieben = vertraege
            .Select(v => (Vertrag: v, Seit: VertragsUnterschrift.LiegtVorSeit(v)))
            .Where(x => x.Seit is not null)
            .ToDictionary(x => x.Vertrag.Id, x => (x.Vertrag.Vertragsnummer, Seit: x.Seit!.Value));

        var anzahl = 0;
        foreach (var gruppe in offen.Where(o => unterschrieben.ContainsKey(o.VertragId)).GroupBy(o => o.VorgangId))
        {
            var v = await db.Vorgaenge.Include(x => x.Schritte).ThenInclude(s => s.ZustaendigBereich)
                .FirstAsync(x => x.Id == gruppe.Key, ct);
            VorgangsHilfen.Verknuepfen(v.Schritte);
            foreach (var schritt in v.Schritte.Where(s => gruppe.Any(g => g.Id == s.Id) && !VorgangsLogik.FertigOderWeg(s)))
            {
                var (nummer, seit) = unterschrieben[schritt.VertragId!.Value];
                var freigegeben = VorgangsLogik.Erledigen(schritt, v.Schritte, Heute, DateTime.UtcNow, null, trotzSperre: true);
                VorgangsProtokoll.Verlauf(db, v, null, $"Automatisch erledigt: {schritt.Titel}",
                    $"Der unterschriebene Vertrag {nummer} liegt vor (unterzeichnet am {seit:dd.MM.yyyy}).", AktivitaetsStufe.Wesentlich);
                VorgangsProtokoll.Freigabe(db, v, null, freigegeben);
                anzahl++;
            }
            VorgangsProtokoll.Abschluss(db, v, null);
            await db.SaveChangesAsync(ct);
        }
        if (anzahl > 0)
            log.LogInformation("Vertragsunterschrift: {Anzahl} Schritt(e) automatisch erledigt", anzahl);
        return anzahl;
    }
}
