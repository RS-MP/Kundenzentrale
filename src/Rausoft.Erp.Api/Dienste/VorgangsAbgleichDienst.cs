using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Domain.Vorgaenge;
using Rausoft.Erp.Dtos;
using Rausoft.Erp.OdooImport;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Abgleich der laufenden Vorgänge mit dem Odoo-Bestand (Übergangszeit):
/// 1. Strang nachziehen — wurde der Vorgang angelegt, bevor der Odoo-Verlauf zum Auftrag
///    importiert war, bekommt er nachträglich dessen Strang (Schritte und Verlauf ziehen mit).
/// 2. „Rechnung in Odoo stellen" abhaken, sobald Odoo eine gebuchte Rechnung zum Auftrag hat
///    (exakt über invoice_origin, nur lesend).
/// Läuft stündlich (<see cref="VorgangsAbgleichHintergrund"/>), nach jedem Chatter-Import und
/// auf Knopfdruck in der Vorgangsübersicht.
/// </summary>
public class VorgangsAbgleichDienst(
    ErpDbContext db, IServiceScopeFactory scopeFactory, IConfiguration konfiguration, ILogger<VorgangsAbgleichDienst> log)
{
    private static DateOnly Heute => DateOnly.FromDateTime(DateTime.Now);

    public async Task<VorgangsAbgleichErgebnis> AbgleichenAsync(CancellationToken ct)
    {
        var straenge = await StraengeNachziehenAsync(ct);
        var (abgehakt, hinweis) = await RechnungenAbhakenAsync(ct);
        return new VorgangsAbgleichErgebnis(straenge, abgehakt, hinweis);
    }

    private async Task<int> StraengeNachziehenAsync(CancellationToken ct)
    {
        var kandidaten = await db.Vorgaenge
            .Where(v => v.AbgeschlossenAm == null && v.OdooAuftrag != null && v.StrangSchluessel.StartsWith("vorgang:"))
            .Include(v => v.Schritte)
            .ToListAsync(ct);
        var anzahl = 0;
        foreach (var v in kandidaten)
        {
            if (await VorgangsHilfen.OdooStrangAsync(db, v.KundeId, v.OdooAuftrag, ct) is not { } strang)
                continue;
            await VorgangsProtokoll.StrangSetzenAsync(db, v, strang.Schluessel, strang.Name, ct);
            VorgangsProtokoll.Verlauf(db, v, null, $"Mit dem Odoo-Verlauf verknüpft: {v.StrangName}");
            anzahl++;
        }
        if (anzahl > 0)
            await db.SaveChangesAsync(ct);
        return anzahl;
    }

    private async Task<(int Abgehakt, string? Hinweis)> RechnungenAbhakenAsync(CancellationToken ct)
    {
        var offen = await db.Aufgaben.AsNoTracking()
            .Where(a => a.VorgangId != null && a.AutoAbhaken == AutoAbhaken.OdooRechnung
                        && a.Status != AufgabenStatus.Erledigt && a.Status != AufgabenStatus.Entfallen
                        && a.Vorgang!.OdooAuftrag != null)
            .Select(a => new { a.Id, a.VorgangId, Auftrag = a.Vorgang!.OdooAuftrag! })
            .ToListAsync(ct);
        if (offen.Count == 0)
            return (0, null);

        List<OdooRechnung> rechnungen;
        try
        {
            var (odoo, _) = await OdooImportDienst.VerbindenAsync(scopeFactory, konfiguration);
            rechnungen = await AuftragsRechnungen.LadenAsync(odoo, offen.Select(o => o.Auftrag).Distinct().ToList());
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Odoo-Zugangsdaten unvollständig"))
        {
            return (0, "Odoo ist nicht eingerichtet — Rechnungen wurden nicht abgeglichen.");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            log.LogWarning(ex, "Vorgangs-Abgleich: Odoo nicht erreichbar");
            return (0, $"Odoo nicht erreichbar: {VorgangsHilfen.Kuerzen(ex.Message, 200)}");
        }

        var treffer = AuftragsRechnungen.Zuordnen(rechnungen, offen.Select(o => o.Auftrag));
        var anzahl = 0;
        foreach (var gruppe in offen.Where(o => treffer.ContainsKey(o.Auftrag)).GroupBy(o => o.VorgangId!.Value))
        {
            var v = await db.Vorgaenge.Include(x => x.Schritte).ThenInclude(s => s.ZustaendigBereich)
                .FirstAsync(x => x.Id == gruppe.Key, ct);
            VorgangsHilfen.Verknuepfen(v.Schritte);
            var rechnung = treffer[v.OdooAuftrag!];
            foreach (var schritt in v.Schritte.Where(s => gruppe.Any(g => g.Id == s.Id) && !VorgangsLogik.FertigOderWeg(s)))
            {
                var freigegeben = VorgangsLogik.Erledigen(schritt, v.Schritte, Heute, DateTime.UtcNow, null, trotzSperre: true);
                VorgangsProtokoll.Verlauf(db, v, null, $"Automatisch erledigt: {schritt.Titel}",
                    $"Rechnung {rechnung.Name}{(rechnung.Datum is { } d ? $" vom {d:dd.MM.yyyy}" : "")} zum Auftrag {v.OdooAuftrag} ist in Odoo gebucht.",
                    AktivitaetsStufe.Wesentlich);
                VorgangsProtokoll.Freigabe(db, v, null, freigegeben);
                anzahl++;
            }
            VorgangsProtokoll.Abschluss(db, v, null);
            await db.SaveChangesAsync(ct);
        }
        if (anzahl > 0)
            log.LogInformation("Vorgangs-Abgleich: {Anzahl} Rechnungsschritt(e) automatisch erledigt", anzahl);
        return (anzahl, null);
    }
}

/// <summary>Stündlicher Abgleich der Vorgänge mit Odoo; Fehler brechen den Takt nie ab.</summary>
public class VorgangsAbgleichHintergrund(IServiceScopeFactory scopeFactory, ILogger<VorgangsAbgleichHintergrund> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var ergebnis = await scope.ServiceProvider.GetRequiredService<VorgangsAbgleichDienst>().AbgleichenAsync(stoppingToken);
                if (ergebnis.StraengeVerknuepft + ergebnis.SchritteAbgehakt > 0)
                    log.LogInformation("Vorgangs-Abgleich: {Straenge} Stränge verknüpft, {Schritte} Schritte abgehakt",
                        ergebnis.StraengeVerknuepft, ergebnis.SchritteAbgehakt);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Vorgangs-Abgleich fehlgeschlagen");
            }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
