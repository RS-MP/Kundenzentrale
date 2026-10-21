namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Stündlicher Takt für die SSL-Zertifikate. Einmal täglich: Zertifikate aus
/// der Serverliste anlegen (wenn eingeschaltet), Live-Prüfung aller
/// Zertifikate (erkennt Erneuerungen selbst), Prüfstörungen melden,
/// Wochenbericht. Stündlich: fällige Erinnerungen/Eskalationen senden.
/// Fehler brechen den Takt nie ab.
/// </summary>
public class ZertifikatHintergrund(IServiceScopeFactory scopeFactory, ILogger<ZertifikatHintergrund> log)
    : BackgroundService
{
    private DateOnly? _letzterTageslauf;

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
                if (_letzterTageslauf != heute)
                {
                    var einstellungen = await dienst.EinstellungenAsync(stoppingToken);
                    if (einstellungen.ServerAutomatisch)
                    {
                        var neu = await dienst.ServerZertifikateAnlegenAsync(null, stoppingToken);
                        if (neu.Angelegt > 0)
                            log.LogInformation("Zertifikate aus der Serverliste angelegt: {Hosts}", string.Join(", ", neu.Hostnamen));
                    }
                    await dienst.PruefeAlleLiveAsync(stoppingToken);
                    var gestoert = await dienst.PruefstoerungenMeldenAsync(stoppingToken);
                    if (gestoert > 0)
                        log.LogWarning("Zertifikats-Prüfstörungen gemeldet: {Anzahl}", gestoert);
                    if (await dienst.WochenberichtAsync(erzwingen: false, stoppingToken))
                        log.LogInformation("Wochenbericht SSL-Zertifikate gesendet");
                    _letzterTageslauf = heute;
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
