using System.Text.Json;
using Rausoft.Erp.OdooImport;

// Modus "vertragsscan": Bestand der Vertragsdateien aus dem Ablageverzeichnis
// erfassen (braucht kein Odoo, nur DB + Dateizugriff). --dry-run = nur Bericht.
if (args.Contains("vertragsscan"))
{
    await new VertragsBestandScanner(dryRun: args.Contains("--dry-run")).AusfuehrenAsync();
    return 0;
}

// Verbindungsdaten ausschließlich aus Umgebungsvariablen (keine Secrets in Dateien):
//   ODOO_URL      z. B. https://rausoft.odoo.com oder http://odoo.intern:8069
//   ODOO_DB       Name der Odoo-Datenbank
//   ODOO_BENUTZER Login (E-Mail) des API-Benutzers — idealerweise ein eigener,
//                 nur lesend berechtigter Benutzer
//   ODOO_API_KEY  der in Odoo erzeugte API-Key (wirkt als Passwort)
var url = Umgebungsvariable("ODOO_URL");
var db = Umgebungsvariable("ODOO_DB");
var benutzer = Umgebungsvariable("ODOO_BENUTZER");
var apiKey = Umgebungsvariable("ODOO_API_KEY");

if (url is null || db is null || benutzer is null || apiKey is null)
{
    Console.Error.WriteLine("Fehlende Umgebungsvariablen. Benötigt: ODOO_URL, ODOO_DB, ODOO_BENUTZER, ODOO_API_KEY.");
    return 1;
}

var odoo = new OdooClient(url, db, benutzer, apiKey);

// Modus "import": Kontakte übernehmen (mit --dry-run nur Vorschau).
// Modus "abos": Abonnements + Lizenzen übernehmen, Laufzeiten anreichern.
// Ohne Argumente läuft die Analyse (Verbindungstest + Datenprofil).
if (args.Contains("import"))
{
    await new OdooImporter(odoo, dryRun: args.Contains("--dry-run")).AusfuehrenAsync();
    return 0;
}
if (args.Contains("abos"))
{
    await new AboImporter(odoo, dryRun: args.Contains("--dry-run")).AusfuehrenAsync();
    return 0;
}

// Schritt 1: Verbindung + Version
var version = await odoo.VersionAsync();
Console.WriteLine($"Odoo erreichbar: Version {version.GetValueOrDefault("server_version")}");

var uid = await odoo.AnmeldenAsync();
Console.WriteLine($"Angemeldet als uid {uid} ({benutzer})");

// Schritt 2: Überblick über die Kontakte (res.partner)
var firmenAnzahl = await odoo.AusfuehrenAsync("res.partner", "search_count",
    [new object[] { new object[] { "is_company", "=", true } }]);
Console.WriteLine($"Firmen (is_company=true): {firmenAnzahl.GetInt32()}");

var personenAnzahl = await odoo.AusfuehrenAsync("res.partner", "search_count",
    [new object[] { new object[] { "is_company", "=", false } }]);
Console.WriteLine($"Personen/Kontakte:       {personenAnzahl.GetInt32()}");

// Schritt 2b: Wie erkennen wir echte Kunden? Ränge + Abos + Adressstruktur
async Task<int> Zaehlen(string modell, object[] domain)
{
    try
    {
        var n = await odoo.AusfuehrenAsync(modell, "search_count", [domain]);
        return n.GetInt32();
    }
    catch (InvalidOperationException)
    {
        return -1; // Modell/Feld existiert nicht
    }
}

Console.WriteLine($"Firmen mit customer_rank>0 (je als Kunde fakturiert): " +
    await Zaehlen("res.partner", [new object[] { "is_company", "=", true }, new object[] { "customer_rank", ">", 0 }]));
Console.WriteLine($"Firmen mit supplier_rank>0 (Lieferanten):            " +
    await Zaehlen("res.partner", [new object[] { "is_company", "=", true }, new object[] { "supplier_rank", ">", 0 }]));
Console.WriteLine($"Aktive Abos (sale.subscription):                     " +
    await Zaehlen("sale.subscription", []));
