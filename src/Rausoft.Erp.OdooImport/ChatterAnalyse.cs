using System.Text;
using System.Text.Json;
using static Rausoft.Erp.OdooImport.OdooJson;

namespace Rausoft.Erp.OdooImport;

/// <summary>
/// Analyse des Odoo-Chatters (mail.message) als Vorstufe zum Timeline-Import.
///
/// Ohne Kunde: Gesamtüberblick — wie viele Nachrichten je Modell, Art, Untertyp
/// und Jahr vorliegen, damit die Filterregeln an echten Zahlen festgelegt werden.
///
/// Mit Kunde (<c>--kunde</c> KundenNr, Odoo-ID oder Namensteil): alle Nachrichten
/// an der Firma, ihren Personen/Adressen und ihren CRM-Leads chronologisch mit
/// der Entscheidung des <see cref="ChatterFilter"/> — zum Bewerten der Regeln.
/// Liest nur; es wird nichts gespeichert.
/// </summary>
public class ChatterAnalyse(OdooClient odoo, string? kunde, string? ausgabeDatei)
{
    private const int BatchGroesse = 2000;
    private readonly StringBuilder _bericht = new();
    private Dictionary<int, (string Name, string? XmlId, bool Intern)> _subtypes = [];
    private HashSet<int> _internePartner = [];
    private int? _odooBot;

    public async Task AusfuehrenAsync()
    {
        Zeile("=== ODOO-CHATTER-ANALYSE (nur lesen) ===");
        Zeile($"Stand: {DateTime.Now:dd.MM.yyyy HH:mm}");
        Zeile();

        // Anmeldung vorab, damit ein Zugangsfehler klar gemeldet wird und nicht
        // in den tolerierten Lesefehlern der Stammdaten untergeht.
        await odoo.AnmeldenAsync();
        await LadeStammdatenAsync();

        if (kunde is null)
            await GesamtueberblickAsync();
        else
            await KundenansichtAsync(kunde);

        if (ausgabeDatei is not null)
        {
            await File.WriteAllTextAsync(ausgabeDatei, _bericht.ToString(), Encoding.UTF8);
            Console.WriteLine();
            Console.WriteLine($"Bericht geschrieben: {Path.GetFullPath(ausgabeDatei)}");
        }
    }

    // --- Gesamtüberblick ------------------------------------------------------

    private async Task GesamtueberblickAsync()
    {
        var gesamt = await odoo.AusfuehrenAsync("mail.message", "search_count", [new object[] { }]);
        Zeile($"Nachrichten gesamt (mail.message): {gesamt.GetInt32():N0}");
        Zeile();

        await GruppeAusgebenAsync("Nach Modell (Top 20)", ["model"], 20);
        await GruppeAusgebenAsync("Nach Nachrichtenart", ["message_type"]);
        await GruppeAusgebenAsync("Kontakte (res.partner) nach Art", ["message_type"], 0,
            new object[] { "model", "=", "res.partner" });
        await GruppeAusgebenAsync("CRM (crm.lead) nach Art", ["message_type"], 0,
            new object[] { "model", "=", "crm.lead" });
        await GruppeAusgebenAsync("Nach Untertyp (Top 30)", ["subtype_id"], 30);
        await GruppeAusgebenAsync("Nach Jahr", ["date:year"]);
        await GruppeAusgebenAsync("Autoren (Top 15)", ["author_id"], 15);

        Zeile("Lesehilfe:");
        Zeile("  comment        = manuell geschrieben (Notiz oder ‚Nachricht senden')");
        Zeile("  email          = eingegangene E-Mail; email_outgoing = per Odoo versendet");
        Zeile("  notification   = Systemmeldung/Feldänderung → wird verworfen");
        Zeile("  auto_comment   = automatische Bestätigung → wird verworfen");
        Zeile();
        Zeile("Nächster Schritt: Beispielkunden bewerten mit  chatter --kunde <KundenNr|Odoo-ID|Name>");
    }

