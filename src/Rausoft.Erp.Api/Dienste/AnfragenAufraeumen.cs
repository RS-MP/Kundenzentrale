using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Customers;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Täglicher Löschlauf für die Klick-Kennungen der Website-Anfragen (gclid, gbraid,
/// wbraid): nach <see cref="AnfrageHerkunft.KlickKennungTage"/> Tagen nimmt Google sie
/// weder zum Nachschlagen noch zur Rückmeldung an — dann haben sie keinen Zweck mehr
/// und werden entfernt (Entscheidung MP 17.11.2026). Die lesbare Herkunft bleibt.
/// Läuft auch im Testsystem: Löschen von Abgelaufenem ist dort genauso richtig.
/// </summary>
public class AnfragenAufraeumen(IServiceScopeFactory scopeFactory, ILogger<AnfragenAufraeumen> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
                var geloescht = await KlickKennungenLoeschenAsync(db, DateTime.UtcNow, stoppingToken);
                if (geloescht > 0)
                    log.LogInformation("Klick-Kennungen abgelaufener Website-Anfragen gelöscht: {Anzahl}", geloescht);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Löschlauf der Klick-Kennungen fehlgeschlagen");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    /// <summary>
    /// Entfernt die Klick-Kennungen aller Anfragen, die älter als die Frist sind. Direkt in
    /// der Datenbank (ohne Änderungsverfolgung), damit die Werte nirgends mitgeschrieben werden.
    /// </summary>
    public static Task<int> KlickKennungenLoeschenAsync(ErpDbContext db, DateTime jetztUtc, CancellationToken ct)
    {
        var grenze = jetztUtc.AddDays(-AnfrageHerkunft.KlickKennungTage);
        return db.Anfragen
            .Where(a => a.EingegangenAm < grenze && (a.Gclid != null || a.Gbraid != null || a.Wbraid != null))
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Gclid, (string?)null)
                .SetProperty(a => a.Gbraid, (string?)null)
                .SetProperty(a => a.Wbraid, (string?)null)
                .SetProperty(a => a.KlickKennungenGeloeschtAm, jetztUtc), ct);
    }
}
