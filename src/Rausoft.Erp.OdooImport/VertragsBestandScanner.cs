using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Contracts;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.OdooImport;

/// <summary>
/// Erfasst den Bestand der Vertragsdateien aus dem Vertragsablage-Verzeichnis
/// (Admin-Einstellung). Zuordnung über die Kundennummer am Dateianfang:
///   4228.pdf        → Basisdokument  → Bestandteil "Wartungsvertrag"
///   4228-1.pdf u. ä. → weitere Datei  → Bestandteil "Nachtrag" (Titel = Dateiname)
/// Je Kunde entsteht EIN Vertrag (Notiz-Marker "Bestandsimport…", Status Aktiv,
/// Beginn = ältestes Dateidatum — Laufzeiten bitte nachpflegen).
/// Idempotent: bereits registrierte Dateipfade werden übersprungen, der
/// Bestands-Vertrag eines Kunden wird wiederverwendet.
/// </summary>
public partial class VertragsBestandScanner(bool dryRun)
{
    private const string NotizMarker = "Bestandsimport aus der Vertragsablage";
    private static readonly Guid ImportBenutzerOid = new("22222222-2222-2222-2222-222222222222");

    [GeneratedRegex(@"^\D{0,3}?(\d{4,6})")]
    private static partial Regex KundennummerAmAnfang();

    public async Task AusfuehrenAsync()
    {
        Console.WriteLine(dryRun
            ? "=== VERTRAGSBESTAND-SCAN (DRY-RUN — es wird nichts gespeichert) ==="
            : "=== VERTRAGSBESTAND-SCAN ===");

        var verbindung = Environment.GetEnvironmentVariable("KUNDENZENTRALE_DB")
            ?? @"Server=(localdb)\MSSQLLocalDB;Database=Kundenzentrale;Trusted_Connection=True;Encrypt=False";
        var options = new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(verbindung).Options;
        await using var db = new ErpDbContext(options);

        var basis = (await db.Einstellungen.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Schluessel == EinstellungsSchluessel.VertragsablageVerzeichnis))
            ?.Wert;
        if (string.IsNullOrWhiteSpace(basis) || !Directory.Exists(basis))
        {
            Console.Error.WriteLine($"Vertragsablage-Verzeichnis nicht konfiguriert oder nicht erreichbar: '{basis}'");
            return;
        }
        Console.WriteLine($"Ablageverzeichnis: {basis}");

