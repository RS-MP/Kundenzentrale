using System.Net;
using System.Text.RegularExpressions;

namespace Rausoft.Erp.Domain.Shared;

/// <summary>
/// E-Mail-Textbaustein mit Betreff und formatiertem Text (HTML) — übernommen aus den
/// Odoo-Mailvorlagen (mail.template, Entscheidung MP 26.11.2026) oder hier angelegt.
/// Grundlage für „E-Mail vorbereiten" aus der Akte (ANFORDERUNGEN.md, E-Mail); bis dahin
/// lässt sich der Text formatiert in die Zwischenablage kopieren.
/// </summary>
public class Textbaustein
{
    public int Id { get; set; }

    public required string Name { get; set; }
    public string Betreff { get; set; } = "";

    /// <summary>Formatierter Text. Immer durch <see cref="OdooMailvorlage.Saeubern"/> gelaufen; angezeigt wird er nur in einem abgeschotteten Rahmen.</summary>
    public string InhaltHtml { get; set; } = "";

    /// <summary>Wofür der Baustein gedacht ist, z. B. „Angebot / Auftrag" (aus dem Odoo-Modell).</summary>
    public string? Verwendung { get; set; }

    /// <summary>Was bei der Übernahme nicht umgesetzt werden konnte (unbekannte Platzhalter, Bedingungen) — zum Nacharbeiten.</summary>
    public string? Hinweise { get; set; }

    public bool Aktiv { get; set; } = true;

    /// <summary>Odoo mail.template-Id — macht die Übernahme wiederholbar.</summary>
    public int? OdooId { get; set; }
    public string? OdooModell { get; set; }
    /// <summary>Änderungsstand in Odoo (write_date, UTC) bei der letzten Übernahme.</summary>
    public DateTime? OdooStand { get; set; }

    /// <summary>In der Kundenzentrale bearbeitet — die Übernahme überschreibt den Baustein dann nicht mehr.</summary>
    public bool LokalGeaendert { get; set; }

    public DateTime GeaendertAm { get; set; }
    public int? GeaendertVonId { get; set; }
    public Mitarbeiter? GeaendertVon { get; set; }
}

/// <summary>Ergebnis der Umsetzung einer Odoo-Mailvorlage.</summary>
public sealed record UmgesetzteVorlage(string Betreff, string Html, IReadOnlyList<string> Hinweise);

