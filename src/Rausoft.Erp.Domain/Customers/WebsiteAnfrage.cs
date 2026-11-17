using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Rausoft.Erp.Domain.Customers;

/// <summary>Ein Herkunftsfeld der Anfrage (gclid, utm_source …), so wie die Website es mitschickt.</summary>
public record WebsiteAnfrageFeld(string Name, string Wert);

/// <summary>
/// Eine Anfrage aus einem Website-Formular, ausgelesen aus dem Text der
/// Benachrichtigungsmail (Zeilen „Feld: Wert").
/// </summary>
public record WebsiteAnfrage
{
    /// <summary>Art des Formulars, z. B. „Infomappe".</summary>
    public string Formular { get; init; } = WebsiteAnfrageLeser.FormularUnbekannt;
    /// <summary>Website, auf der das Formular abgeschickt wurde (z. B. „idprove.de").</summary>
    public string? Website { get; init; }
    public string? Firma { get; init; }
    public string? Name { get; init; }
    public string? Vorname { get; init; }
    public string? Nachname { get; init; }
    public string? EMail { get; init; }
    /// <summary>Domäne der E-Mail-Adresse in Kleinbuchstaben — die zuverlässigste Spur zu einem vorhandenen Kunden.</summary>
    public string? EMailDomaene { get; init; }
    public string? Telefon { get; init; }
    public string? Nachricht { get; init; }
    public bool DatenschutzBestaetigt { get; init; }
    /// <summary>Zeitpunkt des Absendens laut Mail (Ortszeit der Website).</summary>
    public DateTime? Eingang { get; init; }
    /// <summary>Herkunftsfelder mit Inhalt, in der Reihenfolge der Mail.</summary>
    public IReadOnlyList<WebsiteAnfrageFeld> Herkunft { get; init; } = [];
    /// <summary>Auffälligkeiten, die der Mitarbeiter vor dem Anlegen sehen muss (Freemailer, fehlende Angaben …).</summary>
    public IReadOnlyList<string> Hinweise { get; init; } = [];
}

/// <summary>
/// Liest den eingefügten Text einer Website-Formular-Mail aus. Das Formular ist
/// öffentlich, jeder kann beliebige Werte eintragen — der Text gilt deshalb als
/// nicht vertrauenswürdig: feste Obergrenzen für Gesamtlänge, Zeilenzahl und
/// Feldlänge, Steuerzeichen werden entfernt, übernommen werden nur bekannte Felder.
/// Technische Angaben zum Absender (IP-Adresse, Browser) werden bewusst nicht
/// übernommen; der Vertrieb braucht sie nicht.
/// </summary>
public static partial class WebsiteAnfrageLeser
{
    public const string FormularUnbekannt = "Website-Anfrage";
    public const int MaxTextLaenge = 20_000;
    public const int MaxZeilen = 300;
    public const int MaxFeldLaenge = 500;

    /// <summary>Herkunftsfelder, die das Formular kennt; dazu zählt jedes weitere Feld im Stil „wort_wort".</summary>
    private static readonly HashSet<string> HerkunftsFelder = new(StringComparer.OrdinalIgnoreCase)
    {
        "gclid", "gbraid", "wbraid", "fbclid", "msclkid", "userId",
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content",
        "gad_source", "gad_campaignid", "doc_referrer", "landing_page",
        "first_touch_at", "last_page_before_form", "user_id",
    };