Console.WriteLine($"Abo-Aufträge (sale.order mit is_subscription):       " +
    await Zaehlen("sale.order", [new object[] { "is_subscription", "=", true }]));
Console.WriteLine($"Adress-Kinder mit type=invoice:                      " +
    await Zaehlen("res.partner", [new object[] { "type", "=", "invoice" }]));
Console.WriteLine($"Personen mit Firma (parent_id gesetzt):              " +
    await Zaehlen("res.partner", [new object[] { "is_company", "=", false }, new object[] { "parent_id", "!=", false }]));

// Welche res.partner-Felder gibt es in dieser Odoo-Version überhaupt?
// (Odoo 19 hat z. B. 'title' entfernt — wichtig für die Anrede-Zuordnung.)
var felder = await odoo.AusfuehrenAsync("res.partner", "fields_get", [],
    new Dictionary<string, object> { ["attributes"] = new[] { "type" } });
var interessant = new[] { "title", "anrede", "salut", "gender", "function", "mobile", "ref", "vat", "lang", "comment", "user_id", "category", "active", "type", "customer", "child", "kunde", "nummer", "x_" };
var vorhandene = felder.EnumerateObject()
    .Where(p => interessant.Any(i => p.Name.Contains(i, StringComparison.OrdinalIgnoreCase)))
    .Select(p => $"{p.Name} ({p.Value.GetProperty("type").GetString()})")
    .OrderBy(n => n);
Console.WriteLine();
Console.WriteLine("Relevante vorhandene res.partner-Felder: " + string.Join(", ", vorhandene));

// Stichprobe Personen: welche Felder sind für Ansprechpartner gepflegt?
var personen = await odoo.AusfuehrenAsync("res.partner", "search_read",
    [new object[] { new object[] { "is_company", "=", false }, new object[] { "parent_id", "!=", false }, new object[] { "function", "!=", false } }],
    new Dictionary<string, object>
    {
        ["fields"] = new[] { "id", "name", "x_studio_anrede", "x_title_custom", "function", "email", "phone", "parent_id", "type" },
        ["limit"] = 5,
        ["order"] = "id desc",
    });
Console.WriteLine();
Console.WriteLine("Stichprobe Ansprechpartner (mit Funktion, neueste zuerst):");
Console.WriteLine(JsonSerializer.Serialize(personen, new JsonSerializerOptions { WriteIndented = true }));

// Schritt 3: Stichprobe, um die tatsächlich gepflegten Felder zu sehen
var stichprobe = await odoo.AusfuehrenAsync("res.partner", "search_read",
    [new object[] { new object[] { "is_company", "=", true } }],
    new Dictionary<string, object>
    {
        ["fields"] = new[] { "id", "name", "ref", "l10n_de_datev_identifier_customer", "vat", "email", "phone", "street", "zip", "city", "country_id", "category_id", "user_id", "lang", "active" },
        ["limit"] = 5,
        ["order"] = "id",
    });

Console.WriteLine();
Console.WriteLine("Stichprobe (erste 5 Firmen):");
Console.WriteLine(JsonSerializer.Serialize(stichprobe, new JsonSerializerOptions { WriteIndented = true }));

// Anrede-Werteliste (Custom-Feld x_studio_anrede, many2one)
var anreden = await odoo.AusfuehrenAsync("res.partner", "formatted_read_group",
    [
        new object[] { },                    // domain: alle (auch leer, um den Anteil ohne Anrede zu sehen)
        new object[] { "x_studio_anrede" },  // groupby
        new object[] { "__count" },          // aggregates
    ]);
Console.WriteLine();
Console.WriteLine("Anrede-Verteilung (x_studio_anrede):");
foreach (var gruppe in anreden.EnumerateArray())
{
    var wert = gruppe.GetProperty("x_studio_anrede");
    var anzeige = wert.ValueKind == JsonValueKind.Array ? wert[1].GetString() : "(leer)";
    Console.WriteLine($"  {anzeige}: {gruppe.GetProperty("__count").GetInt32()}");
}

