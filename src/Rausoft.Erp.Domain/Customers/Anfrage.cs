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
    /// <summary>Klick-Kennung von Microsoft Ads (Bing) — wird wie die Google-Kennungen behandelt.</summary>
    public string? Msclkid { get; set; }
    /// <summary>Gesetzt, sobald der Löschlauf die Klick-Kennungen entfernt hat.</summary>
    public DateTime? KlickKennungenGeloeschtAm { get; set; }

    // ---- Übernahme aus Odoo ----

    /// <summary>
    /// Verkaufschance in Odoo (crm.lead), aus der die Anfrage übernommen oder mit der sie
    /// verknüpft wurde. Macht die Übernahme wiederholbar; leer bei reinen Hand-Anfragen.
    /// </summary>
    public int? OdooLeadId { get; set; }
    /// <summary>Ausgang der Verkaufschance laut Odoo — nur aussagekräftig mit <see cref="OdooLeadId"/>.</summary>
    public AnfrageErgebnis Ergebnis { get; set; }
    /// <summary>Verlustgrund laut Odoo (lost_reason_id), wenn gepflegt.</summary>
    public string? VerlustGrund { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public string? Kontaktname() =>
        $"{Vorname} {Nachname}".Trim() is { Length: > 0 } name ? name : null;

    public bool HatKlickKennung() =>
        Gclid is not null || Gbraid is not null || Wbraid is not null || Msclkid is not null;
}

/// <summary>Ausgang einer Anfrage, so wie Odoo ihn an der Verkaufschance führt.</summary>
public enum AnfrageErgebnis
{
    Offen = 0,
    Gewonnen = 1,
    Verloren = 2,
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