    private async Task GruppeAusgebenAsync(string titel, string[] gruppierung, int limit = 0, params object[] domain)
    {
        Zeile($"--- {titel} ---");
        try
        {
            var kwargs = new Dictionary<string, object> { ["order"] = "__count desc" };
            if (limit > 0)
                kwargs["limit"] = limit;
            var gruppen = await odoo.AusfuehrenAsync("mail.message", "formatted_read_group",
                [domain, gruppierung, new object[] { "__count" }], kwargs);

            foreach (var gruppe in gruppen.EnumerateArray())
            {
                var wert = GruppenWert(gruppe, gruppierung[0]);
                var zusatz = gruppierung[0] == "subtype_id"
                    ? SubtypeZusatz(Many2OneId(gruppe, "subtype_id"))
                    : string.Empty;
                Zeile($"  {wert,-45} {gruppe.GetProperty("__count").GetInt32(),9:N0}{zusatz}");
            }
        }
        catch (InvalidOperationException ex)
        {
            Zeile($"  (nicht abrufbar: {Kuerzen(ex.Message, 160)})");
        }
        Zeile();
    }

    private string SubtypeZusatz(int? id) =>
        id is not null && _subtypes.TryGetValue(id.Value, out var s)
            ? $"   [{s.XmlId ?? "?"}{(s.Intern ? ", intern" : "")}]"
            : string.Empty;

    // --- Kundenansicht --------------------------------------------------------

