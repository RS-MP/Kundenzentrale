using System.Text.Json;
using System.Text.RegularExpressions;
using Rausoft.Erp.OdooImport;

namespace Rausoft.Erp.Tests.OdooImport;

/// <summary>
/// Nachgestelltes Odoo im Speicher — gerade so viel, wie das Anlegen einer Website-Anfrage
/// braucht: search_read mit Odoo-Bedingungen, fields_get, create, write, message_post und
/// activity_schedule. Alle Daten sind erfunden. Dieselbe Klasse dient dem lokalen
/// Rundlauf-Test über HTTP (JSON-RPC), weil sich das echte Odoo lokal nicht ansprechen lässt.
/// </summary>
public class NachgestelltesOdoo : IOdooAufruf
{
    public record Feld(string Typ, string? Relation = null, bool NurLesen = false);

    public Dictionary<string, List<Dictionary<string, object?>>> Tabellen { get; } = [];
    public Dictionary<string, Dictionary<string, Feld>> Schema { get; } = [];
    /// <summary>Alle Aufrufe in ihrer Reihenfolge, z. B. „res.partner.create“.</summary>
    public List<string> Aufrufe { get; } = [];
    /// <summary>Liefert für einen Aufruf („modell.methode“) den Namen einer Odoo-Ausnahme, die geworfen werden soll.</summary>
    public Func<string, string?>? Fehler { get; set; }
    /// <summary>Antwort der Firmen-Anreicherung je Domäne (res.partner.enrich_by_domain); ohne Angabe „kein Treffer“.</summary>
    public Func<string, object?>? Anreicherung { get; set; }

    private readonly object _sperre = new();
    private int _naechsteNummer = 1000;

