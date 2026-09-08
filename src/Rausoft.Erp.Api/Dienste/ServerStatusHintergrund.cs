using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Automatische Server-Status-Meldung im einstellbaren Takt
/// (Einstellung ServicePortal.IntervallMinuten; 0 = aus). Erster
/// Hintergrunddienst der Kundenzentrale — läuft im API-Prozess und ist
/// damit erst nach dem Server-Deployment dauerhaft aktiv.
/// </summary>
public class ServerStatusHintergrund(IServiceScopeFactory scopeFactory, ILogger<ServerStatusHintergrund> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Kurz warten, bis Migrationen/Seeding beim Start durch sind
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var intervall = 0;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
                var wert = (await db.Einstellungen.AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Schluessel == EinstellungsSchluessel.ServicePortalIntervallMinuten,
                        stoppingToken))?.Wert;
                _ = int.TryParse(wert, out intervall);

                if (intervall > 0)
                {
                    var dienst = scope.ServiceProvider.GetRequiredService<ServerStatusDienst>();
                    var anzahl = await dienst.PruefeAlleAsync(stoppingToken);
                    if (anzahl > 0)
                    {
                        var (verarbeitet, unbekannt) = await dienst.PushAsync(stoppingToken);
                        log.LogInformation(
                            "Server-Status gemeldet: {Verarbeitet} verarbeitet, {Unbekannt} unbekannt",
                            verarbeitet.Count, unbekannt.Count);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Der Takt darf nie sterben — Fehler loggen und weiter
                log.LogError(ex, "Automatische Server-Status-Meldung fehlgeschlagen");
            }

            // Bei abgeschaltetem Takt alle 5 Minuten nachsehen, ob er aktiviert wurde
            var pause = intervall > 0 ? TimeSpan.FromMinutes(intervall) : TimeSpan.FromMinutes(5);
            await Task.Delay(pause, stoppingToken);
        }
    }
}
