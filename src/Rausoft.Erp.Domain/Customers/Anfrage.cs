using System.Globalization;
using System.Text.RegularExpressions;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Domain.Customers;

/// <summary>
/// Gespeicherte Anfrage aus einem Website-Formular, mit ihrer Herkunft (Kanal,
/// Kampagne, Einstiegsseite). Die Kundenzentrale kennt den Weg von der Anfrage bis
/// zum Vertrag und kann deshalb auswerten, welche Werbung tatsächlich Kunden bringt —
/// in Odoo waren das nur Textfelder an der Verkaufschance.
///
/// Entscheidungen MP 17.11.2026:
/// - Die lesbaren Herkunftsfelder bleiben dauerhaft an der Anfrage.
/// - Die Klick-Kennungen (gclid, gbraid, wbraid) werden nach
///   <see cref="AnfrageHerkunft.KlickKennungTage"/> Tagen automatisch gelöscht: danach
///   lassen sie sich bei Google weder nachschlagen noch zurückmelden.
/// - Die Klick-Kennungen werden nicht mehr nach Odoo übertragen.
/// Nicht gespeichert werden der Mailtext selbst, IP-Adresse und Browser-Kennung.
/// </summary>
public class Anfrage
{
    public int Id { get; set; }

    /// <summary>Zeitpunkt des Speicherns (UTC).</summary>
    public DateTime ErfasstAm { get; set; }
    public int? ErfasstVonId { get; set; }
    public Mitarbeiter? ErfasstVon { get; set; }

    /// <summary>Zeitpunkt des Absendens auf der Website (UTC); fehlt er in der Mail, gilt <see cref="ErfasstAm"/>.</summary>
    public DateTime EingegangenAm { get; set; }

    /// <summary>Art des Formulars, z. B. „Infomappe".</summary>
    public required string Formular { get; set; }
    public string? Website { get; set; }

    public string? Firma { get; set; }
    public string? Vorname { get; set; }
    public string? Nachname { get; set; }
    public string? Email { get; set; }
    public string? Telefon { get; set; }
    public string? Nachricht { get; set; }
    public bool DatenschutzBestaetigt { get; set; }

    /// <summary>Zugeordneter Kunde; leer, solange die Firma noch nicht im Bestand ist.</summary>
    public int? KundeId { get; set; }
    public Kunde? Kunde { get; set; }

    // ---- Herkunft (dauerhaft) ----

    /// <summary>utm_source: woher der Besucher kam (google, newsletter, linkedin …).</summary>
    public string? Quelle { get; set; }
    /// <summary>utm_medium: Art des Zugangs (cpc = bezahlt, organic, email …).</summary>
    public string? Medium { get; set; }
    /// <summary>utm_campaign, wenn die Kampagne einen sprechenden Namen mitbringt.</summary>
    public string? Kampagne { get; set; }
    /// <summary>gad_campaignid: Nummer der Google-Ads-Kampagne.</summary>
    public string? KampagnenNr { get; set; }
    /// <summary>utm_term.</summary>
    public string? Suchbegriff { get; set; }
    /// <summary>utm_content.</summary>
    public string? Anzeigenvariante { get; set; }
    /// <summary>doc_referrer: Seite, von der der Besucher kam (ohne Abfrageteil).</summary>
    public string? Verweis { get; set; }
    /// <summary>landing_page: erste Seite auf der eigenen Website (ohne Abfrageteil).</summary>
    public string? Einstiegsseite { get; set; }
    /// <summary>last_page_before_form (ohne Abfrageteil).</summary>
    public string? LetzteSeite { get; set; }
    /// <summary>first_touch_at: erster Besuch in diesem Browser (UTC).</summary>
    public DateTime? ErsterBesuch { get; set; }
    /// <summary>Die Anfrage kam über eine bezahlte Anzeige — bleibt bekannt, auch wenn die Klick-Kennungen gelöscht sind.</summary>
    public bool UeberAnzeige { get; set; }

    // ---- Klick-Kennungen (befristet) ----