        var (gclid, gbraid, wbraid, msclkid) = (Feld("gclid"), Feld("gbraid"), Feld("wbraid"), Feld("msclkid"));
        var medium = Feld("utm_medium");
        // Ohne utm_source bliebe nach dem Löschen der Kennung unbekannt, dass die Anzeige von Microsoft kam
        var quelle = Feld("utm_source") ?? (msclkid is not null ? "bing" : null);
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
            Quelle = Kuerzen(quelle, 100),
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
            UeberAnzeige = gclid is not null || gbraid is not null || wbraid is not null || msclkid is not null
                           || Feld("gad_source") is not null || kampagnenNr is not null
                           || (medium is not null && BezahltMedien.Contains(medium.ToLowerInvariant())),
            Gclid = Kuerzen(gclid, 500),
            Gbraid = Kuerzen(gbraid, 500),
            Wbraid = Kuerzen(wbraid, 500),
            Msclkid = Kuerzen(msclkid, 500),
        };
    }

    /// <summary>Eintrag im Verlauf der Kundenakte: sichtbar bei den Notizen, mit Verweis auf die Anfrage.</summary>
    public static Aktivitaet VerlaufsEintrag(Anfrage a, IReadOnlyDictionary<string, string> namen)
    {
        var zeilen = new List<string?>
        {
            string.Join(" · ", new[] { a.Kontaktname(), a.Email, a.Telefon }.Where(t => !string.IsNullOrWhiteSpace(t))),
            a.Firma is not null ? $"Firma laut Formular: {a.Firma}" : null,
            a.Nachricht is not null ? $"Nachricht: {a.Nachricht}" : null,
            $"Herkunft: {Text(a, namen)}",
            a.Einstiegsseite is not null ? $"Einstiegsseite: {a.Einstiegsseite}" : null,
        };
        return new Aktivitaet
        {
            KundeId = a.KundeId ?? throw new InvalidOperationException("Verlaufseintrag nur für zugeordnete Anfragen."),
            Typ = AktivitaetsTyp.Notiz,
            Richtung = AktivitaetsRichtung.Eingehend,
            Stufe = AktivitaetsStufe.Wesentlich,
            Zeitpunkt = a.EingegangenAm,
            MitarbeiterId = a.ErfasstVonId,
            Betreff = $"Website-Anfrage: {a.Formular}",
            Inhalt = string.Join('\n', zeilen.Where(z => !string.IsNullOrWhiteSpace(z))),
            VerweisTyp = AktivitaetsVerweisTyp.Anfrage,
            VerweisId = a.Id,
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
    public static string Kanal(string? quelle, string? medium, bool ueberAnzeige, string? verweis, string? website = null)
    {
        var q = quelle?.Trim().ToLowerInvariant();
        var m = medium?.Trim().ToLowerInvariant();
        var verweisHost = Host(verweis);
        // Ein Verweis von der eigenen Website (auch idprove.com → idprove.de) ist keine Herkunft
        if (verweisHost is not null && website is not null && Hauptname(verweisHost) == Hauptname(Host(website) ?? website.ToLowerInvariant()))
            verweisHost = null;

        if (ueberAnzeige)
            return string.IsNullOrEmpty(q) || q == "google" ? "Google Ads"
                : q is "bing" or "microsoft" ? "Microsoft Ads"
                : $"{Gross(q)} (bezahlt)";
        if (m is not null && MailMedien.Contains(m))
            return "Newsletter";
        if (!string.IsNullOrEmpty(q))
            return m == "organic" || (string.IsNullOrEmpty(m) && Suchmaschinen.Contains(q))
                ? $"{Gross(q)} organisch"
                : m is null or "referral" ? Gross(q) : $"{Gross(q)} ({m})";
        if (verweisHost is not null)
            return Suchmaschinen.FirstOrDefault(s => verweisHost == s + ".com" || verweisHost.StartsWith(s + ".")) is { } suche
                ? $"{Gross(suche)} organisch"
                // Link in einem Teams-Chat angeklickt — der Rechnername des Teams-Clients sagt niemandem etwas
                : verweisHost.StartsWith("teams.") || verweisHost.Contains(".teams.") ? "Link aus Microsoft Teams"
                : $"Verweis von {verweisHost}";
        return "Direkt";
    }

    public static string Kanal(Anfrage a) =>
        Kanal(a.Quelle, a.Medium, a.UeberAnzeige, a.Verweis, a.Website ?? a.LetzteSeite ?? a.Einstiegsseite);

    /// <summary>
    /// Testaufruf über den Google Tag Assistant (Vorschau des Tag Managers): Er hängt
    /// „gtm_debug=…“ an die Adressen und erscheint als Verweis von tagassistant.google.com.
    /// So prüft man die Formulare selbst — das sind keine Anfragen von Interessenten.
    /// </summary>
    public static bool IstTestAufruf(IEnumerable<string?> herkunftsWerte) =>
        herkunftsWerte.Any(w => w is not null
            && (w.Contains("tagassistant.google.com", StringComparison.OrdinalIgnoreCase)
                || w.Contains("gtm_debug=", StringComparison.OrdinalIgnoreCase)));

    /// <summary>Rechnername einer Adresse in Kleinbuchstaben, ohne „www.“; auch für „idprove.de“ ohne Schema.</summary>
    private static string? Host(string? adresse)
    {
        if (string.IsNullOrWhiteSpace(adresse))
            return null;
        var text = adresse.Trim();
        if (!Uri.TryCreate(text.Contains("://") ? text : "https://" + text, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
            return null;
        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.") ? host[4..] : host;
    }

    /// <summary>Name vor der Endung: „idprove“ für idprove.de, www.idprove.com und portal.idprove.de.</summary>
    private static string Hauptname(string host)
    {
        var teile = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return teile.Length >= 2 ? teile[^2] : host;
    }

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

/// <summary>
/// Verkaufschancen, die die Übernahme aus Odoo auslässt, weil ihre Anfrage in der
/// Kundenzentrale gelöscht wurde — sonst legte der nächste Lauf sie wieder an. Die Nummern
/// stehen als Liste in der Einstellung <see cref="EinstellungsSchluessel.AnfragenOdooUebersprungen"/>.
/// </summary>
public static class AnfragenAusschluss
{
    public static HashSet<int> Lesen(string? einstellung) =>
        (einstellung ?? "").Split([',', ';', ' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => int.TryParse(t, out var nr) && nr > 0 ? nr : (int?)null)
            .OfType<int>().ToHashSet();

    public static string Hinzufuegen(string? einstellung, int leadId) =>
        string.Join(", ", Lesen(einstellung).Append(leadId).Distinct().Order());
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
