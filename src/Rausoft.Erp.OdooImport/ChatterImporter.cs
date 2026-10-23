using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Customers;
using Rausoft.Erp.Domain.Shared;
using static Rausoft.Erp.OdooImport.OdooJson;

namespace Rausoft.Erp.OdooImport;

/// <summary>
/// Übernimmt den Odoo-Chatter (mail.message) in die Kunden-Timeline (Aktivitaet).
///
/// Quellen je Kunde (Entscheidung 22.10.2026): Firma + Personen/Adressen
/// (res.partner), CRM-Leads (crm.lead), Angebote/Aufträge (sale.order) und
/// Ausgangsrechnungen (account.move).
///
/// Seit 23.10.2026 wird <b>alles</b> übernommen: <see cref="ChatterFilter"/> legt nur
/// noch die <see cref="AktivitaetsStufe"/> fest (wesentlich, Meilenstein,
/// Systemmeldung, Feldänderung), die Kundenakte blendet nach Kategorie ein und aus.
/// Jeder Eintrag merkt sich seinen Odoo-Vorgang (Modell + Id) für Marke, Filter und
/// Direktlink.
///
/// Wiederholbar über die Odoo-Nachrichten-Id (Aktivitaet.OdooId): bereits
/// übernommene Nachrichten werden bei jedem Lauf neu bewertet und aktualisiert —
/// Regeländerungen und verbesserte Maskierung wirken so rückwirkend.
///
/// Anhänge: standardmäßig nur Metadaten mit Verweis auf das Original in Odoo.
/// Mit <c>anhaengeLaden</c> werden die Dateien zusätzlich in die Dokumentablage
/// unter &lt;Kundennummer&gt;\Odoo-Chatter\ kopiert (Archiv vor der Odoo-Abschaltung).
/// </summary>
public class ChatterImporter(OdooClient odoo, bool dryRun, string? nurKunde = null, bool anhaengeLaden = false,
    string? verbindung = null, TextWriter? ausgabe = null)
{
    private const int BatchGroesse = 2000;
    private const int IdChunk = 500;
    private const long MaxAnhangBytes = 50L * 1024 * 1024;

    /// <summary>Berichtsausgabe — Konsole im Tool, Puffer beim Aufruf aus der Administration.</summary>
    private readonly TextWriter _aus = ausgabe ?? Console.Out;
    private readonly Statistik _stat = new();
    private Dictionary<int, (string Name, string? XmlId, bool Intern)> _subtypes = [];
    /// <summary>Partner-Id des Odoo-Benutzers → (Name, Login/E-Mail).</summary>
    private Dictionary<int, (string Name, string? Login)> _benutzerNachPartner = [];
    private int? _odooBot;

    private record Quelle(int FirmaOdooId, string? Bezug, int? PersonOdooId, bool Archiviert = false);

    public async Task AusfuehrenAsync()
    {
        _aus.WriteLine(dryRun
            ? "=== ODOO-CHATTER-IMPORT (DRY-RUN — es wird nichts gespeichert) ==="
            : "=== ODOO-CHATTER-IMPORT ===");

        var verbindungsZeichenfolge = verbindung
            ?? Environment.GetEnvironmentVariable("KUNDENZENTRALE_DB")
            ?? @"Server=(localdb)\MSSQLLocalDB;Database=Kundenzentrale;Trusted_Connection=True;Encrypt=False";
        var options = new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(verbindungsZeichenfolge).Options;
        await using var db = new ErpDbContext(options);

        await odoo.AnmeldenAsync();
        await LadeStammdatenAsync();

        // Ablage nur für den Archiv-Modus (Dateien kopieren)
        string? ablage = null;
        if (anhaengeLaden)
        {
            ablage = (await db.Einstellungen.AsNoTracking()
                .Where(e => e.Schluessel == EinstellungsSchluessel.DokumentablageVerzeichnis)
                .Select(e => e.Wert).FirstOrDefaultAsync())?.Trim();
            if (string.IsNullOrWhiteSpace(ablage) || (!dryRun && !Directory.Exists(ablage)))
            {
                _aus.WriteLine(string.IsNullOrWhiteSpace(ablage)
                    ? "Abbruch: „Anhänge herunterladen“ braucht die Dokumentablage (Administration → Verzeichnisse)."
                    : $"Abbruch: Dokumentablage nicht erreichbar: {ablage}");
                return;
            }
        }
        _aus.WriteLine(anhaengeLaden
            ? "Anhänge: Verweis auf Odoo + Kopie in die Dokumentablage (Archiv)"
            : "Anhänge: nur Verweis auf das Original in Odoo");

        // --- Kundenzentrale: Kunden, Ansprechpartner, Mitarbeiter, bereits Übernommenes ---
        var kundenAbfrage = db.Kunden.Include(k => k.Ansprechpartner).Where(k => k.OdooId != null);
        if (nurKunde is not null)
        {
            var odooIdSuche = int.TryParse(nurKunde, out var z) ? z : (int?)null;
            kundenAbfrage = kundenAbfrage.Where(k => k.Kundennummer == nurKunde
                || (odooIdSuche != null && k.OdooId == odooIdSuche));
        }
        var kunden = await kundenAbfrage.ToListAsync();
        // Kundennummer hat Vorrang: trifft sie, ist eine zufällig gleiche Odoo-Id nicht gemeint
        if (nurKunde is not null && kunden.Any(k => k.Kundennummer == nurKunde))
            kunden = kunden.Where(k => k.Kundennummer == nurKunde).ToList();
        if (kunden.Count == 0)
        {
            _aus.WriteLine(nurKunde is null
                ? "Keine Kunden mit Odoo-Id vorhanden — zuerst den Kontakt-Import ('import') laufen lassen."
                : $"Kein Kunde zu '{nurKunde}' (Kundennummer oder Odoo-Id) gefunden.");
            return;
        }
        var kundeNachOdooId = kunden.ToDictionary(k => k.OdooId!.Value);
        var kundenIds = kunden.Select(k => k.Id).ToList();
        var ansprechpartnerNachOdooId = kunden.SelectMany(k => k.Ansprechpartner)
            .Where(a => a.OdooId != null)
            .GroupBy(a => a.OdooId!.Value)
            .ToDictionary(g => g.Key, g => g.First());
        var mitarbeiter = await db.Mitarbeiter.AsNoTracking().ToListAsync();

        // Bereits übernommene Nachrichten: global (Unique-Index), die der gewählten Kunden zum Aktualisieren
        var alleOdooIds = (await db.Aktivitaeten.Where(a => a.OdooId != null)
            .Select(a => a.OdooId!.Value).ToListAsync()).ToHashSet();
        var vorhandene = await db.Aktivitaeten
            .Where(a => a.OdooId != null && kundenIds.Contains(a.KundeId))
            .ToDictionaryAsync(a => a.OdooId!.Value);
        var vorhandeneAnhaenge = (await db.Aktivitaetsanhaenge
                .Where(a => a.OdooId != null).Select(a => a.OdooId!.Value).ToListAsync())
            .ToHashSet();
        _aus.WriteLine($"Kunden mit Odoo-Id: {kunden.Count} · bereits übernommene Nachrichten: {vorhandene.Count}");

        // --- Odoo: Datensätze, an denen Nachrichten hängen, je Kunde zuordnen ---
        var quellen = await QuellenErmittelnAsync(kundeNachOdooId.Keys.ToHashSet(), kundeNachOdooId);
        _aus.WriteLine($"Odoo-Datensätze der Kunden: {quellen.Count} " +
                       $"({string.Join(", ", quellen.Keys.GroupBy(k => k.Modell).Select(g => $"{g.Key} {g.Count()}"))})");

        // --- Nachrichten laden (je Modell in Id-Blöcken) ---
        var roh = new List<JsonElement>();
        foreach (var gruppe in quellen.Keys.GroupBy(k => k.Modell))
        {
            foreach (var block in gruppe.Select(k => k.Id).Chunk(IdChunk))
            {
                roh.AddRange(await LadeAlleAsync("mail.message",
                    [new object[] { "model", "=", gruppe.Key }, new object[] { "res_id", "in", block }],
                    ["id", "model", "res_id", "message_type", "subtype_id", "author_id", "email_from",
                     "date", "subject", "body", "is_internal", "tracking_value_ids", "attachment_ids"]));
            }
        }
        _stat.Gelesen = roh.Count;
        _aus.WriteLine($"Nachrichten gelesen: {roh.Count}");

        // Feldänderungen aller Nachrichten — für die Regeln (Wartung, Auftragsstatus) und die Anzeige
        var tracking = await TrackingLadenAsync(roh
            .Where(m => AnzahlIds(m, "tracking_value_ids") > 0)
            .Select(m => m.GetProperty("id").GetInt32()));

        // --- Bewerten, anlegen oder aktualisieren ---
        var filter = new ChatterFilter(_benutzerNachPartner.Keys.ToHashSet(), _odooBot);
        var neue = new List<(Aktivitaet Eintrag, List<int> AnhangIds)>();
        var bestehendeMitAnhaengen = new List<(Aktivitaet Eintrag, List<int> AnhangIds)>();

        foreach (var m in roh.OrderBy(m => Text(m, "date")).ThenBy(m => m.GetProperty("id").GetInt32()))
        {
            var modell = Text(m, "model") ?? "";
            var resId = Zahl(m, "res_id") ?? 0;
            if (!quellen.TryGetValue((modell, resId), out var quelle))
                continue;
            var n = Umwandeln(m, tracking, quelle.Archiviert);
            var b = filter.Bewerten(n);
            var stufe = ChatterFilter.StufeVon(b);
            var kunde = kundeNachOdooId[quelle.FirmaOdooId];
            var soll = Eintrag(n, b, stufe, quelle, kunde, mitarbeiter, ansprechpartnerNachOdooId);
            _stat.Zaehlen(stufe, b.Grund);

            var anhangIds = Ids(m, "attachment_ids").Where(id => !vorhandeneAnhaenge.Contains(id)).ToList();
            if (vorhandene.TryGetValue(n.Id, out var ist))
            {
                if (Abgleichen(ist, soll)) _stat.Aktualisiert++; else _stat.Unveraendert++;
                if (anhangIds.Count > 0)
                    bestehendeMitAnhaengen.Add((ist, anhangIds));
            }
            else if (alleOdooIds.Contains(n.Id))
            {
                // Hängt bereits an einem anderen Kunden (außerhalb der Auswahl) — nicht doppelt anlegen
                _stat.AndererKunde++;
            }
            else
            {
                _stat.Neu++;
                _stat.JeKunde[kunde.Kundennummer] = _stat.JeKunde.GetValueOrDefault(kunde.Kundennummer) + 1;
                neue.Add((soll, anhangIds));
                alleOdooIds.Add(n.Id);
            }
        }

        _aus.WriteLine($"Neu: {_stat.Neu} · aktualisiert: {_stat.Aktualisiert} · unverändert: {_stat.Unveraendert}" +
                       (_stat.AndererKunde > 0 ? $" · an anderem Kunden: {_stat.AndererKunde}" : ""));

        // --- Anhang-Metadaten (ein Aufruf je Block statt je Datei) ---
        var alleAnhangIds = neue.SelectMany(e => e.AnhangIds)
            .Concat(bestehendeMitAnhaengen.SelectMany(e => e.AnhangIds)).Distinct().ToArray();
        var anhangMeta = await AnhangMetadatenAsync(alleAnhangIds);

        if (dryRun)
        {
            foreach (var meta in anhangMeta.Values)
                _stat.AnhangZaehlen(meta);
        }
        else
        {
            var kundeNachId = kunden.ToDictionary(k => k.Id);
            await db.SaveChangesAsync();   // Aktualisierungen der bestehenden Einträge
            foreach (var block in neue.Chunk(500))
            {
                db.Aktivitaeten.AddRange(block.Select(e => e.Eintrag));
                await db.SaveChangesAsync();   // vergibt die Ids, die die Anhänge brauchen
                foreach (var (eintrag, ids) in block)
                    AnhaengeAnlegen(db, eintrag, ids, anhangMeta, vorhandeneAnhaenge);
                await db.SaveChangesAsync();
            }
            foreach (var (eintrag, ids) in bestehendeMitAnhaengen)
                AnhaengeAnlegen(db, eintrag, ids, anhangMeta, vorhandeneAnhaenge);
            await db.SaveChangesAsync();

            if (ablage is not null)
                await AnhaengeArchivierenAsync(db, kundenIds, kundeNachId, ablage);
        }

        AusgabeStatistik();
    }

    // --- Eintrag bauen und abgleichen -------------------------------------------------

    private Aktivitaet Eintrag(ChatterNachricht n, ChatterBewertung b, AktivitaetsStufe stufe, Quelle quelle,
        Kunde kunde, List<Mitarbeiter> mitarbeiter, Dictionary<int, Ansprechpartner> ansprechpartnerNachOdooId)
    {
        var (betreff, inhalt) = stufe switch
        {
            AktivitaetsStufe.Wesentlich => BetreffUndInhalt(n),
            AktivitaetsStufe.Meilenstein => Verdichtet(n, b),
            AktivitaetsStufe.Feldaenderung => Feldaenderung(n),
            _ => Systemmeldung(n, b),
        };

        var eintrag = new Aktivitaet
        {
            KundeId = kunde.Id,
            Typ = b.Typ ?? (n.MessageType is "email" or "email_outgoing" ? AktivitaetsTyp.EMail : AktivitaetsTyp.System),
            Richtung = b.Richtung ?? AktivitaetsRichtung.Intern,
            Zeitpunkt = n.Datum?.ToUniversalTime() ?? DateTime.UtcNow,
            Betreff = Kuerzen(betreff, 300)!,
            Inhalt = inhalt,
            Quelle = AktivitaetsQuelle.OdooImport,
            OdooId = n.Id,
            Bezug = Kuerzen(quelle.Bezug, 300),
            Stufe = stufe,
            FilterGrund = Kuerzen(b.Grund, 100),
            OdooModell = n.Modell,
            OdooResId = n.ResId,
        };

        // Gesprächspaar: Autor → Mitarbeiter oder Ansprechpartner; sonst Name als Text
        if (n.AutorPartnerId is { } autorId)
        {
            if (_benutzerNachPartner.TryGetValue(autorId, out var benutzer))
            {
                var m = mitarbeiter.FirstOrDefault(x =>
                    (benutzer.Login is not null && x.Email.Equals(benutzer.Login, StringComparison.OrdinalIgnoreCase))
                    || x.Anzeigename.Equals(benutzer.Name, StringComparison.OrdinalIgnoreCase));
                if (m is not null)
                    eintrag.MitarbeiterId = m.Id;
                else
                {
                    eintrag.AutorName = Kuerzen(benutzer.Name, 200);
                    if (stufe <= AktivitaetsStufe.Meilenstein)
                        _stat.UnbekannteBenutzer.Add(benutzer.Name);
                }
            }
            else if (ansprechpartnerNachOdooId.TryGetValue(autorId, out var ap) && ap.KundeId == kunde.Id)
                eintrag.AnsprechpartnerId = ap.Id;
            else
                eintrag.AutorName = Kuerzen(n.AutorName ?? n.EmailFrom, 200);
        }
        else if (n.EmailFrom is not null)
            eintrag.AutorName = Kuerzen(n.EmailFrom, 200);

        // Nachricht hing an einer Person des Kunden → sie ist die Kundenseite des Gesprächs
        if (eintrag.AnsprechpartnerId is null && quelle.PersonOdooId is { } personId
            && ansprechpartnerNachOdooId.TryGetValue(personId, out var person) && person.KundeId == kunde.Id)
            eintrag.AnsprechpartnerId = person.Id;

        return eintrag;
    }

    /// <summary>Bestehenden Eintrag auf den neu bewerteten Stand bringen; true = etwas geändert.</summary>
    private static bool Abgleichen(Aktivitaet ist, Aktivitaet soll)
    {
        var geaendert = false;
        void Setze<T>(T alt, T neu, Action<T> setzen)
        {
            if (EqualityComparer<T>.Default.Equals(alt, neu))
                return;
            setzen(neu);
            geaendert = true;
        }

        Setze(ist.KundeId, soll.KundeId, v => ist.KundeId = v);
        Setze(ist.Typ, soll.Typ, v => ist.Typ = v);
        Setze(ist.Richtung, soll.Richtung, v => ist.Richtung = v);
        Setze(ist.Betreff, soll.Betreff, v => ist.Betreff = v);
        Setze(ist.Inhalt, soll.Inhalt, v => ist.Inhalt = v);
        Setze(ist.Bezug, soll.Bezug, v => ist.Bezug = v);
        Setze(ist.Stufe, soll.Stufe, v => ist.Stufe = v);
        Setze(ist.FilterGrund, soll.FilterGrund, v => ist.FilterGrund = v);
        Setze(ist.OdooModell, soll.OdooModell, v => ist.OdooModell = v);
        Setze(ist.OdooResId, soll.OdooResId, v => ist.OdooResId = v);
        Setze(ist.MitarbeiterId, soll.MitarbeiterId, v => ist.MitarbeiterId = v);
        Setze(ist.AnsprechpartnerId, soll.AnsprechpartnerId, v => ist.AnsprechpartnerId = v);
        Setze(ist.AutorName, soll.AutorName, v => ist.AutorName = v);
        return geaendert;
    }

    private static (string Betreff, string? Inhalt) BetreffUndInhalt(ChatterNachricht n)
    {
        var text = n.Klartext;
        var betreff = n.Betreff ?? ErsteZeile(text);
        if (string.IsNullOrWhiteSpace(betreff))
            betreff = n.MessageType is "email" or "email_outgoing" ? "E-Mail" : "Notiz";
        // Inhalt nur, wenn er mehr sagt als der Betreff
        var inhalt = string.IsNullOrWhiteSpace(text) || text.Equals(betreff, StringComparison.Ordinal) ? null : text;
        return (betreff, inhalt);
    }

    /// <summary>Verdichtete Meilensteine: kurzer deutscher Betreff, nur die entscheidenden Feldänderungen als Inhalt.</summary>
    private static (string Betreff, string? Inhalt) Verdichtet(ChatterNachricht n, ChatterBewertung b)
    {
        string? Aenderung(string feldAnfang) =>
            n.Aenderungen.FirstOrDefault(a => a.Feld.StartsWith(feldAnfang, StringComparison.OrdinalIgnoreCase)) is { Feld: not null } a
                ? $"{a.Alt} → {a.Neu}"
                : null;

        var betreff = n.SubtypeXmlId switch
        {
            "crm.mt_lead_create" => "Verkaufschance angelegt",
            "crm.mt_lead_won" => "Verkaufschance gewonnen",
            "crm.mt_lead_lost" => "Verkaufschance verloren",
            "crm.mt_lead_restored" => "Verkaufschance wiederhergestellt",
            "crm.mt_lead_stage" => (Aenderung("Stage") ?? Aenderung("Phase")) is { } p ? $"Phase: {p}" : "Phase geändert",
            "sale.mt_order_sent" => "Angebot gesendet",
            "sale.mt_order_confirmed" => "Auftrag bestätigt",
            "account.mt_invoice_validated" => "Rechnung gestellt",
            "account.mt_invoice_paid" => "Rechnung bezahlt",
            _ => null,
        };
        betreff ??= b.Grund switch
        {
            "Wartungs-Haken geändert" => WartungText(n),
            "Auftragsstatus geändert" => (Aenderung("Status") ?? Aenderung("State")) is { } s ? $"Status: {s}" : "Auftragsstatus geändert",
            _ => ErsteZeile(n.Klartext) is { Length: > 0 } z ? z : b.Grund,
        };

        // Nur die Feldänderungen, die den Meilenstein ausmachen — Beträge, Adressen und
        // Termine gehören nicht in die Akte
        string[] relevant = ["Status", "State", "Stage", "Phase", "Won", "Wartung", "Payment Status", "Zahlungsstatus"];
        var zeilen = n.Aenderungen
            .Where(a => relevant.Any(r => a.Feld.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
            .Where(a => !(a.Alt == "—" && a.Neu == "—"))
            .Select(a => $"{FeldName(a.Feld)}: {a.Alt} → {a.Neu}")
            .ToList();
        return (betreff, zeilen.Count > 0 ? string.Join("\n", zeilen) : null);
    }

    /// <summary>Reine Feldänderung: Betreff nennt die Felder, Inhalt die Änderungen.</summary>
    private static (string Betreff, string? Inhalt) Feldaenderung(ChatterNachricht n)
    {
        if (n.Aenderungen.Count == 0)
            return (ErsteZeile(n.Klartext) is { Length: > 0 } z ? z : "Feldänderung", null);
        var felder = n.Aenderungen.Select(a => FeldName(a.Feld)).Distinct().ToList();
        var betreff = "Geändert: " + string.Join(", ", felder.Take(4)) + (felder.Count > 4 ? $" (+{felder.Count - 4})" : "");
        var inhalt = string.Join("\n", n.Aenderungen.Select(a => $"{FeldName(a.Feld)}: {a.Alt} → {a.Neu}"));
        return (betreff, inhalt);
    }

    private static (string Betreff, string? Inhalt) Systemmeldung(ChatterNachricht n, ChatterBewertung b)
    {
        var betreff = n.Betreff ?? (ErsteZeile(n.Klartext) is { Length: > 0 } z ? z : b.Grund);
        var inhalt = string.IsNullOrWhiteSpace(n.Klartext) || n.Klartext.Equals(betreff, StringComparison.Ordinal)
            ? null
            : n.Klartext;
        return (betreff, inhalt);
    }

    private static string WartungText(ChatterNachricht n)
    {
        var w = n.Aenderungen.FirstOrDefault(a => a.Feld.StartsWith("Wartung", StringComparison.OrdinalIgnoreCase));
        return w.Neu is "1" or "Ja" or "true" or "True" ? "Wartung aktiviert" : "Wartung deaktiviert";
    }

    private static string ErsteZeile(string text)
    {
        var zeile = text.Split('\n', 2, StringSplitOptions.TrimEntries)[0];
        return zeile.Length <= 120 ? zeile : zeile[..119] + "…";
    }

    // --- Anhänge ---------------------------------------------------------------------

    private record AnhangMeta(int Id, string Name, string? MimeType, long Groesse);

    private async Task<Dictionary<int, AnhangMeta>> AnhangMetadatenAsync(int[] ids)
    {
        var ergebnis = new Dictionary<int, AnhangMeta>();
        foreach (var block in ids.Chunk(IdChunk))
        {
            try
            {
                foreach (var d in await LadeAlleAsync("ir.attachment", [new object[] { "id", "in", block }],
                             ["id", "name", "mimetype", "file_size"]))
                {
                    var id = d.GetProperty("id").GetInt32();
                    ergebnis[id] = new AnhangMeta(id, Text(d, "name") ?? $"anhang-{id}", Text(d, "mimetype"),
                        Zahl(d, "file_size") ?? 0);
                }
            }
            catch (InvalidOperationException ex)
            {
                _stat.AnhangFehler.Add($"Metadaten nicht lesbar: {Kuerzen(ex.Message, 300)}");
            }
        }
        return ergebnis;
    }

    /// <summary>Anhang-Verweise anlegen (ohne Datei); Signaturbilder werden ausgelassen.</summary>
    private void AnhaengeAnlegen(ErpDbContext db, Aktivitaet eintrag, List<int> ids,
        Dictionary<int, AnhangMeta> meta, HashSet<int> vorhandeneAnhaenge)
    {
        foreach (var id in ids)
        {
            if (vorhandeneAnhaenge.Contains(id) || !meta.TryGetValue(id, out var a))
                continue;
            if (!_stat.AnhangZaehlen(a))
                continue;
            db.Aktivitaetsanhaenge.Add(new AktivitaetAnhang
            {
                AktivitaetId = eintrag.Id,
                Dateiname = Kuerzen(a.Name, 300)!,
                MimeType = Kuerzen(a.MimeType, 100),
                Groesse = a.Groesse,
                RelativerPfad = null,
                OdooId = id,
            });
            vorhandeneAnhaenge.Add(id);
        }
    }

    /// <summary>Archiv-Modus: alle Anhänge der gewählten Kunden ohne lokale Datei herunterladen.</summary>
    private async Task AnhaengeArchivierenAsync(ErpDbContext db, List<int> kundenIds, Dictionary<int, Kunde> kundeNachId,
        string ablage)
    {
        var offen = await db.Aktivitaetsanhaenge
            .Include(a => a.Aktivitaet)
            .Where(a => a.OdooId != null && a.RelativerPfad == null && kundenIds.Contains(a.Aktivitaet!.KundeId))
            .ToListAsync();
        foreach (var anhang in offen)
        {
            if (anhang.Groesse > MaxAnhangBytes)
            {
                _stat.AnhangFehler.Add($"{anhang.Dateiname}: {anhang.Groesse / 1024 / 1024} MB — nicht archiviert (Grenze {MaxAnhangBytes / 1024 / 1024} MB)");
                continue;
            }
            byte[] bytes;
            try
            {
                bytes = await AnhangInhaltLadenAsync(odoo, anhang.OdooId!.Value);
            }
            catch (InvalidOperationException ex)
            {
                _stat.AnhangFehler.Add($"{anhang.Dateiname} (#{anhang.OdooId}): {Kuerzen(ex.Message, 400)}");
                continue;
            }
            if (bytes.Length == 0)
            {
                _stat.AnhangFehler.Add($"{anhang.Dateiname} (#{anhang.OdooId}): Odoo lieferte keinen Inhalt.");
                continue;
            }

            var kunde = kundeNachId[anhang.Aktivitaet!.KundeId];
            var relativerOrdner = Path.Combine(SichererName(kunde.Kundennummer), "Odoo-Chatter");
            Directory.CreateDirectory(Path.Combine(ablage, relativerOrdner));
            var relativerPfad = Path.Combine(relativerOrdner, $"{anhang.Aktivitaet.OdooId}_{SichererName(anhang.Dateiname)}");
            await File.WriteAllBytesAsync(Path.Combine(ablage, relativerPfad), bytes);
            anhang.RelativerPfad = relativerPfad;
            anhang.Groesse = bytes.LongLength;
            _stat.Archiviert++;
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Dateiinhalt eines Odoo-Anhangs als Bytes. Odoo liefert Binärfelder per RPC
    /// Base64-kodiert; je nach Version heißt das Feld <c>datas</c> oder nur noch
    /// <c>raw</c>, daher beide Wege. Wirft InvalidOperationException mit der
    /// vollständigen Odoo-Meldung, wenn keiner funktioniert.
    /// </summary>
    public static async Task<byte[]> AnhangInhaltLadenAsync(OdooClient odoo, int anhangId)
    {
        var fehler = new List<string>();
        foreach (var feld in new[] { "datas", "raw" })
        {
            try
            {
                var liste = await odoo.AusfuehrenAsync("ir.attachment", "read",
                    [new[] { anhangId }],
                    new Dictionary<string, object> { ["fields"] = new[] { feld } });
                if (liste.ValueKind != JsonValueKind.Array || liste.GetArrayLength() == 0)
                    throw new InvalidOperationException($"Anhang #{anhangId} nicht gefunden oder nicht lesbar.");
                var wert = liste[0].TryGetProperty(feld, out var w) ? w : default;
                if (wert.ValueKind != JsonValueKind.String)
                    return [];
                return Convert.FromBase64String(wert.GetString()!);
            }
            catch (InvalidOperationException ex)
            {
                fehler.Add($"{feld}: {ex.Message}");
            }
            catch (FormatException ex)
            {
                fehler.Add($"{feld}: kein gültiges Base64 ({ex.Message})");
            }
        }
        throw new InvalidOperationException(string.Join(" | ", fehler));
    }

    private static string SichererName(string name)
    {
        var ungueltig = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(ungueltig.Contains(c) ? '_' : c);
        var s = sb.ToString().Trim().TrimEnd('.');
        return s.Length == 0 ? "datei" : (s.Length > 150 ? s[..150] : s);
    }

    // --- Odoo-Zuordnung ------------------------------------------------------------

    /// <summary>Alle Odoo-Datensätze, deren Nachrichten einem unserer Kunden gehören, mit Anzeigename des Vorgangs.</summary>
    private async Task<Dictionary<(string Modell, int Id), Quelle>> QuellenErmittelnAsync(HashSet<int> firmaIds,
        Dictionary<int, Kunde> kundeNachOdooId)
    {
        var quellen = new Dictionary<(string, int), Quelle>();

        // Partner: Firma selbst sowie Personen/Adressen über commercial_partner_id
        var partner = await LadeAlleAsync("res.partner", [], ["id", "name", "is_company", "commercial_partner_id", "type"]);
        var firmaVonPartner = new Dictionary<int, int>();
        foreach (var p in partner)
        {
            var id = p.GetProperty("id").GetInt32();
            var firma = Wahr(p, "is_company") ? id : Many2OneId(p, "commercial_partner_id") ?? id;
            if (!firmaIds.Contains(firma))
                continue;
            firmaVonPartner[id] = firma;
            var typ = Text(p, "type");
            quellen[("res.partner", id)] = id == firma
                ? new Quelle(firma, "Firma", null)
                : typ is null or "contact"
                    ? new Quelle(firma, $"Kontakt {Text(p, "name")}", id)
                    : new Quelle(firma, $"Adresse {Text(p, "name")}", null);
        }

        int? FirmaZu(JsonElement e, string feld) =>
            Many2OneId(e, feld) is { } pid && firmaVonPartner.TryGetValue(pid, out var f) ? f : null;

        var leads = await LadeAlleAsync("crm.lead", [new object[] { "partner_id", "!=", false }],
            ["id", "name", "partner_id", "type", "active"]);
        foreach (var l in leads)
            if (FirmaZu(l, "partner_id") is { } firma)
                quellen[("crm.lead", l.GetProperty("id").GetInt32())] =
                    new Quelle(firma, $"{(Text(l, "type") == "lead" ? "Lead" : "Verkaufschance")} {Text(l, "name")}", null,
                        Archiviert: !Wahr(l, "active"));

        var auftraege = await LadeAlleAsync("sale.order", [], ["id", "name", "state", "partner_id"]);
        foreach (var a in auftraege)
            if (FirmaZu(a, "partner_id") is { } firma)
                quellen[("sale.order", a.GetProperty("id").GetInt32())] =
                    new Quelle(firma, $"{(Text(a, "state") is "sale" or "done" ? "Auftrag" : "Angebot")} {Text(a, "name")}", null);

        var rechnungen = await LadeAlleAsync("account.move",
            [new object[] { "move_type", "in", new[] { "out_invoice", "out_refund" } }],
            ["id", "name", "move_type", "partner_id", "commercial_partner_id"]);
        foreach (var r in rechnungen)
        {
            var firma = Many2OneId(r, "commercial_partner_id") is { } c && firmaIds.Contains(c) ? c : FirmaZu(r, "partner_id");
            if (firma is not null)
                quellen[("account.move", r.GetProperty("id").GetInt32())] =
                    new Quelle(firma.Value, $"{(Text(r, "move_type") == "out_refund" ? "Gutschrift" : "Rechnung")} {Text(r, "name")}", null);
        }
        return quellen;
    }

    private ChatterNachricht Umwandeln(JsonElement m, Dictionary<int, List<(string Feld, string Alt, string Neu)>> tracking,
        bool quelleArchiviert)
    {
        var subtypeId = Many2OneId(m, "subtype_id");
        _subtypes.TryGetValue(subtypeId ?? -1, out var subtype);
        var id = m.GetProperty("id").GetInt32();
        return new ChatterNachricht(
            Id: id,
            Modell: Text(m, "model") ?? "?",
            ResId: Zahl(m, "res_id") ?? 0,
            RecordName: null,
            QuelleArchiviert: quelleArchiviert,
            MessageType: Text(m, "message_type"),
            SubtypeId: subtypeId,
            SubtypeXmlId: subtype.XmlId,
            SubtypeName: subtype.Name ?? Many2OneLabel(m, "subtype_id"),
            AutorPartnerId: Many2OneId(m, "author_id"),
            AutorName: Many2OneLabel(m, "author_id"),
            EmailFrom: Text(m, "email_from"),
            Datum: Zeitpunkt(m, "date"),
            Betreff: Text(m, "subject"),
            Klartext: ChatterFilter.Klartext(Text(m, "body")),
            IstIntern: Wahr(m, "is_internal"),
            TrackingAnzahl: AnzahlIds(m, "tracking_value_ids"),
            AnhangAnzahl: AnzahlIds(m, "attachment_ids"),
            Aenderungen: tracking.GetValueOrDefault(id) ?? []);
    }

    private async Task<Dictionary<int, List<(string Feld, string Alt, string Neu)>>> TrackingLadenAsync(IEnumerable<int> nachrichtenIds)
    {
        var ergebnis = new Dictionary<int, List<(string Feld, string Alt, string Neu)>>();
        foreach (var block in nachrichtenIds.Chunk(IdChunk))
        {
            List<JsonElement> werte;
            try
            {
                werte = await LadeAlleAsync("mail.tracking.value",
                    [new object[] { "mail_message_id", "in", block }],
                    ["mail_message_id", "field_id", "old_value_char", "new_value_char", "old_value_integer",
                     "new_value_integer", "old_value_float", "new_value_float", "old_value_datetime",
                     "new_value_datetime", "old_value_text", "new_value_text"]);
            }
            catch (InvalidOperationException ex)
            {
                _aus.WriteLine($"Hinweis: Feldänderungen nicht lesbar ({Kuerzen(ex.Message, 100)}) — Wartungs-/Statusregeln greifen nicht.");
                return ergebnis;
            }
            foreach (var w in werte)
            {
                var messageId = Many2OneId(w, "mail_message_id") ?? 0;
                ergebnis.TryAdd(messageId, []);
                ergebnis[messageId].Add((Many2OneLabel(w, "field_id") ?? "Feld", TrackingWert(w, "old"), TrackingWert(w, "new")));
            }
        }
        return ergebnis;
    }

    private async Task LadeStammdatenAsync()
    {
        var xmlIds = new Dictionary<int, string>();
        try
        {
            var daten = await LadeAlleAsync("ir.model.data",
                [new object[] { "model", "=", "mail.message.subtype" }], ["res_id", "module", "name"]);
            foreach (var d in daten)
                xmlIds[Zahl(d, "res_id") ?? 0] = $"{Text(d, "module")}.{Text(d, "name")}";
        }
        catch (InvalidOperationException)
        {
            _aus.WriteLine("Hinweis: ir.model.data nicht lesbar — Untertypen werden nur per Name erkannt.");
        }
        var subtypes = await LadeAlleAsync("mail.message.subtype", [], ["id", "name", "internal"]);
        _subtypes = subtypes.ToDictionary(s => s.GetProperty("id").GetInt32(),
            s => (Text(s, "name") ?? "?", xmlIds.GetValueOrDefault(s.GetProperty("id").GetInt32()), Wahr(s, "internal")));

        var benutzer = await LadeAlleAsync("res.users", [], ["id", "name", "login", "partner_id", "share"]);
        _benutzerNachPartner = benutzer.Where(b => !Wahr(b, "share") && Many2OneId(b, "partner_id") is not null)
            .GroupBy(b => Many2OneId(b, "partner_id")!.Value)
            .ToDictionary(g => g.Key, g => (Text(g.First(), "name") ?? "?", Text(g.First(), "login")));

        try
        {
            var bot = await odoo.AusfuehrenAsync("ir.model.data", "search_read",
                [new object[] { new object[] { "module", "=", "base" }, new object[] { "name", "=", "partner_root" } }],
                new Dictionary<string, object> { ["fields"] = new[] { "res_id" }, ["limit"] = 1 });
            _odooBot = bot.EnumerateArray().Select(b => Zahl(b, "res_id")).FirstOrDefault();
        }
        catch (InvalidOperationException)
        {
            // Namenserkennung im Filter greift
        }
    }

    private async Task<List<JsonElement>> LadeAlleAsync(string modell, object[] domain, string[] felder, string order = "id")
    {
        var alle = new List<JsonElement>();
        for (var offset = 0; ; offset += BatchGroesse)
        {
            var batch = await odoo.AusfuehrenAsync(modell, "search_read", [domain],
                new Dictionary<string, object>
                {
                    ["fields"] = felder,
                    ["limit"] = BatchGroesse,
                    ["offset"] = offset,
                    ["order"] = order,
                    ["context"] = new Dictionary<string, object> { ["active_test"] = false },
                });
            var anzahl = 0;
            foreach (var element in batch.EnumerateArray())
            {
                alle.Add(element);
                anzahl++;
            }
            if (anzahl < BatchGroesse)
                return alle;
        }
    }

    // --- Ausgabe -----------------------------------------------------------------------

    private void AusgabeStatistik()
    {
        _aus.WriteLine();
        _aus.WriteLine("--- Ergebnis ---");
        _aus.WriteLine($"Nachrichten gelesen:        {_stat.Gelesen}");
        _aus.WriteLine($"Neu angelegt:               {_stat.Neu}");
        _aus.WriteLine($"Aktualisiert (neu bewertet):{_stat.Aktualisiert,5}");
        _aus.WriteLine($"Unverändert:                {_stat.Unveraendert}");
        _aus.WriteLine();
        _aus.WriteLine("Nach Stufe (alle Nachrichten, in der Akte umschaltbar):");
        foreach (var stufe in Enum.GetValues<AktivitaetsStufe>())
            _aus.WriteLine($"  {StufeText(stufe),-36} {_stat.JeStufe.GetValueOrDefault(stufe),7}");
        _aus.WriteLine();
        _aus.WriteLine("Nach Grund:");
        foreach (var (grund, anzahl) in _stat.Gruende.OrderByDescending(g => g.Value))
            _aus.WriteLine($"  {grund,-36} {anzahl,7}");
        _aus.WriteLine();
        _aus.WriteLine($"Anhänge{(dryRun ? " (würden verknüpft)" : " neu verknüpft")}: {_stat.Anhaenge} " +
                       $"({_stat.AnhangBytes / 1024.0 / 1024.0:0.0} MB in Odoo)" +
                       (_stat.AnhaengeUebersprungen > 0 ? $" · Signaturbilder übersprungen: {_stat.AnhaengeUebersprungen}" : "") +
                       (anhaengeLaden && !dryRun ? $" · in die Dokumentablage kopiert: {_stat.Archiviert}" : ""));
        if (_stat.UnbekannteBenutzer.Count > 0)
            _aus.WriteLine($"Odoo-Benutzer ohne Mitarbeiter-Zuordnung (Name als Text gespeichert): " +
                           string.Join(", ", _stat.UnbekannteBenutzer.Order()));
        if (_stat.AnhangFehler.Count > 0)
        {
            _aus.WriteLine($"Anhänge mit Problemen: {_stat.AnhangFehler.Count}");
            foreach (var f in _stat.AnhangFehler.Take(20))
                _aus.WriteLine($"  {f}");
        }
        _aus.WriteLine($"Kunden mit neuen Einträgen: {_stat.JeKunde.Count}");
        if (dryRun)
            _aus.WriteLine("DRY-RUN: nichts gespeichert.");
    }

    private static string StufeText(AktivitaetsStufe stufe) => stufe switch
    {
        AktivitaetsStufe.Wesentlich => "Wesentlich (Notizen, E-Mails)",
        AktivitaetsStufe.Meilenstein => "Meilensteine",
        AktivitaetsStufe.Systemmeldung => "Systemmeldungen (ausgeblendet)",
        _ => "Feldänderungen (ausgeblendet)",
    };

    private static string? Kuerzen(string? text, int max) =>
        text is null ? null : (text.Length <= max ? text : text[..(max - 1)] + "…");

    private class Statistik
    {
        public int Gelesen, Neu, Aktualisiert, Unveraendert, AndererKunde, Anhaenge, AnhaengeUebersprungen, Archiviert;
        public long AnhangBytes;
        public readonly Dictionary<AktivitaetsStufe, int> JeStufe = [];
        public readonly Dictionary<string, int> Gruende = [];
        public readonly Dictionary<string, int> JeKunde = [];
        public readonly HashSet<string> UnbekannteBenutzer = [];
        public readonly List<string> AnhangFehler = [];

        public void Zaehlen(AktivitaetsStufe stufe, string grund)
        {
            JeStufe[stufe] = JeStufe.GetValueOrDefault(stufe) + 1;
            Gruende[grund] = Gruende.GetValueOrDefault(grund) + 1;
        }

        /// <summary>Zählt einen Anhang; false = Signaturbild, wird nicht verknüpft.</summary>
        public bool AnhangZaehlen(AnhangMeta a)
        {
            if (ChatterFilter.IstSignaturbild(a.Name, a.MimeType, a.Groesse))
            {
                AnhaengeUebersprungen++;
                return false;
            }
            Anhaenge++;
            AnhangBytes += a.Groesse;
            return true;
        }
    }
}
