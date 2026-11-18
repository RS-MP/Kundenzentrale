using System.Globalization;
using System.Text;
using System.Text.Json;
using static Rausoft.Erp.OdooImport.OdooJson;

namespace Rausoft.Erp.OdooImport;

/// <summary>Eine Verkaufschance aus Odoo (crm.lead), auf das für die Zählung Nötige reduziert — ohne Namen und Adressen.</summary>
public record AnfrageLeadZeile(
    DateTime Angelegt,
    bool Aktiv,
    string? Formular,
    /// <summary>Technische Namen der gefüllten Herkunftsfelder.</summary>
    IReadOnlyCollection<string> GefuellteFelder,
    bool HatKontakt,
    bool HatEmail,
    /// <summary>Der Name der Verkaufschance nennt ein Website-Formular („Infomappe - …").</summary>
    bool NameWieFormular,
    string? UtmSource,
    string? UtmMedium,
    /// <summary>Bezeichnung der Odoo-Quelle (source_id).</summary>
    string? OdooQuelle);

/// <summary>
/// Zählt, was Odoo zu früheren Website-Anfragen enthält — Grundlage für die Entscheidung,
/// ob und ab wann sich die Übernahme in die Kundenzentrale lohnt (MP 18.11.2026).
/// Der Bericht nennt nur Zahlen, Feldnamen und Formular-/Kanalwerte, keine Personen.
/// </summary>
public static class AnfragenAuswertung
{
    public const string FormularFeld = "x_studio_form_source";

    public static readonly string[] KlickFelder = ["x_studio_gclid", "x_studio_gbraid", "x_studio_wbraid"];

    /// <summary>Herkunftsfelder, die der n8n-Workflow bzw. die Handpflege an der Verkaufschance füllt.</summary>
    public static readonly string[] HerkunftsFelder =
    [
        .. KlickFelder,
        "x_studio_gad_source", "x_studio_gad_campaignid",
        "x_studio_utm_source", "x_studio_utm_medium", "x_studio_utm_campaign", "x_studio_utm_term", "x_studio_utm_content",
        "x_studio_doc_referrer", "x_studio_page_url", "x_studio_landing_page",
        "x_studio_first_touch_at", "x_studio_last_page_before_form",
    ];

    /// <summary>Kanalfelder, die Odoo selbst mitbringt.</summary>
    public static readonly string[] OdooKanalFelder = ["source_id", "medium_id", "campaign_id"];

    private static readonly string[] FormularWoerter = ["Infomappe", "Testversion", "Checkliste", "Kontaktanfrage", "Kontaktformular"];

