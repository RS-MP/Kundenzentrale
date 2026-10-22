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
/// Ausgangsrechnungen (account.move). Welche Nachricht übernommen, verdichtet
/// oder verworfen wird, entscheidet ausschließlich <see cref="ChatterFilter"/> —
/// dieselben Regeln wie in der Analyse (Modus <c>chatter</c>).
///
/// Idempotent über die Odoo-Nachrichten-Id (Aktivitaet.OdooId) und die
/// Anhang-Id (AktivitaetAnhang.OdooId): bereits übernommene Nachrichten werden
/// übersprungen, fehlende Anhänge nachgeladen. Anhänge landen als Dateien in der
/// Dokumentablage unter &lt;Kundennummer&gt;\Odoo-Chatter\.
/// </summary>
public class ChatterImporter(OdooClient odoo, bool dryRun, string? nurKunde = null, bool ohneAnhaenge = false,
    string? verbindung = null, TextWriter? ausgabe = null)
{
    private const int BatchGroesse = 2000;
    /// <summary>Berichtsausgabe — Konsole im Tool, Puffer beim Aufruf aus der Administration.</summary>
    private readonly TextWriter _aus = ausgabe ?? Console.Out;
    private const int IdChunk = 500;
    private const long MaxAnhangBytes = 50L * 1024 * 1024;

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

        // Ablage für Anhänge — Pflicht, sobald wirklich geschrieben wird
        string? ablage = null;
        if (!ohneAnhaenge)
        {
            ablage = (await db.Einstellungen.AsNoTracking()
                .Where(e => e.Schluessel == EinstellungsSchluessel.DokumentablageVerzeichnis)
                .Select(e => e.Wert).FirstOrDefaultAsync())?.Trim();
            if (string.IsNullOrWhiteSpace(ablage))
            {
                if (dryRun)
                {
                    _aus.WriteLine("Hinweis: Dokumentablage.Verzeichnis ist nicht konfiguriert — " +
                                      "Anhänge würden beim echten Import fehlen (Administration → Verzeichnisse).");
                    ablage = null;
                }
                else
                {
                    _aus.WriteLine("Abbruch: Dokumentablage.Verzeichnis ist nicht konfiguriert " +
                                            "(Administration → Verzeichnisse). Alternativ --ohne-anhaenge.");
                    return;
                }
            }
            else if (!dryRun && !Directory.Exists(ablage))
            {
                _aus.WriteLine($"Abbruch: Dokumentablage nicht erreichbar: {ablage}");
                return;
            }
        }

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
        var ansprechpartnerNachOdooId = kunden.SelectMany(k => k.Ansprechpartner)
            .Where(a => a.OdooId != null)
            .GroupBy(a => a.OdooId!.Value)
            .ToDictionary(g => g.Key, g => g.First());
        var mitarbeiter = await db.Mitarbeiter.AsNoTracking().ToListAsync();
        var vorhandeneNachrichten = (await db.Aktivitaeten
                .Where(a => a.OdooId != null)
                .Select(a => new { OdooId = a.OdooId!.Value, a.Id })
                .ToListAsync())
            .ToDictionary(x => x.OdooId, x => x.Id);
        var vorhandeneAnhaenge = (await db.Aktivitaetsanhaenge
                .Where(a => a.OdooId != null).Select(a => a.OdooId!.Value).ToListAsync())
            .ToHashSet();
        _aus.WriteLine($"Kunden mit Odoo-Id: {kunden.Count} · bereits übernommene Nachrichten: {vorhandeneNachrichten.Count}");

        // --- Odoo: Datensätze, an denen Nachrichten hängen, je Kunde zuordnen ---
        var firmaIds = kundeNachOdooId.Keys.ToHashSet();
        var quellen = await QuellenErmittelnAsync(firmaIds);
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

        // Feldänderungen nur dort, wo Regeln darauf schauen (Wartung am Kontakt, Status am Auftrag)
        var trackingIds = roh
            .Where(m => AnzahlIds(m, "tracking_value_ids") > 0
                        && Text(m, "model") is "res.partner" or "sale.order"
                        && !vorhandeneNachrichten.ContainsKey(m.GetProperty("id").GetInt32()))
            .Select(m => m.GetProperty("id").GetInt32());
        var tracking = await TrackingLadenAsync(trackingIds);

        // --- Bewerten und übernehmen ---
        var filter = new ChatterFilter(_benutzerNachPartner.Keys.ToHashSet(), _odooBot);
        var neueEintraege = new List<(Aktivitaet Eintrag, List<int> AnhangIds)>();
        var nachzuladendeAnhaenge = new List<(int AktivitaetId, List<int> AnhangIds)>();

        foreach (var m in roh.OrderBy(m => Text(m, "date")).ThenBy(m => m.GetProperty("id").GetInt32()))
        {
            if (!quellen.TryGetValue((Text(m, "model") ?? "", Zahl(m, "res_id") ?? 0), out var quelle))
                continue;
            var n = Umwandeln(m, tracking, quelle.Archiviert);
            var anhangIds = Ids(m, "attachment_ids");

            if (vorhandeneNachrichten.TryGetValue(n.Id, out var vorhandeneId))
            {
                _stat.Uebersprungen++;
                var fehlende = anhangIds.Where(id => !vorhandeneAnhaenge.Contains(id)).ToList();
                if (fehlende.Count > 0 && !ohneAnhaenge)
                    nachzuladendeAnhaenge.Add((vorhandeneId, fehlende));
                continue;
            }

            var b = filter.Bewerten(n);
            if (b.Entscheidung == ChatterEntscheidung.Verwerfen)
            {
                _stat.Verworfen++;
                _stat.Grund(b.Grund);
                continue;
            }

            var kunde = kundeNachOdooId[quelle.FirmaOdooId];
            var eintrag = Eintrag(n, b, quelle, kunde, mitarbeiter, ansprechpartnerNachOdooId);
            if (b.Entscheidung == ChatterEntscheidung.Behalten) _stat.Behalten++; else _stat.Verdichtet++;
            _stat.Grund(b.Grund);
            _stat.JeKunde[kunde.Kundennummer] = _stat.JeKunde.GetValueOrDefault(kunde.Kundennummer) + 1;

            // Anhänge nur an echten Einträgen (Notizen, E-Mails) — Meilensteine haben keine
            var mitAnhaengen = b.Entscheidung == ChatterEntscheidung.Behalten && !ohneAnhaenge
                ? anhangIds.Where(id => !vorhandeneAnhaenge.Contains(id)).ToList()
                : [];
            neueEintraege.Add((eintrag, mitAnhaengen));
        }

        _aus.WriteLine($"Zu übernehmen: {neueEintraege.Count} ({_stat.Behalten} Einträge, {_stat.Verdichtet} verdichtet) · " +
                          $"verworfen: {_stat.Verworfen} · bereits vorhanden: {_stat.Uebersprungen}");

        // --- Speichern (in Blöcken), Anhänge laden ---
        if (!dryRun)
        {
            var kundeNachId = kunden.ToDictionary(k => k.Id);
            foreach (var block in neueEintraege.Chunk(500))
            {
                db.Aktivitaeten.AddRange(block.Select(e => e.Eintrag));
                await db.SaveChangesAsync();   // vergibt die Ids, die die Anhänge brauchen
                if (ablage is not null)
                    foreach (var (eintrag, anhangIds) in block)
                        await AnhaengeUebernehmenAsync(db, eintrag, anhangIds, kundeNachId[eintrag.KundeId], ablage, vorhandeneAnhaenge);
                await db.SaveChangesAsync();
            }
            if (ablage is not null)
            {
                foreach (var (aktivitaetId, anhangIds) in nachzuladendeAnhaenge)
                {
                    var eintrag = await db.Aktivitaeten.FirstAsync(a => a.Id == aktivitaetId);
                    if (kundeNachId.TryGetValue(eintrag.KundeId, out var kunde))
                        await AnhaengeUebernehmenAsync(db, eintrag, anhangIds, kunde, ablage, vorhandeneAnhaenge);
                }
                await db.SaveChangesAsync();
            }
        }
        else
        {
            // Nur zählen und Größe ermitteln
            var alleAnhangIds = neueEintraege.SelectMany(e => e.AnhangIds)
                .Concat(nachzuladendeAnhaenge.SelectMany(e => e.AnhangIds)).Distinct().ToArray();
            foreach (var block in alleAnhangIds.Chunk(IdChunk))
            {
                var meta = await LadeAlleAsync("ir.attachment", [new object[] { "id", "in", block }], ["id", "file_size"]);
                foreach (var a in meta)
                {
                    _stat.Anhaenge++;
                    _stat.AnhangBytes += Zahl(a, "file_size") ?? 0;
                }
            }
        }

        AusgabeStatistik();
    }

    // --- Eintrag bauen -------------------------------------------------------------

    private Aktivitaet Eintrag(ChatterNachricht n, ChatterBewertung b, Quelle quelle, Kunde kunde,
        List<Mitarbeiter> mitarbeiter, Dictionary<int, Ansprechpartner> ansprechpartnerNachOdooId)
    {
        var (betreff, inhalt) = b.Entscheidung == ChatterEntscheidung.Behalten
            ? BetreffUndInhalt(n)
            : Verdichtet(n, b);

        var eintrag = new Aktivitaet
        {
            KundeId = kunde.Id,
            Typ = b.Typ ?? AktivitaetsTyp.Notiz,
            Richtung = b.Richtung,
            Zeitpunkt = n.Datum?.ToUniversalTime() ?? DateTime.UtcNow,
            Betreff = Kuerzen(betreff, 300)!,
            Inhalt = inhalt,
            Quelle = AktivitaetsQuelle.OdooImport,
            OdooId = n.Id,
            Bezug = Kuerzen(quelle.Bezug, 300),
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

    /// <summary>Verdichtete Meilensteine: kurzer deutscher Betreff, Feldänderungen als Inhalt.</summary>
    private static (string Betreff, string? Inhalt) Verdichtet(ChatterNachricht n, ChatterBewertung b)
    {
        string? Aenderung(string feldAnfang) =>
            n.Aenderungen.FirstOrDefault(a => a.Feld.StartsWith(feldAnfang, StringComparison.OrdinalIgnoreCase)) is { } a
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
            .Select(a => $"{a.Feld}: {a.Alt} → {a.Neu}")
            .ToList();
        var inhalt = zeilen.Count > 0 ? string.Join("\n", zeilen) : null;
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

    private async Task AnhaengeUebernehmenAsync(ErpDbContext db, Aktivitaet eintrag, List<int> anhangIds,
        Kunde kunde, string ablage, HashSet<int> vorhandeneAnhaenge)
    {
        if (anhangIds.Count == 0)
            return;
        var relativerOrdner = Path.Combine(SichererName(kunde.Kundennummer), "Odoo-Chatter");
        Directory.CreateDirectory(Path.Combine(ablage, relativerOrdner));

        foreach (var anhangId in anhangIds)
        {
            if (vorhandeneAnhaenge.Contains(anhangId))
                continue;
            JsonElement daten;
            try
            {
                var liste = await odoo.AusfuehrenAsync("ir.attachment", "read",
                    [new[] { anhangId }],
                    new Dictionary<string, object> { ["fields"] = new[] { "name", "mimetype", "file_size", "datas" } });
                if (liste.ValueKind != JsonValueKind.Array || liste.GetArrayLength() == 0)
                    continue;
                daten = liste[0];
            }
            catch (InvalidOperationException ex)
            {
                _stat.AnhangFehler.Add($"#{anhangId}: {Kuerzen(ex.Message, 100)}");
                continue;
            }

            var name = Text(daten, "name") ?? $"anhang-{anhangId}";
            var groesse = Zahl(daten, "file_size") ?? 0;
            if (ChatterFilter.IstSignaturbild(name, Text(daten, "mimetype"), groesse))
            {
                _stat.AnhaengeUebersprungen++;
                continue;
            }
            if (groesse > MaxAnhangBytes)
            {
                _stat.AnhangFehler.Add($"{name}: {groesse / 1024 / 1024} MB — übersprungen (Grenze {MaxAnhangBytes / 1024 / 1024} MB)");
                continue;
            }
            var base64 = Text(daten, "datas");
            if (base64 is null)
                continue;

            var dateiname = $"{eintrag.OdooId}_{SichererName(name)}";
            var relativerPfad = Path.Combine(relativerOrdner, dateiname);
            var voll = Path.Combine(ablage, relativerPfad);
            var bytes = Convert.FromBase64String(base64);
            await File.WriteAllBytesAsync(voll, bytes);

            db.Aktivitaetsanhaenge.Add(new AktivitaetAnhang
            {
                AktivitaetId = eintrag.Id,
                Dateiname = Kuerzen(name, 300)!,
                MimeType = Kuerzen(Text(daten, "mimetype"), 100),
                Groesse = bytes.LongLength,
                RelativerPfad = relativerPfad,
                OdooId = anhangId,
            });
            vorhandeneAnhaenge.Add(anhangId);
            _stat.Anhaenge++;
            _stat.AnhangBytes += bytes.LongLength;
        }
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

    /// <summary>Alle Odoo-Datensätze, deren Nachrichten einem unserer Kunden gehören.</summary>
    private async Task<Dictionary<(string Modell, int Id), Quelle>> QuellenErmittelnAsync(HashSet<int> firmaIds)
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
            quellen[("res.partner", id)] = id == firma
                ? new Quelle(firma, null, null)
                : new Quelle(firma, Text(p, "type") is { } t && t != "contact" ? $"Adresse: {Text(p, "name")}" : Text(p, "name"),
                    Text(p, "type") is null or "contact" ? id : null);
        }

        int? FirmaZu(JsonElement e, string feld) =>
            Many2OneId(e, feld) is { } pid && firmaVonPartner.TryGetValue(pid, out var f) ? f : null;

        var leads = await LadeAlleAsync("crm.lead", [new object[] { "partner_id", "!=", false }],
            ["id", "name", "partner_id", "type", "active"]);
        foreach (var l in leads)
            if (FirmaZu(l, "partner_id") is { } firma)
                quellen[("crm.lead", l.GetProperty("id").GetInt32())] =
                    new Quelle(firma, $"{(Text(l, "type") == "lead" ? "Lead" : "Verkaufschance")}: {Text(l, "name")}", null,
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
                var feld = Many2OneLabel(w, "field_id") ?? "Feld";
                var alt = ErsterWert(w, "old_value_char", "old_value_text", "old_value_integer", "old_value_float", "old_value_datetime");
                var neu = ErsterWert(w, "new_value_char", "new_value_text", "new_value_integer", "new_value_float", "new_value_datetime");
                ergebnis.TryAdd(messageId, []);
                ergebnis[messageId].Add((feld, alt ?? "—", neu ?? "—"));
            }
        }
        return ergebnis;
    }

    private static string? ErsterWert(JsonElement e, params string[] felder)
    {
        foreach (var f in felder)
        {
            if (!e.TryGetProperty(f, out var w))
                continue;
            if (w.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(w.GetString()))
                return w.GetString();
            if (w.ValueKind == JsonValueKind.Number)
                return w.ToString();
        }
        return null;
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
        _aus.WriteLine($"Übernommen als Eintrag:     {_stat.Behalten}");
        _aus.WriteLine($"Verdichtet (Meilenstein):   {_stat.Verdichtet}");
        _aus.WriteLine($"Verworfen:                  {_stat.Verworfen}");
        _aus.WriteLine($"Bereits vorhanden:          {_stat.Uebersprungen}");
        _aus.WriteLine($"Anhänge{(dryRun ? " (würden geladen)" : "")}: {_stat.Anhaenge} ({_stat.AnhangBytes / 1024.0 / 1024.0:0.0} MB)" +
                       (_stat.AnhaengeUebersprungen > 0 ? $" · Signaturbilder übersprungen: {_stat.AnhaengeUebersprungen}" : ""));
        _aus.WriteLine();
        _aus.WriteLine("Nach Grund:");
        foreach (var (grund, anzahl) in _stat.Gruende.OrderByDescending(g => g.Value))
            _aus.WriteLine($"  {grund,-36} {anzahl,7}");
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

    private static string? Kuerzen(string? text, int max) =>
        text is null ? null : (text.Length <= max ? text : text[..(max - 1)] + "…");

    private class Statistik
    {
        public int Gelesen, Behalten, Verdichtet, Verworfen, Uebersprungen, Anhaenge, AnhaengeUebersprungen;
        public long AnhangBytes;
        public readonly Dictionary<string, int> Gruende = [];
        public readonly Dictionary<string, int> JeKunde = [];
        public readonly HashSet<string> UnbekannteBenutzer = [];
        public readonly List<string> AnhangFehler = [];
        public void Grund(string grund) => Gruende[grund] = Gruende.GetValueOrDefault(grund) + 1;
    }
}