    /// <summary>
    /// Anbieter kostenloser Postfächer. Eine solche Adresse sagt nichts über die Firma —
    /// die Mail der Website verlangt ausdrücklich, das vor dem Versand zu prüfen.
    /// </summary>
    private static readonly HashSet<string> Freemailer = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "googlemail.com", "gmx.de", "gmx.net", "gmx.at", "gmx.ch", "gmx.com", "web.de",
        "t-online.de", "outlook.com", "outlook.de", "hotmail.com", "hotmail.de", "live.com", "live.de",
        "msn.com", "yahoo.com", "yahoo.de", "icloud.com", "me.com", "mac.com", "aol.com", "aol.de",
        "freenet.de", "posteo.de", "posteo.net", "mailbox.org", "proton.me", "protonmail.com",
        "mail.de", "email.de", "arcor.de", "online.de", "onlinehome.de", "vodafone.de", "o2online.de",
        "kabelmail.de", "unitybox.de", "bluewin.ch", "gmx.info", "yandex.com", "mail.ru", "qq.com",
    };

    [GeneratedRegex(@"^\s*(?<feld>[\p{L}\d][\p{L}\d _.\-]{0,39}?)\s*:(?:\s+(?<wert>.*)|\s*)$")]
    private static partial Regex FeldZeile();

    [GeneratedRegex(@"\bauf\s+(?<seite>(?:[a-z0-9-]+\.)+[a-z]{2,})\b", RegexOptions.IgnoreCase)]
    private static partial Regex WebsiteImText();

    [GeneratedRegex(@"[\p{Cc}\p{Cf}]")]
    private static partial Regex Steuerzeichen();

    public static bool IstFreemailer(string? domaene) =>
        !string.IsNullOrWhiteSpace(domaene) && Freemailer.Contains(domaene.Trim());

    /// <summary>
    /// Liest den Text aus. Liefert <c>null</c> mit Fehlertext, wenn er zu groß ist oder
    /// weder Firma noch Name noch E-Mail enthält (dann ist es keine Formular-Mail).
    /// </summary>
    public static (WebsiteAnfrage? Anfrage, string? Fehler) Lesen(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, "Bitte den Text der Mail einfügen.");
        if (text.Length > MaxTextLaenge)
            return (null, $"Der Text ist zu lang (mehr als {MaxTextLaenge:N0} Zeichen). Bitte nur die Mail einfügen.");
        var zeilen = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (zeilen.Length > MaxZeilen)
            return (null, $"Der Text hat zu viele Zeilen (mehr als {MaxZeilen}). Bitte nur die Mail einfügen.");

        var felder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var herkunft = new List<WebsiteAnfrageFeld>();
        var hinweise = new List<string>();
        var freitext = new List<string>();
        var gekuerzt = false;

        foreach (var roh in zeilen)
        {
            var zeile = Steuerzeichen().Replace(roh.Replace('\t', ' '), string.Empty).Trim();
            if (zeile.Length == 0)
                continue;
            var treffer = FeldZeile().Match(zeile);
            if (!treffer.Success)
            {
                freitext.Add(zeile);
                continue;
            }
            var feld = treffer.Groups["feld"].Value.Trim();
            var wert = treffer.Groups["wert"].Value.Trim();
            if (wert.Length > MaxFeldLaenge)
            {
                wert = wert[..MaxFeldLaenge];
                gekuerzt = true;
            }
            if (IstHerkunftsFeld(feld))
            {
                if (wert.Length > 0 && !herkunft.Any(h => h.Name.Equals(feld, StringComparison.OrdinalIgnoreCase)))
                    herkunft.Add(new(feld, wert));
            }
            else
            {
                felder.TryAdd(feld, wert); // bei doppelten Feldern gilt das erste
            }
        }

        string? Wert(params string[] namen) =>
            namen.Select(n => felder.GetValueOrDefault(n)).FirstOrDefault(w => !string.IsNullOrWhiteSpace(w));

        var firma = Wert("Firma", "Unternehmen", "Firmenname");
        var name = Wert("Name", "Vor- und Nachname", "Ansprechpartner");
        var vorname = Wert("Vorname");
        var nachname = Wert("Nachname");
        var email = Wert("Firmen E-Mail", "E-Mail", "Email", "E-Mail-Adresse", "Mail");
        if (firma is null && name is null && nachname is null && email is null)
            return (null, "Im Text wurden weder Firma noch Name noch E-Mail gefunden. " +
                          "Erwartet werden Zeilen wie „Firma: …“ und „Name: …“.");

        if (name is null && (vorname is not null || nachname is not null))
            name = $"{vorname} {nachname}".Trim();
        else if (name is not null && vorname is null && nachname is null)
            (vorname, nachname) = NameTeilen(name);

        string? domaene = null;
        if (email is not null)
        {
            if (MailAddress.TryCreate(email, out var adresse) && adresse.Address == email && adresse.Host.Contains('.'))
                domaene = adresse.Host.ToLowerInvariant();
            else
                hinweise.Add($"Die E-Mail-Adresse „{email}“ sieht nicht gültig aus.");
        }
        if (IstFreemailer(domaene))
            hinweise.Add($"Freemailer-Adresse ({domaene}) — die Firma lässt sich darüber nicht prüfen.");
        if (firma is null)
            hinweise.Add("Es ist keine Firma angegeben.");
        if (email is null)
            hinweise.Add("Es ist keine E-Mail-Adresse angegeben.");
        if (name is null)
            hinweise.Add("Es ist kein Name angegeben.");
        var datenschutz = Wert("Datenschutzerklärung", "Datenschutz") is { } d
                          && (d.Equals("on", StringComparison.OrdinalIgnoreCase)
                              || d.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
                              || d.Equals("true", StringComparison.OrdinalIgnoreCase));
        if (!datenschutz)
            hinweise.Add("Die Zustimmung zur Datenschutzerklärung fehlt im Text.");
        if (gekuerzt)
            hinweise.Add($"Mindestens ein Feld war länger als {MaxFeldLaenge} Zeichen und wurde gekürzt.");

        var betreff = Wert("Betreff");
        var einleitung = freitext.FirstOrDefault();
        return (new WebsiteAnfrage
        {
            Formular = FormularVon(betreff, einleitung),
            Website = einleitung is not null && WebsiteImText().Match(einleitung) is { Success: true } w
                ? w.Groups["seite"].Value.ToLowerInvariant()
                : null,
            Firma = firma,
            Name = name,
            Vorname = vorname,
            Nachname = nachname,
            EMail = email,
            EMailDomaene = domaene,
            Telefon = Wert("Telefon", "Telefonnummer", "Tel", "Mobil"),
            Nachricht = Wert("Nachricht", "Ihre Nachricht", "Bemerkung", "Anmerkung", "Kommentar"),
            DatenschutzBestaetigt = datenschutz,
            Eingang = EingangVon(Wert("Datum"), Wert("Zeit", "Uhrzeit")),
            Herkunft = herkunft,
            Hinweise = hinweise,
        }, null);
    }

    private static bool IstHerkunftsFeld(string feld) =>
        HerkunftsFelder.Contains(feld)
        || (feld.Contains('_') && feld.All(c => c == '_' || char.IsAsciiLetterOrDigit(c)));

    /// <summary>„Jens Köpping" → Vorname „Jens", Nachname „Köpping"; ein einzelnes Wort gilt als Nachname.</summary>
    private static (string? Vorname, string? Nachname) NameTeilen(string name)
    {
        var teile = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return teile.Length switch
        {
            0 => (null, null),
            1 => (null, teile[0]),
            _ => (string.Join(' ', teile[..^1]), teile[^1]),
        };
    }

    /// <summary>Art des Formulars aus Betreff oder Einleitung; bekannte Formulare bekommen einen festen Namen.</summary>
    private static string FormularVon(string? betreff, string? einleitung)
    {
        var quelle = $"{betreff} {einleitung}";
        (string Stichwort, string Name)[] bekannte =
        [
            ("Infomappe", "Infomappe"), ("Demo", "Demo"), ("Präsentation", "Präsentation"),
            ("Testzugang", "Testzugang"), ("Rückruf", "Rückruf"), ("Angebot", "Angebot"),
            ("Kontakt", "Kontakt"), ("Newsletter", "Newsletter"),
        ];
        foreach (var (stichwort, formular) in bekannte)
            if (quelle.Contains(stichwort, StringComparison.OrdinalIgnoreCase))
                return formular;
        return FormularUnbekannt;
    }

    private static DateTime? EingangVon(string? datum, string? zeit)
    {
        if (datum is null)
            return null;
        var kultur = CultureInfo.GetCultureInfo("de-DE");
        string[] datumsFormate = ["d. MMMM yyyy", "d.M.yyyy", "dd.MM.yyyy", "yyyy-MM-dd"];
        if (!DateTime.TryParseExact(datum, datumsFormate, kultur, DateTimeStyles.None, out var tag))
            return null;
        return zeit is not null && TimeSpan.TryParseExact(zeit, [@"h\:mm", @"hh\:mm", @"h\:mm\:ss"], kultur, out var uhr)
            ? tag + uhr
            : tag;
    }
}