    public NachgestelltesOdoo()
    {
        Schema["res.partner"] = new()
        {
            ["name"] = new("char"), ["email"] = new("char"), ["phone"] = new("char"), ["website"] = new("char"),
            ["is_company"] = new("boolean"), ["type"] = new("selection"),
            ["parent_id"] = new("many2one", "res.partner"), ["child_ids"] = new("one2many", "res.partner"),
            ["category_id"] = new("many2many", "res.partner.category"),
            ["x_studio_vorname"] = new("char"), ["x_studio_anrede"] = new("many2one", "x_titel"),
            ["x_studio_hauptansprechpartner"] = new("many2one", "res.partner"),
            ["vat"] = new("char"), ["street"] = new("char"), ["street2"] = new("char"), ["zip"] = new("char"), ["city"] = new("char"),
            ["state_id"] = new("many2one", "res.country.state"), ["country_id"] = new("many2one", "res.country"),
            ["industry_id"] = new("many2one", "res.partner.industry"),
        };
        Schema["crm.lead"] = new()
        {
            ["name"] = new("char"), ["type"] = new("selection"), ["contact_name"] = new("char"), ["partner_name"] = new("char"),
            ["email_from"] = new("char"), ["phone"] = new("char"), ["description"] = new("html"),
            ["partner_id"] = new("many2one", "res.partner"), ["stage_id"] = new("many2one", "crm.stage"),
            ["user_id"] = new("many2one", "res.users"), ["source_id"] = new("many2one", "utm.source"),
            ["medium_id"] = new("many2one", "utm.medium"), ["tag_ids"] = new("many2many", "crm.tag"),
            ["x_studio_vorname"] = new("char"), ["x_studio_form_source"] = new("char"),
            ["x_studio_gclid"] = new("char"), ["x_studio_gbraid"] = new("char"), ["x_studio_wbraid"] = new("char"),
            ["x_studio_msclkid"] = new("char"), ["x_studio_gad_campaignid"] = new("char"),
            ["x_studio_utm_source"] = new("char"), ["x_studio_utm_medium"] = new("char"), ["x_studio_utm_campaign"] = new("char"),
            ["x_studio_utm_term"] = new("char"), ["x_studio_utm_content"] = new("char"), ["x_studio_doc_referrer"] = new("char"),
            ["x_studio_landing_page"] = new("char"), ["x_studio_first_touch_at"] = new("char"),
            ["x_studio_last_page_before_form"] = new("char"),
        };
        Schema["crm.stage"] = new() { ["name"] = new("char"), ["is_won"] = new("boolean"), ["sequence"] = new("integer") };
        Schema["crm.tag"] = new() { ["name"] = new("char") };
        Schema["res.users"] = new() { ["name"] = new("char"), ["share"] = new("boolean") };
        Schema["mail.activity.type"] = new() { ["name"] = new("char"), ["res_model"] = new("char") };
        Schema["utm.source"] = new() { ["name"] = new("char") };
        Schema["utm.medium"] = new() { ["name"] = new("char") };
        Schema["x_titel"] = new() { ["x_name"] = new("char") };
        Schema["res.partner.category"] = new() { ["name"] = new("char") };
        Schema["ir.model"] = new() { ["model"] = new("char"), ["name"] = new("char") };
        Schema["mail.activity"] = new()
        {
            ["res_model"] = new("char"), ["res_model_id"] = new("many2one", "ir.model"), ["res_id"] = new("integer"),
            ["activity_type_id"] = new("many2one", "mail.activity.type"), ["summary"] = new("char"),
            ["date_deadline"] = new("date"), ["note"] = new("html"), ["user_id"] = new("many2one", "res.users"),
        };
        Schema["mail.message"] = new() { ["model"] = new("char"), ["res_id"] = new("integer"), ["body"] = new("html") };

        // Stammdaten, wie sie eine Odoo-Datenbank mitbringt (Namen erfunden bzw. Odoo-Standard)
        Neu("crm.stage", 1, new() { ["name"] = "Testversion/Checkliste", ["sequence"] = 1 });
        Neu("crm.stage", 2, new() { ["name"] = "Infomappe", ["sequence"] = 2 });
        Neu("crm.stage", 3, new() { ["name"] = "Angebot", ["sequence"] = 3 });
        Neu("crm.stage", 4, new() { ["name"] = "Gewonnen", ["sequence"] = 4, ["is_won"] = true });
        Neu("crm.tag", 1, new() { ["name"] = "Website" });
        Neu("crm.tag", 2, new() { ["name"] = "Messe" });
        Neu("res.users", 2, new() { ["name"] = "Dana Beispiel", ["share"] = false });
        Neu("res.users", 6, new() { ["name"] = "Mika Muster", ["share"] = false });
        Neu("res.users", 9, new() { ["name"] = "Portal-Nutzer", ["share"] = true });
        Neu("mail.activity.type", 4, new() { ["name"] = "Zu erledigen" });
        Neu("mail.activity.type", 15, new() { ["name"] = "Infomappe versenden", ["res_model"] = "crm.lead" });
        Neu("mail.activity.type", 16, new() { ["name"] = "Testversion nachfassen", ["res_model"] = "crm.lead" });
        Neu("mail.activity.type", 30, new() { ["name"] = "Rechnung prüfen", ["res_model"] = "account.move" });
        Neu("utm.source", 1, new() { ["name"] = "Suchmaschine" });
        Neu("utm.source", 3, new() { ["name"] = "Newsletter" });
        Neu("utm.source", 6, new() { ["name"] = "LinkedIn" });
        Neu("utm.medium", 1, new() { ["name"] = "Website" });
        Neu("utm.medium", 4, new() { ["name"] = "E-Mail" });
        Neu("utm.medium", 10, new() { ["name"] = "Google Adwords" });
        Neu("x_titel", 1, new() { ["x_name"] = "Herr" });
        Neu("x_titel", 2, new() { ["x_name"] = "Frau" });
        Neu("res.partner.category", 2, new() { ["name"] = "Hauptansprechpartner" });
        Neu("ir.model", 624, new() { ["model"] = "crm.lead", ["name"] = "Lead/Verkaufschance" });
    }

    /// <summary>Legt einen Datensatz mit fester Nummer an (Testdaten).</summary>
    public Dictionary<string, object?> Neu(string modell, int id, Dictionary<string, object?> werte)
    {
        werte["id"] = id;
        Tabelle(modell).Add(werte);
        return werte;
    }