    private async Task KundenansichtAsync(string suche)
    {
        var firma = await FirmaSuchenAsync(suche);
        if (firma is null)
        {
            Zeile($"Keine Firma zu '{suche}' gefunden (KundenNr, Odoo-ID oder Namensteil).");
            return;
        }
        var firmaId = firma.Value.GetProperty("id").GetInt32();
        Zeile($"Firma: {Text(firma.Value, "name")}  (Odoo-ID {firmaId}, KundenNr {Text(firma.Value, "x_studio_kundenr") ?? "—"})");

        // Personen und Adressen der Firma (auch archivierte)
        var personen = await LadeAlleAsync("res.partner",
            [new object[] { "parent_id", "child_of", firmaId }, new object[] { "id", "!=", firmaId }],
            ["id", "name", "type", "function", "active"]);
        var partnerNamen = new Dictionary<int, string> { [firmaId] = Text(firma.Value, "name") ?? "Firma" };
        foreach (var p in personen)
            partnerNamen[p.GetProperty("id").GetInt32()] =
                $"{Text(p, "name") ?? "?"}{(Text(p, "type") is { } t && t != "contact" ? $" [{t}]" : "")}";
        Zeile($"Personen/Adressen: {personen.Count}");
        foreach (var p in personen)
            Zeile($"  - {partnerNamen[p.GetProperty("id").GetInt32()]}{(Wahr(p, "active") ? "" : " (archiviert)")}");

        // Quellen-Beschriftung je (Modell, Id) für die Chronologie
        var quellen = partnerNamen.ToDictionary(p => ("res.partner", p.Key), p => p.Value);

        // CRM-Leads/Chancen des Kunden (auch verlorene/archivierte)
        var leads = await LadeAlleAsync("crm.lead",
            [new object[] { "partner_id", "child_of", firmaId }],
            ["id", "name", "type", "stage_id", "active", "probability", "create_date"]);
        Zeile($"CRM-Leads/Chancen: {leads.Count}");
        foreach (var l in leads)
        {
            var name = $"{Text(l, "name") ?? "?"} ({Many2OneLabel(l, "stage_id") ?? "ohne Phase"})";
            quellen[("crm.lead", l.GetProperty("id").GetInt32())] = "Lead: " + name;
            Zeile($"  - {name}{(Wahr(l, "active") ? "" : " (archiviert)")}");
        }

        // Angebote/Verkaufsaufträge des Kunden — hier läuft oft der eigentliche
        // E-Mail-Verkehr (Angebot verschickt, Rückfragen, Bestellung).
        var auftraege = await LadeAlleAsync("sale.order",
            [new object[] { "partner_id", "child_of", firmaId }],
            ["id", "name", "state", "date_order"]);
        Zeile($"Angebote/Verkaufsaufträge: {auftraege.Count}");
        foreach (var a in auftraege)
        {
            var status = Text(a, "state") switch
            {
                "draft" => "Angebot",
                "sent" => "Angebot gesendet",
                "sale" => "Verkaufsauftrag",
                "cancel" => "storniert",
                var s => s ?? "?",
            };
            var name = $"{Text(a, "name") ?? "?"} ({status})";
            quellen[("sale.order", a.GetProperty("id").GetInt32())] = "Auftrag: " + name;
            Zeile($"  - {name}");
        }

        // Ausgangsrechnungen/Gutschriften des Kunden — die Buchhaltung bleibt bei
        // DATEV, aber „gestellt" und „bezahlt" gehören als Meilensteine in die Akte.
        var rechnungen = await LadeAlleAsync("account.move",
            [new object[] { "partner_id", "child_of", firmaId },
             new object[] { "move_type", "in", new[] { "out_invoice", "out_refund" } }],
            ["id", "name", "move_type", "state", "payment_state", "invoice_date"]);
        Zeile($"Rechnungen/Gutschriften: {rechnungen.Count}");
        foreach (var r in rechnungen)
        {
            var art = Text(r, "move_type") == "out_refund" ? "Gutschrift" : "Rechnung";
            var status = Text(r, "state") switch
            {
                "draft" => "Entwurf",
                "cancel" => "storniert",
                _ => Text(r, "payment_state") switch
                {
                    "paid" or "in_payment" => "bezahlt",
                    "partial" => "teilweise bezahlt",
                    "reversed" => "storniert",
                    _ => "offen",
                },
            };
            var name = $"{Text(r, "name") ?? "?"} ({status}{(Text(r, "invoice_date") is { } d ? $", {d}" : "")})";
            quellen[("account.move", r.GetProperty("id").GetInt32())] = $"{art}: " + name;
            Zeile($"  - {art} {name}");
        }
        Zeile();

        // Alle Nachrichten an diesen Datensätzen: ODER über die Modelle,
        // je Modell UND aus model = … und res_id in (…) (Odoo-Polnische-Notation)
        var gruppen = quellen.Keys.GroupBy(k => k.Item1)
            .Select(g => (Modell: g.Key, Ids: g.Select(k => k.Item2).ToArray())).ToList();
        var domain = new List<object>();
        for (var i = 1; i < gruppen.Count; i++)
            domain.Add("|");
        foreach (var (modell, ids) in gruppen)
        {
            domain.Add("&");
            domain.Add(new object[] { "model", "=", modell });
            domain.Add(new object[] { "res_id", "in", ids });
        }
        var roh = await LadeAlleAsync("mail.message", domain.ToArray(),
            ["id", "model", "res_id", "record_name", "message_type", "subtype_id", "author_id", "email_from",
             "date", "subject", "body", "is_internal", "tracking_value_ids", "attachment_ids"],
            "date asc, id asc");

        var filter = new ChatterFilter(_internePartner, _odooBot);
        var tracking = await TrackingLadenAsync(roh
            .Where(m => AnzahlIds(m, "tracking_value_ids") > 0)
            .Select(m => m.GetProperty("id").GetInt32()));
        var nachrichten = roh.Select(m => Umwandeln(m, tracking)).ToList();
        var anhangIdsJeNachricht = roh.ToDictionary(m => m.GetProperty("id").GetInt32(), m => Ids(m, "attachment_ids"));
        var anhaenge = await AnhaengeLadenAsync(anhangIdsJeNachricht.Values.SelectMany(ids => ids).Distinct());

        Zeile($"Nachrichten gesamt: {nachrichten.Count}");
        Zeile();
        Zeile("Chronologie (Entscheidung | Art | Quelle | Autor | Inhalt):");
        Zeile();

        var zaehler = new Dictionary<(ChatterEntscheidung, string), int>();
        foreach (var n in nachrichten)
        {
            var b = filter.Bewerten(n);
            zaehler[(b.Entscheidung, b.Grund)] = zaehler.GetValueOrDefault((b.Entscheidung, b.Grund)) + 1;

            var quelle = quellen.TryGetValue((n.Modell, n.ResId), out var q) ? q : $"{n.Modell}#{n.ResId}";
            var autor = Kuerzen(n.AutorName ?? n.EmailFrom ?? "—", 28);
            if (n.AutorPartnerId is not null && _internePartner.Contains(n.AutorPartnerId.Value))
                autor += " (rausoft)";

            var markierung = b.Entscheidung switch
            {
                ChatterEntscheidung.Behalten => "[+]",
                ChatterEntscheidung.Verdichten => "[~]",
                _ => "[-]",
            };
            Zeile($"{n.Datum:dd.MM.yyyy HH:mm} {markierung} {b.Grund,-28} | {n.MessageType,-14} | {Kuerzen(quelle, 40),-40} | {autor}");

            var inhalt = n.Betreff is not null && !n.Klartext.StartsWith(n.Betreff, StringComparison.OrdinalIgnoreCase)
                ? $"{n.Betreff}: {n.Klartext}"
                : n.Klartext;
            if (inhalt.Length > 0)
                Zeile($"      {Kuerzen(inhalt.Replace("\r", "").Replace('\n', ' '), b.Entscheidung == ChatterEntscheidung.Verwerfen ? 100 : 220)}");
            foreach (var anhangId in anhangIdsJeNachricht.GetValueOrDefault(n.Id) ?? [])
                Zeile($"      Anhang: {anhaenge.GetValueOrDefault(anhangId, $"#{anhangId}")}");
            foreach (var a in n.Aenderungen)
                Zeile($"      Änderung: {a.Feld}: {Kuerzen(a.Alt, 40)} → {Kuerzen(a.Neu, 40)}");
        }

        Zeile();
        Zeile("--- Zusammenfassung ---");
        foreach (var gruppe in zaehler.GroupBy(z => z.Key.Item1).OrderBy(g => g.Key))
        {
            Zeile($"{gruppe.Key}: {gruppe.Sum(z => z.Value)}");
            foreach (var (key, anzahl) in gruppe.OrderByDescending(z => z.Value))
                Zeile($"    {key.Item2,-32} {anzahl,5}");
        }
        Zeile();
        Zeile("[+] = übernehmen als eigener Eintrag · [~] = zu Systemeintrag verdichten · [-] = nicht übernehmen");
    }

    private async Task<JsonElement?> FirmaSuchenAsync(string suche)
    {
        string[] felder = ["id", "name", "x_studio_kundenr"];
        var kwargs = new Dictionary<string, object>
        {
            ["fields"] = felder,
            ["limit"] = 5,
            ["context"] = new Dictionary<string, object> { ["active_test"] = false },
        };

        // 1) KundenNr, 2) Odoo-ID, 3) Namensteil
        var kandidaten = new List<object[]>
        {
            new object[] { new object[] { "is_company", "=", true }, new object[] { "x_studio_kundenr", "=", suche } },
        };
        if (int.TryParse(suche, out var id))
            kandidaten.Add([new object[] { "is_company", "=", true }, new object[] { "id", "=", id }]);
        kandidaten.Add([new object[] { "is_company", "=", true }, new object[] { "name", "ilike", suche }]);

        foreach (var domain in kandidaten)
        {
            var treffer = await odoo.AusfuehrenAsync("res.partner", "search_read", [domain], kwargs);
            var liste = treffer.EnumerateArray().ToList();
            if (liste.Count == 1)
                return liste[0];
            if (liste.Count > 1)
            {
                Zeile($"Mehrere Firmen passen zu '{suche}' — bitte eindeutiger (KundenNr oder Odoo-ID):");
                foreach (var t in liste)
                    Zeile($"  {t.GetProperty("id").GetInt32(),6}  {(Text(t, "x_studio_kundenr") ?? "—"),-8}  {Text(t, "name")}");
                return null;
            }
        }
        return null;
    }

    private ChatterNachricht Umwandeln(JsonElement m, Dictionary<int, List<(string Feld, string Alt, string Neu)>> tracking)
    {
        var subtypeId = Many2OneId(m, "subtype_id");
        _subtypes.TryGetValue(subtypeId ?? -1, out var subtype);
        var id = m.GetProperty("id").GetInt32();
        return new ChatterNachricht(
            Id: id,
            Modell: Text(m, "model") ?? "?",
            ResId: Zahl(m, "res_id") ?? 0,
            RecordName: Text(m, "record_name"),
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

    /// <summary>Anhänge (ir.attachment) als „Name (Typ, Größe)" je Anhang-Id — was der Import später laden würde.</summary>
    private async Task<Dictionary<int, string>> AnhaengeLadenAsync(IEnumerable<int> anhangIds)
    {
        var ergebnis = new Dictionary<int, string>();
        var ids = anhangIds.ToArray();
        if (ids.Length == 0)
            return ergebnis;
        try
        {
            var daten = await LadeAlleAsync("ir.attachment",
                [new object[] { "id", "in", ids }], ["id", "name", "mimetype", "file_size"]);
            foreach (var d in daten)
            {
                var groesse = Zahl(d, "file_size") ?? 0;
                var groesseText = groesse >= 1024 * 1024
                    ? $"{groesse / 1024.0 / 1024.0:0.0} MB"
                    : $"{Math.Max(1, groesse / 1024)} KB";
                ergebnis[d.GetProperty("id").GetInt32()] =
                    $"{Text(d, "name") ?? "?"} ({Text(d, "mimetype") ?? "?"}, {groesseText})";
            }
        }
        catch (InvalidOperationException ex)
        {
            Zeile($"(Anhänge nicht lesbar: {Kuerzen(ex.Message, 120)})");
        }
        return ergebnis;
    }

    /// <summary>Feldänderungen zu Benachrichtigungen ("Phase: Neu → Qualifiziert"), je Nachricht.</summary>
    private async Task<Dictionary<int, List<(string Feld, string Alt, string Neu)>>> TrackingLadenAsync(IEnumerable<int> nachrichtenIds)
    {
        var ergebnis = new Dictionary<int, List<(string Feld, string Alt, string Neu)>>();
        var ids = nachrichtenIds.ToArray();
        if (ids.Length == 0)
            return ergebnis;
        try
        {
            var werte = await LadeAlleAsync("mail.tracking.value",
                [new object[] { "mail_message_id", "in", ids }],
                ["mail_message_id", "field_id", "old_value_char", "new_value_char", "old_value_integer",
                 "new_value_integer", "old_value_float", "new_value_float", "old_value_datetime",
                 "new_value_datetime", "old_value_text", "new_value_text"]);
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
        catch (InvalidOperationException ex)
        {
            Zeile($"(Feldänderungen nicht lesbar: {Kuerzen(ex.Message, 120)})");
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

    // --- Stammdaten für die Bewertung --------------------------------------------

    private async Task LadeStammdatenAsync()
    {
        // Untertypen mit technischem Namen (z. B. mail.mt_note, crm.mt_lead_won)
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
            Zeile("Hinweis: ir.model.data nicht lesbar — Untertypen werden nur per Name erkannt.");
        }
        var subtypes = await LadeAlleAsync("mail.message.subtype", [], ["id", "name", "internal"]);
        _subtypes = subtypes.ToDictionary(s => s.GetProperty("id").GetInt32(),
            s => (Text(s, "name") ?? "?", xmlIds.GetValueOrDefault(s.GetProperty("id").GetInt32()), Wahr(s, "internal")));

        // Interne Autoren = Partner aller Odoo-Benutzer (auch deaktivierte)
        var benutzer = await LadeAlleAsync("res.users", [], ["id", "partner_id", "share"]);
        _internePartner = benutzer.Where(b => !Wahr(b, "share"))
            .Select(b => Many2OneId(b, "partner_id") ?? 0).Where(id => id > 0).ToHashSet();

        // OdooBot (base.partner_root)
        try
        {
            var bot = await odoo.AusfuehrenAsync("ir.model.data", "search_read",
                [new object[] { new object[] { "module", "=", "base" }, new object[] { "name", "=", "partner_root" } }],
                new Dictionary<string, object> { ["fields"] = new[] { "res_id" }, ["limit"] = 1 });
            _odooBot = bot.EnumerateArray().Select(b => Zahl(b, "res_id")).FirstOrDefault();
        }
        catch (InvalidOperationException)
        {
            // dann greift die Namenserkennung im Filter
        }

        Zeile($"Untertypen: {_subtypes.Count} · interne Benutzer: {_internePartner.Count} · OdooBot-Partner: {_odooBot?.ToString() ?? "unbekannt"}");
        Zeile();
    }

    // --- Odoo-Zugriff ---------------------------------------------------------

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

    // --- Ausgabe ----------------------------------------------------------------

    private void Zeile(string text = "")
    {
        Console.WriteLine(text);
        _bericht.AppendLine(text);
    }

    private static string Kuerzen(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";
}