/// <summary>
/// Vergleich von Firmennamen für die Dublettenprüfung: Rechtsformen, Satzzeichen und
/// Groß-/Kleinschreibung zählen nicht („PROMOS Consult GmbH" = „Promos-Consult").
/// </summary>
public static partial class Firmenname
{
    private static readonly HashSet<string> Rechtsformen = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmbh", "mbh", "ag", "kg", "kgaa", "co", "ohg", "gbr", "ug", "se", "ek", "eg", "ev", "kdoer",
        "ltd", "limited", "inc", "llc", "corp", "sa", "sarl", "srl", "bv", "nv", "spa", "plc",
        "und", "haftungsbeschraenkt", "holding", "gruppe", "group",
    };

    [GeneratedRegex(@"[^\p{L}\d]+")]
    private static partial Regex Trenner();

    /// <summary>Kernbestandteile des Namens in Kleinbuchstaben, ohne Rechtsform und Füllwörter.</summary>
    public static IReadOnlyList<string> Kern(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return [];
        var klein = name.ToLowerInvariant()
            .Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss")
            .Replace("e.k.", "ek").Replace("e.v.", "ev").Replace("e.g.", "eg");
        return Trenner().Split(klein)
            .Where(t => t.Length > 0 && !Rechtsformen.Contains(t))
            .ToList();
    }

    /// <summary>Vergleichsschlüssel: die Kernbestandteile ohne Trenner („promosconsult").</summary>
    public static string Schluessel(string? name) => string.Concat(Kern(name));

    /// <summary>
    /// Ähnlichkeit zweier Firmennamen: 100 = gleicher Kern, 80 = einer beginnt mit dem
    /// anderen (mindestens 5 Zeichen), 60 = das erste Kernwort stimmt überein
    /// (mindestens 4 Zeichen), sonst 0.
    /// </summary>
    public static int Aehnlichkeit(string? a, string? b)
    {
        var (ka, kb) = (Kern(a), Kern(b));
        if (ka.Count == 0 || kb.Count == 0)
            return 0;
        var (sa, sb) = (string.Concat(ka), string.Concat(kb));
        if (sa == sb)
            return 100;
        var (kurz, lang) = sa.Length <= sb.Length ? (sa, sb) : (sb, sa);
        if (kurz.Length >= 5 && lang.StartsWith(kurz, StringComparison.Ordinal))
            return 80;
        return ka[0].Length >= 4 && ka[0] == kb[0] ? 60 : 0;
    }
}