    public string? Gclid { get; set; }
    public string? Gbraid { get; set; }
    public string? Wbraid { get; set; }
    /// <summary>Gesetzt, sobald der Löschlauf die Klick-Kennungen entfernt hat.</summary>
    public DateTime? KlickKennungenGeloeschtAm { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public string? Kontaktname() =>
        $"{Vorname} {Nachname}".Trim() is { Length: > 0 } name ? name : null;
}

/// <summary>Regeln zur Herkunft einer Anfrage: Übernahme aus der Mail, lesbarer Kanal, Kampagnennamen.</summary>
public static partial class AnfrageHerkunft
{
    /// <summary>So lange nimmt Google eine Klick-Kennung für Nachschlagen und Rückmeldung an.</summary>
    public const int KlickKennungTage = 90;

    private static readonly string[] BezahltMedien = ["cpc", "ppc", "paidsearch", "paid_search", "googleads", "google_ads", "adwords", "paid", "paidsocial", "paid_social", "display", "banner"];
    private static readonly string[] MailMedien = ["email", "e-mail", "newsletter", "mail"];
    private static readonly string[] Suchmaschinen = ["google", "bing", "duckduckgo", "yahoo", "ecosia", "startpage", "qwant"];

    [GeneratedRegex(@"^google_ads_(?<nr>\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ErsatzKampagne();

    /// <summary>
    /// Baut die zu speichernde Anfrage aus der ausgelesenen Mail. Alle Texte werden auf
    /// die Spaltenlängen begrenzt; Adressen verlieren ihren Abfrageteil, weil darin die
    /// Klick-Kennungen noch einmal stehen und sonst das befristete Löschen umgangen würde.
    /// </summary>
    public static Anfrage Uebernehmen(WebsiteAnfrage a, DateTime jetztUtc)
    {
        string? Feld(string name) =>
            a.Herkunft.FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Wert;

        var (gclid, gbraid, wbraid) = (Feld("gclid"), Feld("gbraid"), Feld("wbraid"));
        var medium = Feld("utm_medium");
        var kampagne = Feld("utm_campaign");
        var kampagnenNr = Feld("gad_campaignid");
        // Die Website trägt bei Anzeigen ohne eigenen Namen „google_ads_<Nummer>" ein — das ist kein Name
        if (kampagne is not null && ErsatzKampagne().Match(kampagne) is { Success: true } ersatz)
        {
            kampagnenNr ??= ersatz.Groups["nr"].Value;
            kampagne = null;
        }

        return new Anfrage
        {
            ErfasstAm = jetztUtc,
            EingegangenAm = a.Eingang is { } lokal ? Ortszeit.NachUtc(lokal) : jetztUtc,
            Formular = Kuerzen(a.Formular, 100) ?? WebsiteAnfrageLeser.FormularUnbekannt,
            Website = Kuerzen(a.Website, 200),
            Firma = Kuerzen(a.Firma, 300),
            Vorname = Kuerzen(a.Vorname, 100),
            Nachname = Kuerzen(a.Nachname, 100),
            Email = Kuerzen(a.EMail, 320),
            Telefon = Kuerzen(a.Telefon, 50),
            Nachricht = Kuerzen(a.Nachricht, WebsiteAnfrageLeser.MaxFeldLaenge),
            DatenschutzBestaetigt = a.DatenschutzBestaetigt,
            Quelle = Kuerzen(Feld("utm_source"), 100),
            Medium = Kuerzen(medium, 100),
            Kampagne = Kuerzen(kampagne, 200),
            KampagnenNr = Kuerzen(kampagnenNr, 50),
            Suchbegriff = Kuerzen(Feld("utm_term"), 200),
            Anzeigenvariante = Kuerzen(Feld("utm_content"), 200),
            Verweis = UrlOhneAbfrage(Feld("doc_referrer")),
            Einstiegsseite = UrlOhneAbfrage(Feld("landing_page")),
            LetzteSeite = UrlOhneAbfrage(Feld("last_page_before_form")),
            ErsterBesuch = DateTimeOffset.TryParse(Feld("first_touch_at"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var besuch) ? besuch.UtcDateTime : null,
            UeberAnzeige = gclid is not null || gbraid is not null || wbraid is not null
                           || Feld("gad_source") is not null || kampagnenNr is not null
                           || (medium is not null && BezahltMedien.Contains(medium.ToLowerInvariant())),
            Gclid = Kuerzen(gclid, 500),
            Gbraid = Kuerzen(gbraid, 500),
            Wbraid = Kuerzen(wbraid, 500),
        };
    }

    /// <summary>Adresse ohne Abfrageteil und Sprungmarke („https://seite.de/pfad"); alles andere wird verworfen.</summary>
    public static string? UrlOhneAbfrage(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;
        return Kuerzen($"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}", 500);
    }

    /// <summary>
    /// Lesbarer Kanal: „Google Ads", „Google organisch", „Newsletter", „Verweis von seite.de", „Direkt".
    /// </summary>
    public static string Kanal(string? quelle, string? medium, bool ueberAnzeige, string? verweis)
    {
        var q = quelle?.Trim().ToLowerInvariant();
        var m = medium?.Trim().ToLowerInvariant();
        var verweisHost = Uri.TryCreate(verweis, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : null;
        if (verweisHost?.StartsWith("www.") == true)
            verweisHost = verweisHost[4..];

        if (ueberAnzeige)
            return string.IsNullOrEmpty(q) || q == "google" ? "Google Ads" : $"{Gross(q)} (bezahlt)";
        if (m is not null && MailMedien.Contains(m))
            return "Newsletter";
        if (!string.IsNullOrEmpty(q))
            return m == "organic" || (string.IsNullOrEmpty(m) && Suchmaschinen.Contains(q))
                ? $"{Gross(q)} organisch"
                : m is null or "referral" ? Gross(q) : $"{Gross(q)} ({m})";
        if (verweisHost is not null)
            return Suchmaschinen.FirstOrDefault(s => verweisHost == s + ".com" || verweisHost.StartsWith(s + ".")) is { } suche
                ? $"{Gross(suche)} organisch"
                : $"Verweis von {verweisHost}";
        return "Direkt";
    }

    public static string Kanal(Anfrage a) => Kanal(a.Quelle, a.Medium, a.UeberAnzeige, a.Verweis);

    /// <summary>
    /// Kampagnennamen aus der Einstellung „Nummer = Name; Nummer = Name" (auch zeilenweise).
    /// </summary>
    public static Dictionary<string, string> KampagnenNamen(string? einstellung)
    {
        var namen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var teil in (einstellung ?? "").Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var trenner = teil.IndexOf('=');
            if (trenner <= 0)
                continue;
            var (nr, name) = (teil[..trenner].Trim(), teil[(trenner + 1)..].Trim());
            if (nr.Length > 0 && name.Length > 0)
                namen.TryAdd(nr, name);
        }
        return namen;
    }

    /// <summary>Name der Kampagne: eigener Name aus der Mail, sonst der gepflegte Name zur Nummer, sonst „Nr. …".</summary>
    public static string? KampagnenText(Anfrage a, IReadOnlyDictionary<string, string> namen) =>
        a.Kampagne
        ?? (a.KampagnenNr is { } nr ? namen.GetValueOrDefault(nr) ?? $"Nr. {nr}" : null);

    /// <summary>Eine Zeile für Akte und Liste: „Google Ads · Kampagne Sanktionslistenprüfung".</summary>
    public static string Text(Anfrage a, IReadOnlyDictionary<string, string> namen) =>
        KampagnenText(a, namen) is { } kampagne ? $"{Kanal(a)} · Kampagne {kampagne}" : Kanal(a);

    private static string Gross(string wort) =>
        wort.Length == 0 ? wort : char.ToUpperInvariant(wort[0]) + wort[1..];

    private static string? Kuerzen(string? wert, int laenge) =>
        string.IsNullOrWhiteSpace(wert) ? null : wert.Length <= laenge ? wert : wert[..laenge];
}

/// <summary>Umrechnung deutscher Ortszeit (Angaben der Website) in UTC.</summary>
public static class Ortszeit
{
    private static readonly TimeZoneInfo Zone = Finde();

    private static TimeZoneInfo Finde()
    {
        foreach (var id in (string[])["Europe/Berlin", "W. Europe Standard Time"])
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
                return zone;
        return TimeZoneInfo.Utc;
    }

    public static DateTime NachUtc(DateTime lokal)
    {
        var unbestimmt = DateTime.SpecifyKind(lokal, DateTimeKind.Unspecified);
        // In der ausgelassenen Stunde der Zeitumstellung gibt es die Uhrzeit nicht — dann eine Stunde weiter
        if (Zone.IsInvalidTime(unbestimmt))
            unbestimmt = unbestimmt.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(unbestimmt, Zone);
    }

    public static DateTime VonUtc(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);
}