    public List<Dictionary<string, object?>> Tabelle(string modell) =>
        Tabellen.TryGetValue(modell, out var tabelle) ? tabelle : Tabellen[modell] = [];

    public Dictionary<string, object?> Satz(string modell, int id) => Tabelle(modell).Single(s => (int)s["id"]! == id);

    public int Anzahl(string aufruf) => Aufrufe.Count(a => a == aufruf);

    public Task<JsonElement> AusfuehrenAsync(string modell, string methode, object[] args, Dictionary<string, object>? kwargs = null) =>
        Task.FromResult(JsonSerializer.SerializeToElement(
            Ausfuehren(modell, methode, JsonSerializer.SerializeToElement(args), JsonSerializer.SerializeToElement(kwargs ?? []))));

    /// <summary>Ein Aufruf mit bereits als JSON vorliegenden Argumenten (so kommt er auch über HTTP an).</summary>
    public object? Ausfuehren(string modell, string methode, JsonElement args, JsonElement kwargs)
    {
        lock (_sperre)
        {
            var aufruf = $"{modell}.{methode}";
            Aufrufe.Add(aufruf);
            if (Fehler?.Invoke(aufruf) is { } art)
                throw new OdooFehlerException(art, "Odoo-Fehler: {\"data\":{\"name\":\"" + art + "\",\"message\":\"ROHTEXT-AUS-ODOO\"}}");
            if (!Schema.TryGetValue(modell, out var schema))
                throw new OdooFehlerException("builtins.KeyError", "Odoo-Fehler: Modell unbekannt");

            switch (methode)
            {
                case "fields_get":
                    return schema.ToDictionary(f => f.Key, f => (object)new Dictionary<string, object?>
                    {
                        ["type"] = f.Value.Typ, ["relation"] = (object?)f.Value.Relation ?? false, ["string"] = f.Key,
                        ["readonly"] = f.Value.NurLesen,
                    });
                case "search_read":
                    return Suchen(modell, schema, args.GetArrayLength() > 0 ? args[0] : default, kwargs);
                case "create":
                {
                    var werte = args[0].ValueKind == JsonValueKind.Array ? args[0][0] : args[0];
                    return Anlegen(modell, schema, werte);
                }
                case "write":
                    foreach (var id in args[0].EnumerateArray().Select(e => e.GetInt32()))
                        Setzen(modell, schema, Satz(modell, id), args[1]);
                    return true;
                case "enrich_by_domain" when modell == "res.partner":
                    return Anreicherung?.Invoke(args[0].GetString()!) ?? new Dictionary<string, object?>();
                case "message_post" when modell == "crm.lead":
                {
                    var id = _naechsteNummer++;
                    Tabelle("mail.message").Add(new()
                    {
                        ["id"] = id, ["model"] = modell, ["res_id"] = args[0][0].GetInt32(),
                        ["body"] = kwargs.GetProperty("body").GetString(),
                        ["body_is_html"] = kwargs.TryGetProperty("body_is_html", out var html) && html.ValueKind == JsonValueKind.True,
                    });
                    return new[] { id };
                }
                case "activity_schedule" when modell == "crm.lead":
                {
                    var satz = new Dictionary<string, object?> { ["res_model"] = modell, ["res_id"] = args[0][0].GetInt32() };
                    Setzen("mail.activity", Schema["mail.activity"], satz, kwargs);
                    satz["id"] = _naechsteNummer++;
                    Tabelle("mail.activity").Add(satz);
                    return new[] { (int)satz["id"]! };
                }
                default:
                    throw new OdooFehlerException("builtins.AttributeError", "Odoo-Fehler: Methode unbekannt");
            }
        }
    }

    private int Anlegen(string modell, Dictionary<string, Feld> schema, JsonElement werte)
    {
        var satz = new Dictionary<string, object?>();
        Setzen(modell, schema, satz, werte);
        if (modell == "mail.activity" && satz.GetValueOrDefault("res_model_id") is int modellId)
            satz["res_model"] = Satz("ir.model", modellId)["model"];
        satz["id"] = _naechsteNummer++;
        Tabelle(modell).Add(satz);
        return (int)satz["id"]!;
    }

