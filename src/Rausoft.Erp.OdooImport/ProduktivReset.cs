using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;

namespace Rausoft.Erp.OdooImport;

/// <summary>
/// Produktiv-Reset (Entscheidung 10.11.2026, DEPLOYMENT.md): löscht die
/// Geschäftsdaten der Testphase und lässt Administration, Wiki und
/// Konfiguration stehen. Danach laufen die Importe neu (import, abos,
/// vertragsscan, chatter-import).
///
/// Bleibt: Mitarbeiter mit Rollen, Bereiche, Einstellungen, Geheimnisse,
/// Server, SSL-Zertifikate, Merkmal-Stammliste, Vorgangsvorlagen,
/// Vertragsvorlagen (ohne Versionen) und das gesamte Wiki.
/// Gelöscht: Kunden mit Ansprechpartnern und Adressen, Partner, Verträge mit
/// Bestandteilen und Dokument-Einträgen, Abos, Verlauf, Vereinbarungen,
/// Aufgaben, Vorgänge, Website-Anfragen, Vorlagen-Versionen (Historie beginnt wieder bei v1),
/// Audit-Log; die Nummernkreise beginnen wieder bei 1.
///
/// Ohne --ausfuehren wird nur gezählt. Dateien in den Ablagen fasst der Reset
/// nicht an.
/// </summary>
public class ProduktivReset(bool ausfuehren, string? bestaetigung)
{
    public async Task<int> AusfuehrenAsync()
    {
        var verbindung = ImportUmgebung.Verbindung();
        var datenbank = new SqlConnectionStringBuilder(verbindung).InitialCatalog;
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(verbindung, sql => sql.CommandTimeout(900)).Options;
        await using var db = new ErpDbContext(options);

        Console.WriteLine(ausfuehren
            ? "=== PRODUKTIV-RESET ==="
            : "=== PRODUKTIV-RESET (Vorschau — es wird nichts gelöscht) ===");

        Console.WriteLine();
        Console.WriteLine("Wird gelöscht:");
        Zeile("Kunden", await db.Kunden.CountAsync());
        Zeile("Ansprechpartner", await db.Ansprechpartner.CountAsync());
        Zeile("Adressen", await db.Adressen.CountAsync());
        Zeile("Partner", await db.Partner.CountAsync());
        Zeile("Verträge", await db.Vertraege.CountAsync());
        Zeile("Vertragsbestandteile", await db.Vertragsbestandteile.CountAsync());
        Zeile("Vertragsdokumente (nur Einträge)", await db.Vertragsdokumente.CountAsync());
        Zeile("Vorlagen-Versionen", await db.VorlagenVersionen.CountAsync());
        Zeile("Abos", await db.Abos.CountAsync());
        Zeile("Abo-Positionen", await db.AboPositionen.CountAsync());
        Zeile("Verlaufseinträge", await db.Aktivitaeten.CountAsync());
        Zeile("Anhänge im Verlauf (nur Einträge)", await db.Aktivitaetsanhaenge.CountAsync());
        Zeile("Vorbereitete und gesendete Kundenmails", await db.Ausgangsmails.CountAsync());
        Zeile("Vereinbarungen", await db.Vereinbarungen.CountAsync());
        Zeile("Aufgaben und Vorgangsschritte", await db.Aufgaben.CountAsync());
        Zeile("Vorgänge", await db.Vorgaenge.CountAsync());
        Zeile("Website-Anfragen", await db.Anfragen.CountAsync());
        Zeile("Audit-Log", await db.AuditLog.CountAsync());

        Console.WriteLine();
        Console.WriteLine("Bleibt:");
        Zeile("Mitarbeiter", await db.Mitarbeiter.CountAsync());
        Zeile("Bereiche", await db.Bereiche.CountAsync());
        Zeile("Einstellungen", await db.Einstellungen.CountAsync());
        Zeile("Geheimnisse", await db.GeheimeEinstellungen.CountAsync());
        Zeile("Server", await db.Server.CountAsync());
        Zeile("SSL-Zertifikate", await db.Zertifikate.CountAsync());
        Zeile("Merkmal-Stammliste", await db.Merkmale.CountAsync());
        Zeile("Vorgangsvorlagen", await db.Vorgangstypen.CountAsync());
        Zeile("Vertragsvorlagen (ohne Versionen)", await db.Vorlagen.CountAsync());
        Zeile("Sanktionslisten", await db.Sanktionslisten.CountAsync());
        Zeile("Preislisten-Versionen", await db.PreislistenVersionen.CountAsync());
        Zeile("FAQ-Einträge", await db.FaqEintraege.CountAsync());
        Zeile("Kommunikationsleitlinien", await db.Sprachregeln.CountAsync());
        Zeile("Wiki-Neuerungen", await db.WikiNeuerungen.CountAsync());
        Zeile("Lesebestätigungen", await db.WikiNeuerungLesungen.CountAsync());
        Zeile("Wiki-Meldungen", await db.WikiMeldungen.CountAsync());

        // Der Kundenbezug der FAQ-Einträge geht mit den Kunden verloren (interne
        // Nummern ändern sich beim Neuimport) — auflisten, damit er sich bei
        // Bedarf von Hand wieder setzen lässt.
        var faqMitKunde = await db.FaqEintraege.AsNoTracking()
            .Where(f => f.KundeId != null)
            .OrderBy(f => f.Id)
            .Select(f => new { f.Id, f.Titel, f.Kunde!.Kundennummer })
            .ToListAsync();
        if (faqMitKunde.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("FAQ-Einträge, deren Kundenbezug geleert wird:");
            foreach (var f in faqMitKunde)
                Console.WriteLine($"  FAQ {f.Id} · Kunde {f.Kundennummer} · {f.Titel}");
        }

        if (!ausfuehren)
        {
            Console.WriteLine();
            Console.WriteLine($"Vorschau beendet. Ausführen mit: produktiv-reset --ausfuehren --bestaetigen {datenbank}");
            return 0;
        }
        if (!string.Equals(bestaetigung, datenbank, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                $"Abgebrochen: --bestaetigen muss den Namen der Datenbank nennen ({datenbank}). Es wurde nichts gelöscht.");
            return 1;
        }

        // Eine Transaktion: entweder alles oder nichts. ExecuteDelete umgeht bewusst
        // Audit-Log und Dokument-Unveränderlichkeit — beides gehört zur Testphase.
        await using var transaktion = await db.Database.BeginTransactionAsync();

        await db.Aufgaben.Where(a => a.WartetAufId != null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.WartetAufId, (int?)null));
        await db.Aufgaben.ExecuteDeleteAsync();
        await db.Vereinbarungen.ExecuteDeleteAsync();
        await db.Ausgangsmails.ExecuteDeleteAsync();           // verweisen auf Verlaufseinträge und Kunden
        await db.Aktivitaeten.ExecuteDeleteAsync();            // Teilnehmer und Anhänge per Cascade
        await db.Vorgaenge.ExecuteDeleteAsync();