/// <summary>
/// Setzt eine Odoo-Mailvorlage in einen Textbaustein um. Die Formatierung (HTML) bleibt,
/// Odoos Platzhalter werden zu {{Platzhaltern}} der Kundenzentrale:
/// <c>&lt;t t-out="object.partner_id.name"/&gt;</c>, <c>{{ object.name }}</c> und das alte
/// <c>${object.name}</c>. Bekannte Felder bekommen einen sprechenden Namen, unbekannte bleiben
/// als <c>{{Odoo: …}}</c> sichtbar stehen und werden als Hinweis gemeldet. Bedingungen und
/// Schleifen (t-if, t-foreach) kann ein Textbaustein nicht — ihr Inhalt bleibt stehen, die
/// Bedingung wird als Hinweis genannt. Reine Textverarbeitung, ohne Odoo prüfbar.
/// </summary>
public static partial class OdooMailvorlage
{
    /// <summary>Odoo-Feld (ohne „object.") → Platzhalter der Kundenzentrale.</summary>
    private static readonly Dictionary<string, string> Bekannt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = "Beleg.Nummer",
        ["partner_id.name"] = "Kunde.Name",
        ["partner_id.commercial_partner_id.name"] = "Kunde.Firma",
        ["partner_id.email"] = "Kunde.Email",
        ["partner_invoice_id.name"] = "Kunde.Name",
        ["user_id.name"] = "Mitarbeiter.Name",
        ["user_id.signature"] = "Mitarbeiter.Signatur",
        ["user_id.email"] = "Mitarbeiter.Email",
        ["user_id.phone"] = "Mitarbeiter.Telefon",
        ["invoice_user_id.name"] = "Mitarbeiter.Name",
        ["invoice_user_id.signature"] = "Mitarbeiter.Signatur",
        ["company_id.name"] = "Firma.Name",
        ["company_id.email"] = "Firma.Email",
        ["company_id.phone"] = "Firma.Telefon",
        ["amount_total"] = "Beleg.Betrag",
        ["amount_untaxed"] = "Beleg.Netto",
        ["amount_residual"] = "Rechnung.Offen",
        ["date_order"] = "Beleg.Datum",
        ["validity_date"] = "Angebot.GueltigBis",
        ["client_order_ref"] = "Beleg.Kundenreferenz",
        ["invoice_date"] = "Rechnung.Datum",
        ["invoice_date_due"] = "Rechnung.Faellig",
        ["payment_reference"] = "Rechnung.Zahlungsreferenz",
        ["invoice_origin"] = "Rechnung.Auftrag",
    };

    private static readonly Dictionary<string, string> Verwendungen = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sale.order"] = "Angebot / Auftrag",
        ["account.move"] = "Rechnung",
        ["account.payment"] = "Zahlung",
        ["crm.lead"] = "Verkaufschance",
        ["res.partner"] = "Kontakt",
        ["res.users"] = "Benutzerkonto",
        ["project.task"] = "Projektaufgabe",
        ["project.project"] = "Projekt",
        ["helpdesk.ticket"] = "Ticket",
        ["purchase.order"] = "Einkauf",
        ["calendar.event"] = "Termin",
        ["calendar.attendee"] = "Termin",
        ["sign.request"] = "Unterschrift",
    };

    /// <summary>Lesbare Verwendung zum Odoo-Modell; unbekannte Modelle behalten ihren technischen Namen.</summary>
    public static string? Verwendung(string? modell) =>
        string.IsNullOrWhiteSpace(modell) ? null : Verwendungen.GetValueOrDefault(modell.Trim(), modell.Trim());

    public static UmgesetzteVorlage Umsetzen(string? betreff, string? bodyHtml)
    {
        var hinweise = new List<string>();
        var html = Saeubern(bodyHtml);

        // 1. Ausgabe-Elemente: <t t-out="…">Beispiel</t> und <span t-out="…">Beispiel</span>
        html = TAusgabe().Replace(html, m => WebUtility.HtmlEncode(Platzhalter(WebUtility.HtmlDecode(m.Groups["x"].Value), hinweise)));
        html = ElementAusgabe().Replace(html, m =>
            $"<{m.Groups["tag"].Value}{m.Groups["davor"].Value}{m.Groups["danach"].Value}>" +
            WebUtility.HtmlEncode(Platzhalter(WebUtility.HtmlDecode(m.Groups["x"].Value), hinweise)) +
            $"</{m.Groups["tag"].Value}>");

        // 2. Steuer-Elemente: <t t-if>, <t t-foreach>, <t t-set> — Hülle weg, Inhalt bleibt
        html = TSteuer().Replace(html, m =>
        {
            Steuerhinweis(m.Value, hinweise);
            return "";
        });
        html = html.Replace("</t>", "", StringComparison.OrdinalIgnoreCase);

        // 3. Steuer-Attribute an gewöhnlichen Elementen (t-if, t-att-href, t-attf-style …)
        html = TAttribut().Replace(html, m =>
        {
            var name = m.Groups["name"].Value.ToLowerInvariant();
            var wert = WebUtility.HtmlDecode(m.Groups["wert"].Value);
            if (name is "t-if" or "t-elif" or "t-else")
                hinweise.Add($"Bedingung entfernt — der Abschnitt steht jetzt immer im Text: {Kurz(name == "t-else" ? "sonst" : wert)}");
            else if (name == "t-foreach")
                hinweise.Add($"Wiederholung entfernt — der Abschnitt steht einmal im Text: {Kurz(wert)}");
            else if (name.StartsWith("t-att"))
                hinweise.Add($"Berechnetes Attribut entfernt ({name[(name.IndexOf('-', 2) + 1)..]}): {Kurz(wert)}");
            return "";
        });

        // 4. Platzhalter im laufenden Text und im Betreff
        html = Inline(html, hinweise, kodiert: true);
        var betreffNeu = Inline(betreff ?? "", hinweise, kodiert: false).Trim();

        return new UmgesetzteVorlage(betreffNeu, html.Trim(), hinweise.Distinct().ToList());
    }

    /// <summary>
    /// Entfernt alles Ausführbare aus fremdem HTML: Skripte, eingebettete Inhalte, Formulare,
    /// Ereignis-Attribute und javascript:-Verweise. Schutz in der Tiefe — angezeigt wird der
    /// Text zusätzlich nur in einem abgeschotteten Rahmen (iframe sandbox).
    /// </summary>
    public static string Saeubern(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";
        var sauber = html;
        string vorher;
        do
        {
            // wiederholen, bis sich nichts mehr ändert — sonst setzt „<scr<script>ipt>" sich neu zusammen
            vorher = sauber;
            sauber = Kommentar().Replace(sauber, "");
            sauber = GefaehrlicherBlock().Replace(sauber, "");
            sauber = GefaehrlichesElement().Replace(sauber, "");
            sauber = EreignisAttribut().Replace(sauber, "");
            sauber = SkriptVerweis().Replace(sauber, "${attr}=\"#\"");
        } while (sauber != vorher);
        return sauber;
    }

    /// <summary>Ein Odoo-Ausdruck als Platzhalter der Kundenzentrale.</summary>
    public static string Platzhalter(string ausdruck, List<string> hinweise)
    {
        var a = ausdruck.Trim();
        // „… or ''" ist Odoos Schutz vor leeren Feldern
        a = OderLeer().Replace(a, "").Trim();
        while (a.StartsWith('(') && a.EndsWith(')'))
            a = a[1..^1].Trim();
        // format_date(object.x), format_amount(object.x, object.currency_id), format_datetime(…)
        if (Formatierung().Match(a) is { Success: true } f)
            a = f.Groups["feld"].Value;
        if (a.StartsWith("user.", StringComparison.Ordinal))
            a = "object.user_id." + a["user.".Length..];
        if (Feldpfad().IsMatch(a) && Bekannt.TryGetValue(a["object.".Length..], out var name))
            return "{{" + name + "}}";
        hinweise.Add($"Unbekannter Platzhalter aus Odoo — bitte von Hand ersetzen: {Kurz(ausdruck.Trim())}");
        return "{{Odoo: " + Kurz(ausdruck.Trim()).Replace("{", "").Replace("}", "") + "}}";
    }

    private static string Inline(string text, List<string> hinweise, bool kodiert)
    {
        string Ersetzen(Match m)
        {
            var roh = kodiert ? WebUtility.HtmlDecode(m.Groups["x"].Value) : m.Groups["x"].Value;
            var p = Platzhalter(roh, hinweise);
            return kodiert ? WebUtility.HtmlEncode(p) : p;
        }
        text = Geschweift().Replace(text, m => m.Groups["x"].Value.TrimStart().StartsWith("Odoo:", StringComparison.Ordinal)
            || Bekannt.ContainsValue(m.Groups["x"].Value.Trim()) ? m.Value : Ersetzen(m));
        return Dollar().Replace(text, Ersetzen);
    }

    private static void Steuerhinweis(string element, List<string> hinweise)
    {
        if (Regex.Match(element, @"t-(?:if|elif)\s*=\s*""(?<w>[^""]*)""", RegexOptions.IgnoreCase) is { Success: true } wenn)
            hinweise.Add($"Bedingung entfernt — der Abschnitt steht jetzt immer im Text: {Kurz(WebUtility.HtmlDecode(wenn.Groups["w"].Value))}");
        else if (element.Contains("t-else", StringComparison.OrdinalIgnoreCase))
            hinweise.Add("Bedingung entfernt — der Abschnitt steht jetzt immer im Text: sonst");
        else if (Regex.Match(element, @"t-foreach\s*=\s*""(?<w>[^""]*)""", RegexOptions.IgnoreCase) is { Success: true } je)
            hinweise.Add($"Wiederholung entfernt — der Abschnitt steht einmal im Text: {Kurz(WebUtility.HtmlDecode(je.Groups["w"].Value))}");
        else if (Regex.Match(element, @"t-(?:set|call)\s*=\s*""(?<w>[^""]*)""", RegexOptions.IgnoreCase) is { Success: true } setzen)
            hinweise.Add($"Odoo-Anweisung entfernt: {Kurz(WebUtility.HtmlDecode(setzen.Groups["w"].Value))}");
    }

    private static string Kurz(string text) => text.Length <= 120 ? text : text[..119] + "…";

    private const RegexOptions Optionen = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant;

    [GeneratedRegex(@"<t\s[^>]*?t-(?:out|esc|raw|field)\s*=\s*""(?<x>[^""]*)""[^>]*?(?:/>|>.*?</t>)", Optionen)]
    private static partial Regex TAusgabe();

    [GeneratedRegex(@"<(?<tag>[a-z][a-z0-9]*)(?<davor>(?:\s[^>]*?)?)\st-(?:out|esc|raw|field)\s*=\s*""(?<x>[^""]*)""(?<danach>[^>]*?)(?:/>|>.*?</\k<tag>>)", Optionen)]
    private static partial Regex ElementAusgabe();

    [GeneratedRegex(@"<t(?:\s[^>]*)?/?>", Optionen)]
    private static partial Regex TSteuer();

    [GeneratedRegex(@"\s(?<name>t-(?:if|elif|else|foreach|as|set|value|att-[\w:-]+|attf-[\w:-]+|att|options[\w-]*))(?:\s*=\s*""(?<wert>[^""]*)"")?", Optionen)]
    private static partial Regex TAttribut();

    [GeneratedRegex(@"\{\{(?<x>[^{}]*)\}\}", Optionen)]
    private static partial Regex Geschweift();

    [GeneratedRegex(@"\$\{(?<x>[^{}]*)\}", Optionen)]
    private static partial Regex Dollar();

    [GeneratedRegex(@"\s+or\s+(?:''|""""|'[^']*'|""[^""]*"")\s*$", Optionen)]
    private static partial Regex OderLeer();

    [GeneratedRegex(@"^format_(?:date|datetime|amount|time)\(\s*(?<feld>object\.[\w.]+)\s*(?:,.*)?\)$", Optionen)]
    private static partial Regex Formatierung();

    [GeneratedRegex(@"^object\.[\w.]+$", Optionen)]
    private static partial Regex Feldpfad();

    [GeneratedRegex(@"<!--.*?-->", Optionen)]
    private static partial Regex Kommentar();

    [GeneratedRegex(@"<(?<tag>script|iframe|object|embed|applet|noscript)\b[^>]*>.*?</\k<tag>\s*>", Optionen)]
    private static partial Regex GefaehrlicherBlock();

    [GeneratedRegex(@"</?(?:script|iframe|object|embed|applet|form|input|button|textarea|select|option|meta|link|base|frame|frameset|noscript)\b[^>]*>", Optionen)]
    private static partial Regex GefaehrlichesElement();

    [GeneratedRegex(@"\son[a-z]+\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)", Optionen)]
    private static partial Regex EreignisAttribut();

    [GeneratedRegex(@"(?<attr>href|src|action|xlink:href)\s*=\s*(?:""\s*(?:javascript|vbscript|data\s*:\s*text/html)[^""]*""|'\s*(?:javascript|vbscript|data\s*:\s*text/html)[^']*'|(?:javascript|vbscript):[^\s>]*)", Optionen)]
    private static partial Regex SkriptVerweis();
}