    private static void Setzen(string modell, Dictionary<string, Feld> schema, Dictionary<string, object?> satz, JsonElement werte)
    {
        foreach (var w in werte.EnumerateObject())
        {
            // Wie Odoo: ein unbekanntes Feld ist ein Fehler
            if (!schema.TryGetValue(w.Name, out var feld) && w.Name != "automated")
                throw new OdooFehlerException("builtins.ValueError", $"Odoo-Fehler: Feld {w.Name} unbekannt an {modell}");
            if (feld is { NurLesen: true })
                throw new OdooFehlerException("odoo.exceptions.ValidationError", $"Odoo-Fehler: Feld {w.Name} ist nur lesbar");
            if (feld?.Typ == "many2many")
            {
                var ids = (satz.GetValueOrDefault(w.Name) as int[] ?? []).ToList();
                foreach (var befehl in w.Value.EnumerateArray())
                    switch (befehl[0].GetInt32())
                    {
                        case 6: ids = befehl[2].EnumerateArray().Select(e => e.GetInt32()).ToList(); break;
                        case 4: ids.Add(befehl[1].GetInt32()); break;
                        case 3: ids.Remove(befehl[1].GetInt32()); break;
                    }
                satz[w.Name] = ids.ToArray();
                continue;
            }
            satz[w.Name] = w.Value.ValueKind switch
            {
                JsonValueKind.String => w.Value.GetString(),
                JsonValueKind.Number => w.Value.GetInt32(),
                JsonValueKind.True => true,
                _ => null, // false/null = leer
            };
        }
    }

    // ---- Lesen ----

    private List<Dictionary<string, object?>> Suchen(string modell, Dictionary<string, Feld> schema, JsonElement bedingungen, JsonElement kwargs)
    {
        var treffer = Tabelle(modell).Where(s => Passt(modell, s, bedingungen)).ToList();
        var reihenfolge = kwargs.TryGetProperty("order", out var o) ? o.GetString() ?? "" : "";
        treffer = reihenfolge.StartsWith("name") ? treffer.OrderBy(s => s.GetValueOrDefault("name") as string).ToList()
            : reihenfolge.StartsWith("sequence") ? treffer.OrderBy(s => s.GetValueOrDefault("sequence") as int? ?? 0).ThenBy(s => (int)s["id"]!).ToList()
            : reihenfolge.Contains("desc") ? treffer.OrderByDescending(s => (int)s["id"]!).ToList()
            : treffer.OrderBy(s => (int)s["id"]!).ToList();
        if (kwargs.TryGetProperty("limit", out var limit) && limit.ValueKind == JsonValueKind.Number)
            treffer = treffer.Take(limit.GetInt32()).ToList();
        var felder = kwargs.TryGetProperty("fields", out var f)
            ? f.EnumerateArray().Select(e => e.GetString()!).ToList()
            : schema.Keys.ToList();

        return treffer.Select(s =>
        {
            var zeile = new Dictionary<string, object?> { ["id"] = s["id"] };
            foreach (var name in felder.Where(n => n != "id"))
            {
                if (name == "display_name")
                {
                    zeile[name] = Anzeigename(s);
                    continue;
                }
                if (!schema.TryGetValue(name, out var feld))
                    throw new OdooFehlerException("builtins.ValueError", $"Odoo-Fehler: Feld {name} unbekannt an {modell}");
                zeile[name] = feld.Typ switch
                {
                    "one2many" when name == "child_ids" => Kinder((int)s["id"]!).Select(k => (int)k["id"]!).ToArray(),
                    "many2many" => s.GetValueOrDefault(name) as int[] ?? [],
                    "many2one" => s.GetValueOrDefault(name) is int id
                        ? new object[] { id, Anzeigename(Satz(feld.Relation!, id)) }
                        : false,
                    "boolean" => s.GetValueOrDefault(name) is true,
                    _ => s.GetValueOrDefault(name) ?? false,
                };
            }
            return zeile;
        }).ToList();
    }

    private static string Anzeigename(Dictionary<string, object?> satz) =>
        (satz.GetValueOrDefault("name") ?? satz.GetValueOrDefault("x_name") ?? satz.GetValueOrDefault("model")) as string ?? "";