        var kundenNachNummer = await db.Kunden.ToDictionaryAsync(k => k.Kundennummer, StringComparer.OrdinalIgnoreCase);
        var registriertePfade = (await db.Vertragsdokumente.Select(d => d.Dateipfad).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bestandsVertraege = (await db.Vertraege
                .Include(v => v.Bestandteile).ThenInclude(b => b.Dokumente)
                .Where(v => v.Notiz != null && v.Notiz.StartsWith(NotizMarker))
                .ToListAsync())
            .ToDictionary(v => v.KundeId);

        // Dateien einsammeln und zuordnen
        var proKunde = new Dictionary<int, List<FileInfo>>();
        var nichtZuordenbar = new List<string>();
        var unbekannteNummer = new List<string>();
        var bereitsRegistriert = 0;

        foreach (var pfad in Directory.EnumerateFiles(basis, "*.pdf", SearchOption.AllDirectories))
        {
            if (registriertePfade.Contains(pfad))
            {
                bereitsRegistriert++;
                continue;
            }
            var datei = new FileInfo(pfad);
            var treffer = KundennummerAmAnfang().Match(Path.GetFileNameWithoutExtension(datei.Name));
            if (!treffer.Success)
            {
                nichtZuordenbar.Add(datei.Name);
                continue;
            }
            if (!kundenNachNummer.TryGetValue(treffer.Groups[1].Value, out var kunde))
            {
                unbekannteNummer.Add(datei.Name);
                continue;
            }
            (proKunde.TryGetValue(kunde.Id, out var liste)
                ? liste
                : proKunde[kunde.Id] = []).Add(datei);
        }

        Console.WriteLine($"Zuzuordnen: {proKunde.Values.Sum(l => l.Count)} Dateien für {proKunde.Count} Kunden · " +
            $"bereits registriert: {bereitsRegistriert} · ohne Nummer: {nichtZuordenbar.Count} · " +
            $"unbekannte Nummer: {unbekannteNummer.Count}");

        if (dryRun)
        {
            var neueVertraege = proKunde.Keys.Count(kundeId => !bestandsVertraege.ContainsKey(kundeId));
            Console.WriteLine($"DRY-RUN: würde {neueVertraege} Verträge anlegen " +
                $"({proKunde.Count - neueVertraege} vorhandene Bestands-Verträge würden ergänzt).");
            Listen("Ohne erkennbare Kundennummer (manuell zuordnen)", nichtZuordenbar);
            Listen("Nummer passt zu keinem Kunden", unbekannteNummer);
            return;
        }

        var mitarbeiterId = await ImportMitarbeiterIdAsync(db);
        var nummernkreis = new NummernkreisDienst(db);
        var vertraegeNeu = 0;
        var dokumenteNeu = 0;
        var verarbeitet = 0;

        foreach (var (kundeId, dateien) in proKunde)
        {
            if (!bestandsVertraege.TryGetValue(kundeId, out var vertrag))
            {
                // Nummer ziehen speichert zwischendurch — Reihenfolge: erst Nummer, dann Vertrag
                var nummer = await nummernkreis.NaechsteNummerAsync("Vertrag");
                vertrag = new Vertrag
                {
                    Vertragsnummer = nummer,
                    KundeId = kundeId,
                    Status = VertragsStatus.Aktiv,
                    Beginn = DateOnly.FromDateTime(dateien.Min(d => d.LastWriteTime)),
                    Notiz = $"{NotizMarker} — Laufzeit/Kündigungsfrist bitte nachpflegen.",
                };
                db.Vertraege.Add(vertrag);
                bestandsVertraege[kundeId] = vertrag;
                vertraegeNeu++;

                db.Aktivitaeten.Add(new Aktivitaet
                {
                    KundeId = kundeId,
                    Typ = AktivitaetsTyp.Vertrag,
                    Zeitpunkt = DateTime.UtcNow,
                    MitarbeiterId = mitarbeiterId,
                    Richtung = AktivitaetsRichtung.Intern,
                    Betreff = $"Vertrag {nummer} aus Bestandsimport angelegt ({dateien.Count} Datei(en))",
                });
            }

            // Basisdatei = reine Kundennummer; sonst älteste Datei als Hauptvertrag
            var basisDatei = dateien
                .FirstOrDefault(d => Regex.IsMatch(Path.GetFileNameWithoutExtension(d.Name), @"^\d+$"))
                ?? dateien.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).First();

            foreach (var datei in dateien.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                var istBasis = datei == basisDatei;
                var typ = istBasis ? BestandteilTyp.Wartungsvertrag : BestandteilTyp.Nachtrag;
                var titel = istBasis ? "Wartungsvertrag" : Path.GetFileNameWithoutExtension(datei.Name);

                var bestandteil = vertrag.Bestandteile
                    .FirstOrDefault(b => b.Typ == typ && b.Titel == titel);
                if (bestandteil is null)
                {
                    bestandteil = new Vertragsbestandteil { Typ = typ, Titel = titel, Vertrag = vertrag };
                    vertrag.Bestandteile.Add(bestandteil);
                }

                string pruefsumme;
                await using (var stream = datei.OpenRead())
                {
                    pruefsumme = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
                }

                bestandteil.Dokumente.Add(new Vertragsdokument
                {
                    Vertragsbestandteil = bestandteil,
                    Version = (bestandteil.Dokumente.Count == 0 ? 0 : bestandteil.Dokumente.Max(d => d.Version)) + 1,
                    Dateipfad = datei.FullName,
                    DateinameOriginal = datei.Name,
                    Sha256 = pruefsumme,
                    ErstelltAm = DateTime.UtcNow,
                    ErstelltVonId = mitarbeiterId,
                });
                dokumenteNeu++;
            }

            if (++verarbeitet % 50 == 0)
            {
                await db.SaveChangesAsync();
                Console.WriteLine($"  … {verarbeitet}/{proKunde.Count} Kunden verarbeitet");
            }
        }

        await db.SaveChangesAsync();

        Console.WriteLine();
        Console.WriteLine($"Fertig: {vertraegeNeu} Verträge neu, {dokumenteNeu} Dokumentversionen registriert.");
        Listen("Ohne erkennbare Kundennummer (manuell zuordnen)", nichtZuordenbar);
        Listen("Nummer passt zu keinem Kunden", unbekannteNummer);
    }

    private static async Task<int> ImportMitarbeiterIdAsync(ErpDbContext db)
    {
        var vorhanden = await db.Mitarbeiter.FirstOrDefaultAsync(m => m.EntraObjectId == ImportBenutzerOid);
        if (vorhanden is not null)
            return vorhanden.Id;

        var import = new Mitarbeiter
        {
            EntraObjectId = ImportBenutzerOid,
            Anzeigename = "Bestandsimport",
            Email = "bestandsimport@system.lokal",
            Aktiv = false, // Systemkonto, keine Anmeldung
        };
        db.Mitarbeiter.Add(import);
        await db.SaveChangesAsync();
        return import.Id;
    }

    private static void Listen(string titel, List<string> eintraege)
    {
        if (eintraege.Count == 0)
            return;
        Console.WriteLine();
        Console.WriteLine($"{titel} ({eintraege.Count}):");
        foreach (var eintrag in eintraege.OrderBy(e => e))
            Console.WriteLine($"  {eintrag}");
    }
}