// Namensstruktur der Personen: nur Nachname oder "Vorname Nachname"?
Console.WriteLine();
Console.WriteLine($"Personen mit Leerzeichen im Namen (vermutlich Vor+Nachname): " +
    await Zaehlen("res.partner", [new object[] { "is_company", "=", false }, new object[] { "name", "like", "% %" }]));
Console.WriteLine($"Archivierte Firmen (active=false):  " +
    await Zaehlen("res.partner", [new object[] { "is_company", "=", true }, new object[] { "active", "=", false }]));
Console.WriteLine($"Firmen mit x_title_custom gesetzt:  " +
    await Zaehlen("res.partner", [new object[] { "x_title_custom", "!=", false }]));

var titel = await odoo.AusfuehrenAsync("res.partner", "formatted_read_group",
    [
        new object[] { new object[] { "x_title_custom", "!=", false } },
        new object[] { "x_title_custom" },
        new object[] { "__count" },
    ]);
Console.WriteLine();
Console.WriteLine("Werte in x_title_custom:");
foreach (var gruppe in titel.EnumerateArray())
{
    var wert = gruppe.GetProperty("x_title_custom");
    var anzeige = wert.ValueKind == JsonValueKind.Array ? wert[1].GetString() : wert.ToString();
    Console.WriteLine($"  {anzeige}: {gruppe.GetProperty("__count").GetInt32()}");
}

// Profil der rausoft-Studio-Felder: Füllgrade
Console.WriteLine();
Console.WriteLine("Füllgrade der Studio-Felder (Anzahl != leer):");
string[] studioFelder =
[
    "x_studio_kundenr", "x_studio_vorname", "x_studio_nachname", "x_studio_titel",
    "x_studio_name_komplett", "x_studio_hosting", "x_studio_vertriebspartner",
    "x_studio_e_mail_rechnungen", "x_studio_hauptansprechpartner",
    "x_studio_technischer_ansprechpartner", "x_studio_lizenzen", "x_studio_wartung",
    "x_studio_sanktionslisten", "x_studio_blocked", "x_studio_firmenname",
];
foreach (var feld in studioFelder)
{
    Console.WriteLine($"  {feld}: {await Zaehlen("res.partner", [new object[] { feld, "!=", false }])}");
}

// Werteverteilungen für Auswahlfelder
foreach (var feld in new[] { "x_studio_hosting", "x_studio_vertriebspartner" })
{
    var gruppen = await odoo.AusfuehrenAsync("res.partner", "formatted_read_group",
        [
            new object[] { new object[] { feld, "!=", false } },
            new object[] { feld },
            new object[] { "__count" },
        ]);
    Console.WriteLine();
    Console.WriteLine($"Werte in {feld}:");
    foreach (var gruppe in gruppen.EnumerateArray())
    {
        var wert = gruppe.GetProperty(feld);
        var anzeige = wert.ValueKind == JsonValueKind.Array ? wert[1].GetString() : wert.ToString();
        Console.WriteLine($"  {anzeige}: {gruppe.GetProperty("__count").GetInt32()}");
    }
}

// Stichprobe Kundennummern-Format
var kundenNummern = await odoo.AusfuehrenAsync("res.partner", "search_read",
    [new object[] { new object[] { "x_studio_kundenr", "!=", false } }],
    new Dictionary<string, object>
    {
        ["fields"] = new[] { "id", "name", "x_studio_kundenr" },
        ["limit"] = 10,
        ["order"] = "id",
    });
Console.WriteLine();
Console.WriteLine("Stichprobe KundenNr:");
foreach (var k in kundenNummern.EnumerateArray())
{
    Console.WriteLine($"  {k.GetProperty("x_studio_kundenr")} — {k.GetProperty("name")}");
}

Console.WriteLine();
Console.WriteLine("Verbindungstest erfolgreich. Nächster Schritt: Feld-Zuordnung festlegen, dann Import.");
return 0;

static string? Umgebungsvariable(string name)
{
    var wert = Environment.GetEnvironmentVariable(name);
    return string.IsNullOrWhiteSpace(wert) ? null : wert.Trim();
}