    private IEnumerable<Dictionary<string, object?>> Kinder(int id) =>
        Tabelle("res.partner").Where(k => k.GetValueOrDefault("parent_id") is int eltern && eltern == id);

    /// <summary>Odoo-Bedingungen in Präfix-Schreibweise („|“, „&amp;“, „!“); mehrere Bedingungen ohne Operator gelten gemeinsam.</summary>
    private bool Passt(string modell, Dictionary<string, object?> satz, JsonElement bedingungen)
    {
        if (bedingungen.ValueKind != JsonValueKind.Array)
            return true;
        var teile = bedingungen.EnumerateArray().ToList();
        var stelle = 0;
        var ergebnis = true;
        while (stelle < teile.Count)
            ergebnis &= Auswerten(modell, satz, teile, ref stelle);
        return ergebnis;
    }

    private bool Auswerten(string modell, Dictionary<string, object?> satz, List<JsonElement> teile, ref int stelle)
    {
        var teil = teile[stelle++];
        if (teil.ValueKind == JsonValueKind.String)
        {
            switch (teil.GetString())
            {
                case "|":
                {
                    var links = Auswerten(modell, satz, teile, ref stelle);
                    var rechts = Auswerten(modell, satz, teile, ref stelle);
                    return links || rechts;
                }
                case "&":
                {
                    var links = Auswerten(modell, satz, teile, ref stelle);
                    var rechts = Auswerten(modell, satz, teile, ref stelle);
                    return links && rechts;
                }
                default:
                    return !Auswerten(modell, satz, teile, ref stelle);
            }
        }
        var (feld, vergleich, wert) = (teil[0].GetString()!, teil[1].GetString()!, teil[2]);
        if (feld.StartsWith("child_ids."))
            return Kinder((int)satz["id"]!).Any(k => Vergleich(k.GetValueOrDefault(feld["child_ids.".Length..]), vergleich, wert));
        if (vergleich == "child_of")
        {
            var ids = wert.ValueKind == JsonValueKind.Array ? wert.EnumerateArray().Select(e => e.GetInt32()).ToHashSet() : [wert.GetInt32()];
            return satz.GetValueOrDefault(feld) is int id
                   && (ids.Contains(id) || (Satz("res.partner", id).GetValueOrDefault("parent_id") is int eltern && ids.Contains(eltern)));
        }
        return Vergleich(satz.GetValueOrDefault(feld), vergleich, wert);
    }

    private static bool Vergleich(object? ist, string vergleich, JsonElement soll)
    {
        switch (vergleich)
        {
            case "=" or "!=":
            {
                var gleich = soll.ValueKind switch
                {
                    JsonValueKind.False or JsonValueKind.Null => ist is null or false,
                    JsonValueKind.True => ist is true,
                    JsonValueKind.Number => ist is int zahl && zahl == soll.GetInt32(),
                    _ => ist is string text && text == soll.GetString(),
                };
                return gleich == (vergleich == "=");
            }
            case "in":
                return ist is int nr && soll.EnumerateArray().Any(e => e.GetInt32() == nr);
            case "=ilike":
                return ist is string genau && Regex.IsMatch(genau, "^" + Muster(soll.GetString()!) + "$", RegexOptions.IgnoreCase);
            case "ilike":
                return ist is string teil && Regex.IsMatch(teil, Muster(soll.GetString()!), RegexOptions.IgnoreCase);
            default:
                throw new OdooFehlerException("builtins.ValueError", $"Odoo-Fehler: Vergleich {vergleich} unbekannt");
        }
    }

    /// <summary>LIKE-Muster („%“, „_“, mit „\“ maskiert) als regulärer Ausdruck.</summary>
    private static string Muster(string like)
    {
        var b = new System.Text.StringBuilder();
        for (var i = 0; i < like.Length; i++)
        {
            var c = like[i];
            if (c == '\\' && i + 1 < like.Length)
                b.Append(Regex.Escape(like[++i].ToString()));
            else
                b.Append(c switch { '%' => ".*", '_' => ".", _ => Regex.Escape(c.ToString()) });
        }
        return b.ToString();
    }
}