    public static bool NameNenntFormular(string? name) =>
        name is not null && FormularWoerter.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));

    public static string Bericht(
        IReadOnlyCollection<AnfrageLeadZeile> zeilen,
        IReadOnlyDictionary<string, string> vorhandeneFelder,
        IReadOnlyCollection<string> fehlendeFelder,
        IReadOnlyDictionary<string, string> weitereStudioFelder,
        DateTime heute,
        int klickKennungTage = 90)
    {
        var kultur = CultureInfo.GetCultureInfo("de-DE");
        var b = new StringBuilder();
        void Zeile(string text = "") => b.Append(text).Append('\n');
        string Zahl(int n) => n.ToString("N0", kultur);

        bool HatFormular(AnfrageLeadZeile z) => z.Formular is not null;
        bool HatHerkunft(AnfrageLeadZeile z) => z.GefuellteFelder.Any(f => HerkunftsFelder.Contains(f));
        bool HatKlick(AnfrageLeadZeile z) => z.GefuellteFelder.Any(f => KlickFelder.Contains(f));
        bool HatOdooKanal(AnfrageLeadZeile z) => z.GefuellteFelder.Any(f => OdooKanalFelder.Contains(f));
        bool NurAmNamen(AnfrageLeadZeile z) => !HatFormular(z) && !HatHerkunft(z) && z.NameWieFormular;

        Zeile("=== WEBSITE-ANFRAGEN IN ODOO (nur lesen) ===");
        Zeile($"Stand {heute.ToString("dd.MM.yyyy HH:mm", kultur)} · Verkaufschancen gesamt (auch archivierte/verlorene): {Zahl(zeilen.Count)}");
        Zeile();

        Zeile("Felder an der Verkaufschance");
        Zeile($"  vorhanden: {vorhandeneFelder.Count}");
        foreach (var (name, bezeichnung) in vorhandeneFelder.OrderBy(f => f.Key, StringComparer.Ordinal))
            Zeile($"    {name,-34} {bezeichnung}");
        if (fehlendeFelder.Count > 0)
        {
            Zeile($"  NICHT vorhanden: {fehlendeFelder.Count} (heißen in Odoo anders oder gibt es nicht)");
            foreach (var name in fehlendeFelder.OrderBy(f => f, StringComparer.Ordinal))
                Zeile($"    {name}");
        }
        if (weitereStudioFelder.Count > 0)
        {
            Zeile($"  weitere eigene Felder (x_…): {weitereStudioFelder.Count} — bitte prüfen, ob Herkunft darunter ist");
            foreach (var (name, bezeichnung) in weitereStudioFelder.OrderBy(f => f.Key, StringComparer.Ordinal))
                Zeile($"    {name,-34} {bezeichnung}");
        }
        Zeile();

        if (zeilen.Count == 0)
        {
            Zeile("Keine Verkaufschancen gefunden.");
            return b.ToString();
        }

        Zeile("Je Jahr (Anlagedatum)");
        Zeile($"  {"Jahr",-6}{"gesamt",9}{"Formular",10}{"Herkunft",10}{"Klick-K.",10}{"Odoo-Kanal",12}{"nur Name",10}{"m. Kontakt",12}");
        foreach (var jahr in zeilen.GroupBy(z => z.Angelegt.Year).OrderBy(g => g.Key))
            Zeile($"  {jahr.Key,-6}{Zahl(jahr.Count()),9}{Zahl(jahr.Count(HatFormular)),10}{Zahl(jahr.Count(HatHerkunft)),10}" +
                  $"{Zahl(jahr.Count(HatKlick)),10}{Zahl(jahr.Count(HatOdooKanal)),12}{Zahl(jahr.Count(NurAmNamen)),10}{Zahl(jahr.Count(z => z.HatKontakt)),12}");
        Zeile("  Formular   = Feld „Formular“ (x_studio_form_source) gefüllt");
        Zeile("  Herkunft   = mindestens ein Herkunftsfeld gefüllt (utm, gad, Seiten, Klick-Kennung)");
        Zeile("  Klick-K.   = gclid, gbraid oder wbraid gefüllt");
        Zeile("  Odoo-Kanal = Quelle, Medium oder Kampagne aus Odoos eigenen Feldern");
        Zeile("  nur Name   = kein Feld gefüllt, aber der Name nennt ein Formular (Infomappe, Testversion …)");
        Zeile();

        var mitFormular = zeilen.Where(HatFormular).ToList();
        var mitHerkunft = zeilen.Where(HatHerkunft).ToList();
        var nurName = zeilen.Count(NurAmNamen);
        Zeile("Zeitraum");
        Zeile(mitFormular.Count > 0
            ? $"  Formularfeld gefüllt:   {Zahl(mitFormular.Count)} · von {mitFormular.Min(z => z.Angelegt):dd.MM.yyyy} bis {mitFormular.Max(z => z.Angelegt):dd.MM.yyyy}"
            : "  Formularfeld gefüllt:   nie");
        Zeile(mitHerkunft.Count > 0
            ? $"  Herkunft gefüllt:       {Zahl(mitHerkunft.Count)} · von {mitHerkunft.Min(z => z.Angelegt):dd.MM.yyyy} bis {mitHerkunft.Max(z => z.Angelegt):dd.MM.yyyy}"
            : "  Herkunft gefüllt:       nie");
        var grenze = heute.AddDays(-klickKennungTage);
        Zeile($"  Klick-Kennung jünger als {klickKennungTage} Tage (nur diese würden übernommen): {Zahl(zeilen.Count(z => HatKlick(z) && z.Angelegt >= grenze))}");
        Zeile();

        Zeile("Füllgrad der Herkunftsfelder (Anzahl Verkaufschancen)");
        foreach (var feld in HerkunftsFelder.Concat(OdooKanalFelder).Where(vorhandeneFelder.ContainsKey))
            Zeile($"  {feld,-34} {Zahl(zeilen.Count(z => z.GefuellteFelder.Contains(feld))),8}");
        Zeile();

        Top("Formulare (Feld „Formular“)", mitFormular.Select(z => z.Formular!));
        Top("Kanal laut Website (utm_source / utm_medium)",
            zeilen.Where(z => z.UtmSource is not null || z.UtmMedium is not null)
                .Select(z => $"{z.UtmSource ?? "—"} / {z.UtmMedium ?? "—"}"));
        Top("Quelle laut Odoo (source_id)", zeilen.Where(z => z.OdooQuelle is not null).Select(z => z.OdooQuelle!));

        var kandidaten = zeilen.Count(z => HatFormular(z) || HatHerkunft(z));
        Zeile("Für die Übernahme");
        Zeile($"  sicher als Website-Anfrage erkennbar (Formular oder Herkunft): {Zahl(kandidaten)}");
        Zeile($"    davon mit Kontakt verknüpft: {Zahl(zeilen.Count(z => (HatFormular(z) || HatHerkunft(z)) && z.HatKontakt))}");
        Zeile($"    davon archiviert/verloren:   {Zahl(zeilen.Count(z => (HatFormular(z) || HatHerkunft(z)) && !z.Aktiv))}");
        Zeile($"  nur am Namen erkennbar (ohne Herkunft, unsicher):               {Zahl(nurName)}");
        return b.ToString();

        void Top(string titel, IEnumerable<string> werte)
        {
            var gruppen = werte.GroupBy(w => w.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => (Wert: g.Key, Anzahl: g.Count()))
                .OrderByDescending(g => g.Anzahl).ThenBy(g => g.Wert, StringComparer.OrdinalIgnoreCase).ToList();
            if (gruppen.Count == 0)
                return;
            Zeile(titel);
            foreach (var (wert, anzahl) in gruppen.Take(15))
                Zeile($"  {Zahl(anzahl),8}  {(wert.Length > 70 ? wert[..70] + "…" : wert)}");
            if (gruppen.Count > 15)
                Zeile($"  … und {gruppen.Count - 15} weitere Werte");
            Zeile();
        }
    }
}