        await db.Vertragsdokumente.ExecuteDeleteAsync();
        await db.Vertraege.ExecuteDeleteAsync();               // Bestandteile per Cascade
        await db.VorlagenVersionen.ExecuteDeleteAsync();
        await db.Abos.ExecuteDeleteAsync();                    // Positionen per Cascade

        // Kunde ↔ Partner verweisen gegenseitig aufeinander — erst lösen, dann löschen
        await db.Kunden.Where(k => k.VermitteltDurchPartnerId != null || k.RechnungsempfaengerPartnerId != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(k => k.VermitteltDurchPartnerId, (int?)null)
                .SetProperty(k => k.RechnungsempfaengerPartnerId, (int?)null));
        await db.Anfragen.ExecuteDeleteAsync();                // verweisen auf Kunden
        // Zahlungseingänge bleiben (gebucht ist gebucht) — nur ihr Kundenbezug geht mit den Kunden
        await db.Bankumsaetze.Where(u => u.KundeId != null)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.KundeId, (int?)null));
        await db.Partner.ExecuteDeleteAsync();
        await db.Kunden.ExecuteDeleteAsync();                  // Ansprechpartner, Adressen per Cascade; FAQ-Bezug wird geleert

        await db.AuditLog.ExecuteDeleteAsync();
        await db.Nummernkreise.ExecuteUpdateAsync(s => s.SetProperty(n => n.NaechsterWert, 1));

        await transaktion.CommitAsync();

        Console.WriteLine();
        Console.WriteLine("Reset abgeschlossen. Weiter mit: import, abos, vertragsscan, chatter-import.");
        return 0;

        static void Zeile(string name, int anzahl) => Console.WriteLine($"  {name,-36} {anzahl,8}");
    }
}
