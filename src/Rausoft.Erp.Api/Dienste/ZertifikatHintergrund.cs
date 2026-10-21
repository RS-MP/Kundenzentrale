namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Stündlicher Takt für die SSL-Zertifikate: einmal täglich Live-Prüfung aller
/// Zertifikate mit Hostname, danach fällige Erinnerungen senden (der Dienst
/// selbst prüft über Zertifikate.ErinnerungIntervallTage, ob eine Erinnerung
/// erneut fällig ist). Fehler brechen den Takt nie ab.
/// </summary>
public class ZertifikatHintergrund(IServiceScopeFactory scopeFactory, ILogger<ZertifikatHintergrund> log)
    : BackgroundService
{
    private DateOnly? _letzteLivePruefung;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dienst = scope.ServiceProvider.GetRequiredService<ZertifikatDienst>();

                var heute = DateOnly.FromDateTime(DateTime.UtcNow);
                if (_letzteLivePruefung != heute)
                {
                    await dienst.PruefeAlleLiveAsync(stoppingToken);
                    _letzteLivePruefung = heute;
                }

                var lauf = await dienst.ErinnerungenAsync(stoppingToken);
                if (lauf.Gesendet > 0 || lauf.Fehler.Count > 0)
                    log.LogInformation("Zertifikats-Erinnerungen: {Gesendet} gesendet, {Fehler} Fehler",
                        lauf.Gesendet, lauf.Fehler.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Zertifikats-Takt fehlgeschlagen");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
