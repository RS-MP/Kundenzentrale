using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Subscriptions;

namespace Rausoft.Erp.OdooImport;

/// <summary>
/// Importiert Abonnements aus Odoo (sale.order mit is_subscription) inklusive
/// Positionen (Lizenzen/Module) — ohne Preise (Phase 4). Reichert außerdem die
/// Bestands-Vertragsakten an: Laufzeit/Verlängerung aus dem Abo-Intervall,
/// Beginn aus dem Laufzeit-Startdatum — nur dort, wo noch keine Laufzeit
/// gepflegt ist (manuelle Pflege wird nie überschrieben).
/// Idempotent über die Odoo-Id des Abos.
/// </summary>
public class AboImporter(OdooClient odoo, bool dryRun, string? verbindung = null)
{
    private const string NotizMarker = "Bestandsimport aus der Vertragsablage";

    public async Task AusfuehrenAsync()
    {
        Console.WriteLine(dryRun
            ? "=== ABO-IMPORT (DRY-RUN — es wird nichts gespeichert) ==="
            : "=== ABO-IMPORT ===");

        var verbindungsZeichenfolge = verbindung
            ?? Environment.GetEnvironmentVariable("KUNDENZENTRALE_DB")
            ?? @"Server=(localdb)\MSSQLLocalDB;Database=Kundenzentrale;Trusted_Connection=True;Encrypt=False";
        var options = new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(verbindungsZeichenfolge).Options;
        await using var db = new ErpDbContext(options);

        // Abos aus Odoo laden
        var abosRoh = new List<JsonElement>();
        for (var offset = 0; ; offset += 500)
        {
            var batch = await odoo.AusfuehrenAsync("sale.order", "search_read",
                [new object[] { new object[] { "is_subscription", "=", true } }],
                new Dictionary<string, object>
                {
                    ["fields"] = new[]
                    {
                        "id", "name", "partner_id", "plan_id", "subscription_state",
                        "start_date", "end_date", "next_invoice_date", "x_studio_startdatum_laufzeit",
                    },
                    ["limit"] = 500,
                    ["offset"] = offset,
                    ["order"] = "id",
                });
            var anzahl = 0;
            foreach (var element in batch.EnumerateArray())
            {
                abosRoh.Add(element);
                anzahl++;
            }
            if (anzahl < 500)
                break;
        }

        // Positionen aller Abos in einem Rutsch (Abschnitts-/Nullzeilen aussortieren)
        var positionenNachAuftrag = new Dictionary<int, List<(string Produkt, decimal Menge)>>();
        for (var offset = 0; ; offset += 1000)
        {
            var batch = await odoo.AusfuehrenAsync("sale.order.line", "search_read",
                [new object[]
                {
                    new object[] { "order_id.is_subscription", "=", true },
                    new object[] { "display_type", "=", false },
                    new object[] { "product_uom_qty", ">", 0 },
                }],
                new Dictionary<string, object>
                {
                    ["fields"] = new[] { "order_id", "name", "product_uom_qty" },
                    ["limit"] = 1000,
                    ["offset"] = offset,
                    ["order"] = "id",
                });
            var anzahl = 0;
            foreach (var zeile in batch.EnumerateArray())
            {
                anzahl++;
                var auftragId = zeile.GetProperty("order_id")[0].GetInt32();
                var name = zeile.GetProperty("name").GetString() ?? "";
                name = name.Split('\n')[0].Trim(); // Odoo hängt Beschreibungszeilen an
                var menge = zeile.GetProperty("product_uom_qty").GetDecimal();
                (positionenNachAuftrag.TryGetValue(auftragId, out var liste)
                    ? liste
                    : positionenNachAuftrag[auftragId] = []).Add((name, menge));
            }
            if (anzahl < 1000)
                break;
        }

        Console.WriteLine($"Geladen aus Odoo: {abosRoh.Count} Abos, " +
            $"{positionenNachAuftrag.Values.Sum(l => l.Count)} Positionen");

        var kundenNachOdooId = (await db.Kunden.Where(k => k.OdooId != null).ToListAsync())
            .ToDictionary(k => k.OdooId!.Value);
        var abosNachOdooId = (await db.Abos.Include(a => a.Positionen).ToListAsync())
            .Where(a => a.OdooId is not null)
            .ToDictionary(a => a.OdooId!.Value);
        var bestandsVertraege = (await db.Vertraege
                .Where(v => v.Notiz != null && v.Notiz.StartsWith(NotizMarker)
                    // nur Kundenverträge — Partnerverträge haben keinen Kunden
                    && v.KundeId != null)
                .ToListAsync())
            .GroupBy(v => v.KundeId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        int neu = 0, aktualisiert = 0, ohneKunde = 0, vertraegeAngereichert = 0;

        foreach (var roh in abosRoh)
        {
            var partnerId = roh.GetProperty("partner_id")[0].GetInt32();
            if (!kundenNachOdooId.TryGetValue(partnerId, out var kunde))
            {
                ohneKunde++;
                continue;
            }

            var odooId = roh.GetProperty("id").GetInt32();
            var istNeu = !abosNachOdooId.TryGetValue(odooId, out var abo);
            if (istNeu)
            {
                abo = new Abo { KundeId = kunde.Id, Nummer = "", OdooId = odooId };
                db.Abos.Add(abo);
                abosNachOdooId[odooId] = abo;
                neu++;
            }
            else
            {
                aktualisiert++;
            }

            abo!.Nummer = Text(roh, "name") ?? $"S{odooId}";
            abo.Status = Status(Text(roh, "subscription_state"));
            abo.IntervallMonate = IntervallMonate(roh);
            abo.Beginn = Datum(roh, "start_date");
            abo.LaufzeitBeginn = Datum(roh, "x_studio_startdatum_laufzeit");
            abo.NaechsteVerlaengerung = Datum(roh, "next_invoice_date");
            abo.Ende = Datum(roh, "end_date");

            // Positionen abgleichen — nur schreiben, wenn sich wirklich etwas ändert
            var soll = positionenNachAuftrag.GetValueOrDefault(odooId, []);
            var ist = abo.Positionen.Select(p => (p.Produkt, p.Menge)).ToList();
            if (!soll.OrderBy(p => p.Produkt).SequenceEqual(ist.OrderBy(p => p.Produkt)))
            {
                abo.Positionen.Clear();
                foreach (var (produkt, menge) in soll)
                    abo.Positionen.Add(new AboPosition { Produkt = Kuerzen(produkt, 300), Menge = menge });
            }

            // Neues laufendes Abo holt archivierte Kunden automatisch aus dem Archiv
            if (abo.Status == AboStatus.Laufend && kunde.ArchiviertAm != null)
            {
                kunde.ArchiviertAm = null;
                db.Aktivitaeten.Add(new Rausoft.Erp.Domain.Shared.Aktivitaet
                {
                    KundeId = kunde.Id,
                    Typ = Rausoft.Erp.Domain.Shared.AktivitaetsTyp.System,
                    Zeitpunkt = DateTime.UtcNow,
                    Richtung = Rausoft.Erp.Domain.Shared.AktivitaetsRichtung.Intern,
                    Betreff = $"Aus dem Archiv geholt — laufendes Abo {abo.Nummer} aus Odoo",
                });
            }

            // Bestands-Vertragsakte anreichern: nur laufende Abos, nie manuelle Pflege überschreiben
            if (abo.Status == AboStatus.Laufend
                && bestandsVertraege.TryGetValue(kunde.Id, out var vertraege))
            {
                foreach (var vertrag in vertraege.Where(v => v.MindestlaufzeitMonate == null))
                {
                    vertrag.Beginn = abo.LaufzeitBeginn ?? abo.Beginn ?? vertrag.Beginn;
                    vertrag.MindestlaufzeitMonate = abo.IntervallMonate;
                    vertrag.VerlaengerungMonate = abo.IntervallMonate;
                    vertrag.Notiz = $"{NotizMarker} — Laufzeit aus Odoo-Abo {abo.Nummer} übernommen; " +
                                    "Kündigungsfrist bitte prüfen.";
                    vertraegeAngereichert++;
                }
            }
        }

        Console.WriteLine($"Abos: {neu} neu, {aktualisiert} vorhanden · {ohneKunde} ohne zuordenbaren Kunden");
        Console.WriteLine($"Bestands-Verträge mit Laufzeit angereichert: {vertraegeAngereichert}");

        if (dryRun)
        {
            Console.WriteLine("DRY-RUN: keine Änderungen gespeichert.");
            return;
        }

        var geschrieben = await db.SaveChangesAsync();
        Console.WriteLine($"Gespeichert ({geschrieben} Datensätze inkl. Audit-Log).");
    }

    private static AboStatus Status(string? odooZustand) => odooZustand switch
    {
        "3_progress" or "2_renewal" => AboStatus.Laufend,
        "5_renewed" => AboStatus.Abgeloest,
        "6_churn" => AboStatus.Beendet,
        _ => AboStatus.Entwurf,
    };

    private static int IntervallMonate(JsonElement roh)
    {
        var plan = roh.TryGetProperty("plan_id", out var p) && p.ValueKind == JsonValueKind.Array
            ? p[1].GetString()
            : null;
        return plan switch
        {
            "Monthly" or "Monatlich" => 1,
            "Vierteljährlich" => 3,
            "Halbjährlich" => 6,
            "3 Jahre" => 36,
            _ => 12, // Yearly und unbekannte Pläne
        };
    }

    private static string? Text(JsonElement element, string feld) =>
        element.TryGetProperty(feld, out var wert) && wert.ValueKind == JsonValueKind.String
            ? wert.GetString()
            : null;

    private static DateOnly? Datum(JsonElement element, string feld) =>
        Text(element, feld) is { } text && DateOnly.TryParse(text, out var datum) ? datum : null;

    private static string Kuerzen(string text, int max) => text.Length <= max ? text : text[..max];
}
