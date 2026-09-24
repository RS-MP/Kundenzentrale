using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.OdooImport;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Manueller Odoo-Import aus der Administration: nutzt dieselben Importer wie
/// das Konsolen-Tool (Kontakte, dann Abos), Zugangsdaten kommen aus den
/// Odoo-Einstellungen + dem verschlüsselten API-Key. Läuft als Hintergrund-
/// Task im Prozess — es kann immer nur ein Lauf gleichzeitig aktiv sein.
/// </summary>
public class OdooImportDienst(
    IServiceScopeFactory scopeFactory, IConfiguration konfiguration, ILogger<OdooImportDienst> logger)
{
    private readonly object _sperre = new();

    public bool Laeuft { get; private set; }
    public DateTime? GestartetAm { get; private set; }
    public DateTime? BeendetAm { get; private set; }
    public string? Fehler { get; private set; }

    /// <summary>Startet den Import im Hintergrund; false, wenn bereits einer läuft.</summary>
    public bool Starten()
    {
        lock (_sperre)
        {
            if (Laeuft)
                return false;
            Laeuft = true;
            GestartetAm = DateTime.UtcNow;
            BeendetAm = null;
            Fehler = null;
        }
        _ = Task.Run(AusfuehrenAsync);
        return true;
    }

    private async Task AusfuehrenAsync()
    {
        try
        {
            string url, datenbank, benutzer;
            string? apiKey;
            using (var scope = scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
                var einstellungen = await db.Einstellungen.AsNoTracking()
                    .Where(e => e.Schluessel.StartsWith("Odoo."))
                    .ToDictionaryAsync(e => e.Schluessel, e => e.Wert);
                url = einstellungen.GetValueOrDefault(EinstellungsSchluessel.OdooUrl, "").Trim();
                datenbank = einstellungen.GetValueOrDefault(EinstellungsSchluessel.OdooDatenbank, "").Trim();
                benutzer = einstellungen.GetValueOrDefault(EinstellungsSchluessel.OdooBenutzer, "").Trim();
                apiKey = await scope.ServiceProvider.GetRequiredService<GeheimnisDienst>()
                    .HolenAsync(GeheimnisSchluessel.OdooApiKey);
            }

            if (url == "" || datenbank == "" || benutzer == "" || string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException(
                    "Odoo-Zugangsdaten unvollständig — bitte URL, Datenbank, Benutzer und API-Key " +
                    "unter Administration → Odoo-Anbindung pflegen.");

            var verbindung = konfiguration.GetConnectionString("Kundenzentrale")
                ?? throw new InvalidOperationException("Connection String 'Kundenzentrale' fehlt.");

            logger.LogInformation("Odoo-Import gestartet (manuell aus der Administration).");
            var odoo = new OdooClient(url, datenbank, benutzer, apiKey);
            await new OdooImporter(odoo, dryRun: false, verbindung).AusfuehrenAsync();
            await new AboImporter(odoo, dryRun: false, verbindung).AusfuehrenAsync();
            logger.LogInformation("Odoo-Import abgeschlossen.");

            await LetztenLaufSpeichernAsync($"{DateTime.Now:dd.MM.yyyy HH:mm} — Kontakte und Abos importiert");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Odoo-Import fehlgeschlagen.");
            Fehler = ex.Message;
            await LetztenLaufSpeichernAsync($"{DateTime.Now:dd.MM.yyyy HH:mm} — FEHLGESCHLAGEN: {Kurz(ex.Message)}");
        }
        finally
        {
            lock (_sperre)
            {
                Laeuft = false;
                BeendetAm = DateTime.UtcNow;
            }
        }
    }

    private async Task LetztenLaufSpeichernAsync(string wert)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            var eintrag = await db.Einstellungen
                .FirstOrDefaultAsync(e => e.Schluessel == EinstellungsSchluessel.OdooLetzterImport);
            if (eintrag is null)
                db.Einstellungen.Add(new Einstellung
                {
                    Schluessel = EinstellungsSchluessel.OdooLetzterImport,
                    Wert = wert,
                    Beschreibung = "Letzter Odoo-Import (automatisch gepflegt).",
                });
            else
                eintrag.Wert = wert;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Letzter-Import-Zeitstempel konnte nicht gespeichert werden.");
        }
    }

    private static string Kurz(string text) => text.Length <= 200 ? text : text[..200] + "…";
}