/// <summary>
/// Modus „anfragen-analyse": liest alle Verkaufschancen aus Odoo (nur lesen, auch archivierte)
/// und gibt den Bericht von <see cref="AnfragenAuswertung"/> aus.
/// </summary>
public class AnfragenAnalyse(OdooClient odoo, TextWriter? ausgabe = null)
{
    private const int Seitengroesse = 500;
    private readonly TextWriter _aus = ausgabe ?? Console.Out;

    public async Task AusfuehrenAsync()
    {
        // Welche Felder gibt es überhaupt? Ein unbekanntes Feld in der Abfrage wäre ein Odoo-Fehler.
        var felder = await odoo.AusfuehrenAsync("crm.lead", "fields_get", [],
            new Dictionary<string, object> { ["attributes"] = new[] { "string", "type" } });
        var alle = felder.ValueKind == JsonValueKind.Object
            ? felder.EnumerateObject().ToDictionary(f => f.Name, f => Text(f.Value, "string") ?? f.Name)
            : [];

        string[] gesucht = [AnfragenAuswertung.FormularFeld, .. AnfragenAuswertung.HerkunftsFelder, .. AnfragenAuswertung.OdooKanalFelder];
        var vorhanden = gesucht.Where(alle.ContainsKey).ToDictionary(f => f, f => alle[f]);
        var fehlend = gesucht.Where(f => !alle.ContainsKey(f)).ToList();
        var weitere = alle.Where(f => f.Key.StartsWith("x_", StringComparison.Ordinal) && !gesucht.Contains(f.Key))
            .ToDictionary(f => f.Key, f => f.Value);

        string[] basis = ["create_date", "active", "name", "partner_id", "email_from"];
        var lesen = basis.Where(alle.ContainsKey).Concat(vorhanden.Keys).ToArray();
        var zeilen = new List<AnfrageLeadZeile>();
        for (var versatz = 0; ; versatz += Seitengroesse)
        {
            var seite = await odoo.AusfuehrenAsync("crm.lead", "search_read", [Array.Empty<object>()],
                new Dictionary<string, object>
                {
                    ["fields"] = lesen,
                    ["limit"] = Seitengroesse,
                    ["offset"] = versatz,
                    ["order"] = "id asc",
                    // verlorene Verkaufschancen sind in Odoo archiviert — sie gehören dazu
                    ["context"] = new Dictionary<string, object> { ["active_test"] = false },
                });
            var anzahl = 0;
            foreach (var lead in seite.EnumerateArray())
            {
                anzahl++;
                if (Zeitpunkt(lead, "create_date") is not { } angelegt)
                    continue;
                zeilen.Add(new AnfrageLeadZeile(
                    angelegt,
                    Wahr(lead, "active"),
                    Text(lead, AnfragenAuswertung.FormularFeld),
                    vorhanden.Keys.Where(f => f != AnfragenAuswertung.FormularFeld && Gefuellt(lead, f)).ToList(),
                    Many2OneId(lead, "partner_id") is not null,
                    Text(lead, "email_from") is not null,
                    AnfragenAuswertung.NameNenntFormular(Text(lead, "name")),
                    Text(lead, "x_studio_utm_source"),
                    Text(lead, "x_studio_utm_medium"),
                    Many2OneLabel(lead, "source_id")));
            }
            if (anzahl < Seitengroesse)
                break;
        }

        _aus.Write(AnfragenAuswertung.Bericht(zeilen, vorhanden, fehlend, weitere, DateTime.Now));
    }

    /// <summary>Odoo liefert für leere Felder <c>false</c>; Text gilt nur mit Inhalt als gefüllt.</summary>
    private static bool Gefuellt(JsonElement lead, string feld) =>
        lead.TryGetProperty(feld, out var wert) && wert.ValueKind switch
        {
            JsonValueKind.String => !string.IsNullOrWhiteSpace(wert.GetString()),
            JsonValueKind.Array => wert.GetArrayLength() > 0,
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.Object => true,
            _ => false,
        };
}
