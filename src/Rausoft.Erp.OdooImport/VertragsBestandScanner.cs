using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Contracts;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.OdooImport;

/// <summary>
/// Erfasst den Bestand der Kundendokumente aus dem Ablage-Basisverzeichnis
/// (Admin-Einstellung Vertragsablage.Verzeichnis = "Kunden Archiv").
/// Je Unterordner ist eine Dokumentart konfiguriert; die Zuordnung läuft über
/// die Kundennummer am Dateianfang. Je Kunde entsteht EINE Vertragsakte
/// (Notiz-Marker "Bestandsimport…"; Status Aktiv, sobald ein Hauptvertrag
/// dabei ist, sonst Entwurf). Im Hauptvertrags-Ordner gilt:
///   4228.pdf   → Bestandteil "Wartungsvertrag"
///   4228-1.pdf → Bestandteil "Nachtrag"
/// Idempotent: registrierte Dateipfade werden übersprungen, die Bestands-
/// Vertragsakte eines Kunden wird wiederverwendet.
/// </summary>
public partial class VertragsBestandScanner(bool dryRun)
{
    private const string NotizMarker = "Bestandsimport aus der Vertragsablage";
    private static readonly Guid ImportBenutzerOid = new("22222222-2222-2222-2222-222222222222");
    private static readonly string[] Endungen = ["*.pdf", "*.doc", "*.docx"];

    /// <summary>Unterordner des Basisverzeichnisses und ihre Dokumentart.</summary>
    private static readonly (string Ordner, BestandteilTyp Typ, bool Hauptvertrag)[] Ordnerarten =
    [
        ("2_Wartungsverträge", BestandteilTyp.Wartungsvertrag, true),
        ("1_Bestellungen", BestandteilTyp.Bestellung, false),
        ("3_Installations-Protokolle", BestandteilTyp.Installationsprotokoll, false),
        ("9_Kündigungen", BestandteilTyp.Kuendigung, false),
        ("Verträge_NTT Data_Kunden", BestandteilTyp.Lizenzvertrag, false),
        ("5_Softwaremiete", BestandteilTyp.Lizenzvertrag, false),
        ("4_Datenschutz", BestandteilTyp.Sonstiges, false),
        ("6_DORA", BestandteilTyp.Sonstiges, false),
    ];

    [GeneratedRegex(@"^\D{0,3}?(\d{4,6})")]
    private static partial Regex KundennummerAmAnfang();

    private record Fund(FileInfo Datei, BestandteilTyp Typ, bool Hauptordner);

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
            Console.Error.WriteLine($"Ablage-Basisverzeichnis nicht konfiguriert oder nicht erreichbar: '{basis}'");
            return;
        }
        Console.WriteLine($"Basisverzeichnis: {basis}");

        var kundenNachNummer = await db.Kunden.ToDictionaryAsync(k => k.Kundennummer, StringComparer.OrdinalIgnoreCase);
        var registriertePfade = (await db.Vertragsdokumente.Select(d => d.Dateipfad).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bestandsVertraege = (await db.Vertraege
                .Include(v => v.Bestandteile).ThenInclude(b => b.Dokumente)
                .Where(v => v.Notiz != null && v.Notiz.StartsWith(NotizMarker))
                .ToListAsync())
            .ToDictionary(v => v.KundeId);

        // Dateien aus allen konfigurierten Ordnern einsammeln und zuordnen
        var proKunde = new Dictionary<int, List<Fund>>();
        var nichtZuordenbar = new List<string>();
        var unbekannteNummer = new List<string>();
        var bereitsRegistriert = 0;

        foreach (var (ordner, typ, hauptordner) in Ordnerarten)
        {
            var pfad = Path.Combine(basis, ordner);
            if (!Directory.Exists(pfad))
            {
                Console.WriteLine($"  Hinweis: Ordner fehlt, übersprungen: {ordner}");
                continue;
            }
            foreach (var muster in Endungen)
            foreach (var dateiPfad in Directory.EnumerateFiles(pfad, muster, SearchOption.AllDirectories))
            {
                if (registriertePfade.Contains(dateiPfad))
                {
                    bereitsRegistriert++;
                    continue;
                }
                var datei = new FileInfo(dateiPfad);
                var treffer = KundennummerAmAnfang().Match(Path.GetFileNameWithoutExtension(datei.Name));
                if (!treffer.Success)
                {
                    nichtZuordenbar.Add($"{ordner}\\{datei.Name}");
                    continue;
                }
                if (!kundenNachNummer.TryGetValue(treffer.Groups[1].Value, out var kunde))
                {
                    unbekannteNummer.Add($"{ordner}\\{datei.Name}");
                    continue;
                }
                (proKunde.TryGetValue(kunde.Id, out var liste)
                    ? liste
                    : proKunde[kunde.Id] = []).Add(new Fund(datei, typ, hauptordner));
            }
        }

        Console.WriteLine($"Zuzuordnen: {proKunde.Values.Sum(l => l.Count)} Dateien für {proKunde.Count} Kunden · " +
            $"bereits registriert: {bereitsRegistriert} · ohne Nummer: {nichtZuordenbar.Count} · " +
            $"unbekannte Nummer: {unbekannteNummer.Count}");

        if (dryRun)
        {
            var neueVertraege = proKunde.Keys.Count(kundeId => !bestandsVertraege.ContainsKey(kundeId));
            Console.WriteLine($"DRY-RUN: würde {neueVertraege} Vertragsakten anlegen " +
                $"({proKunde.Count - neueVertraege} vorhandene würden ergänzt).");
            foreach (var gruppe in proKunde.Values.SelectMany(l => l).GroupBy(f => f.Typ).OrderByDescending(g => g.Count()))
                Console.WriteLine($"  {gruppe.Key}: {gruppe.Count()} Dateien");
            Listen("Ohne erkennbare Kundennummer (manuell zuordnen)", nichtZuordenbar);
            Listen("Nummer passt zu keinem Kunden", unbekannteNummer);
            return;
        }

        var mitarbeiterId = await ImportMitarbeiterIdAsync(db);
        var nummernkreis = new NummernkreisDienst(db);
        var vertraegeNeu = 0;
        var dokumenteNeu = 0;
        var verarbeitet = 0;

        foreach (var (kundeId, funde) in proKunde)
        {
            var hatHauptvertrag = funde.Any(f => f.Hauptordner);
            var neueDokumente = 0;

            if (!bestandsVertraege.TryGetValue(kundeId, out var vertrag))
            {
                var nummer = await nummernkreis.NaechsteNummerAsync("Vertrag");
                vertrag = new Vertrag
                {
                    Vertragsnummer = nummer,
                    KundeId = kundeId,
                    // Ohne Hauptvertrag (nur Bestellungen/Protokolle) als Entwurf,
                    // damit keine scheinbar aktiven Verträge entstehen.
                    Status = hatHauptvertrag ? VertragsStatus.Aktiv : VertragsStatus.Entwurf,
                    Beginn = DateOnly.FromDateTime(funde.Min(f => f.Datei.LastWriteTime)),
                    Notiz = $"{NotizMarker} — Laufzeit/Kündigungsfrist bitte nachpflegen.",
                };
                db.Vertraege.Add(vertrag);
                bestandsVertraege[kundeId] = vertrag;
                vertraegeNeu++;

                db.Aktivitaeten.Add(TimelineEintrag(kundeId, mitarbeiterId, vertrag,
                    $"Vertragsakte {nummer} aus Bestandsimport angelegt ({funde.Count} Datei(en))"));
            }

            // Basisdatei im Hauptvertrags-Ordner: reine Kundennummer, sonst älteste
            var hauptFunde = funde.Where(f => f.Hauptordner).ToList();
            var basisDatei = hauptFunde
                .FirstOrDefault(f => Regex.IsMatch(Path.GetFileNameWithoutExtension(f.Datei.Name), @"^\d+$"))
                ?.Datei ?? hauptFunde.OrderBy(f => f.Datei.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault()?.Datei;

            foreach (var fund in funde.OrderBy(f => f.Datei.Name, StringComparer.OrdinalIgnoreCase))
            {
                var istBasis = fund.Hauptordner && fund.Datei == basisDatei;
                var typ = fund.Hauptordner
                    ? (istBasis ? BestandteilTyp.Wartungsvertrag : BestandteilTyp.Nachtrag)
                    : fund.Typ;
                var titel = istBasis ? "Wartungsvertrag" : Path.GetFileNameWithoutExtension(fund.Datei.Name);

                var bestandteil = vertrag.Bestandteile.FirstOrDefault(b => b.Typ == typ && b.Titel == titel);
                if (bestandteil is null)
                {
                    bestandteil = new Vertragsbestandteil { Typ = typ, Titel = titel, Vertrag = vertrag };
                    vertrag.Bestandteile.Add(bestandteil);
                }

                string pruefsumme;
                await using (var stream = fund.Datei.OpenRead())
                {
                    pruefsumme = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
                }

                bestandteil.Dokumente.Add(new Vertragsdokument
                {
                    Vertragsbestandteil = bestandteil,
                    Version = (bestandteil.Dokumente.Count == 0 ? 0 : bestandteil.Dokumente.Max(d => d.Version)) + 1,
                    Dateipfad = fund.Datei.FullName,
                    DateinameOriginal = fund.Datei.Name,
                    Sha256 = pruefsumme,
                    ErstelltAm = DateTime.UtcNow,
                    ErstelltVonId = mitarbeiterId,
                });
                dokumenteNeu++;
                neueDokumente++;
            }

            // Bestehende Akte ergänzt → ein Timeline-Eintrag statt vieler
            if (neueDokumente > 0 && vertrag.Id != 0)
            {
                db.Aktivitaeten.Add(TimelineEintrag(kundeId, mitarbeiterId, vertrag,
                    $"Vertragsakte {vertrag.Vertragsnummer}: {neueDokumente} Dokument(e) aus Bestandsimport ergänzt"));
            }

            if (++verarbeitet % 50 == 0)
            {
                await db.SaveChangesAsync();
                Console.WriteLine($"  … {verarbeitet}/{proKunde.Count} Kunden verarbeitet");
            }
        }

        await db.SaveChangesAsync();

        Console.WriteLine();
        Console.WriteLine($"Fertig: {vertraegeNeu} Vertragsakten neu, {dokumenteNeu} Dokumentversionen registriert.");
        Listen("Ohne erkennbare Kundennummer (manuell zuordnen)", nichtZuordenbar);
        Listen("Nummer passt zu keinem Kunden", unbekannteNummer);
    }

    private static Aktivitaet TimelineEintrag(int kundeId, int mitarbeiterId, Vertrag vertrag, string betreff) => new()
    {
        KundeId = kundeId,
        Typ = AktivitaetsTyp.Vertrag,
        Zeitpunkt = DateTime.UtcNow,
        MitarbeiterId = mitarbeiterId,
        Richtung = AktivitaetsRichtung.Intern,
        Betreff = betreff,
        VerweisTyp = AktivitaetsVerweisTyp.Vertrag,
        // VerweisId wird nach dem Speichern gesetzt, wenn die Vertrags-Id feststeht
        VerweisId = vertrag.Id != 0 ? vertrag.Id : null,
    };

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
        foreach (var eintrag in eintraege.OrderBy(e => e).Take(40))
            Console.WriteLine($"  {eintrag}");
        if (eintraege.Count > 40)
            Console.WriteLine($"  … und {eintraege.Count - 40} weitere");
    }
}
