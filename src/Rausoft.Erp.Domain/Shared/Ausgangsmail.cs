using System.Text.RegularExpressions;
using Rausoft.Erp.Domain.Customers;

namespace Rausoft.Erp.Domain.Shared;

/// <summary>
/// Über welchen Weg eine Mail die Kundenzentrale verlässt. Die Mail selbst (Empfänger, Betreff,
/// Text, Anhänge) wird unabhängig vom Weg zusammengestellt — so kommt ein weiterer Weg dazu,
/// ohne dass sich an Bausteinen, Platzhaltern oder dem Eintrag im Verlauf etwas ändert.
/// </summary>
public enum MailKanal
{
    /// <summary>Eigenes Postfach des Mitarbeiters über Microsoft Graph — als Entwurf oder direkt gesendet.</summary>
    Postfach = 0,

    /// <summary>
    /// Vorgesehen (MP 26.11.2026): eigener SMTP-Kanal mit fester Absenderadresse, z. B. das
    /// Ticketsystem unter @idprove.de. Noch nicht gebaut — der Wert hält den Platz frei.
    /// </summary>
    Smtp = 1,
}

public enum AusgangsmailStatus
{
    /// <summary>Liegt als Entwurf im Postfach und ist noch nicht gesendet.</summary>
    Entwurf = 0,
    Gesendet = 1,
    /// <summary>Der Entwurf wurde in Outlook gelöscht, ohne gesendet zu werden.</summary>
    Verworfen = 2,
}

/// <summary>
/// Eine Kundenmail, die aus der Kundenzentrale vorbereitet wurde (ANFORDERUNGEN.md, „E-Mail aus
/// der Akte“). Sie entsteht erst, wenn die Mail im Postfach wirklich existiert. Über
/// <see cref="GraphId"/> findet die Kundenzentrale später die gesendete Fassung wieder; die
/// <see cref="NachrichtenKennung"/> (Message-ID) lässt Antworten des Kunden im selben
/// Gesprächsfaden landen. Zugriffstokens werden nie gespeichert.
/// </summary>
public class Ausgangsmail
{
    public int Id { get; set; }

    public int KundeId { get; set; }
    public Kunde? Kunde { get; set; }

    public int MitarbeiterId { get; set; }
    public Mitarbeiter? Mitarbeiter { get; set; }

    public int? AnsprechpartnerId { get; set; }

    /// <summary>Empfänger, mit „; “ getrennt.</summary>
    public string An { get; set; } = "";
    public string? Cc { get; set; }

    public string Betreff { get; set; } = "";

    /// <summary>Text der Mail, gesäubert; nach dem Senden die wirklich gesendete Fassung.</summary>
    public string InhaltHtml { get; set; } = "";

    /// <summary>Dateinamen der Anhänge, mit „; “ getrennt — die Dateien selbst liegen im Postfach.</summary>
    public string? Anhaenge { get; set; }

    public int? TextbausteinId { get; set; }

    public MailKanal Kanal { get; set; }

    /// <summary>Unveränderliche Kennung der Nachricht im Postfach (Graph, ImmutableId).</summary>
    public string? GraphId { get; set; }

    /// <summary>Internet-Message-ID der Mail.</summary>
    public string? NachrichtenKennung { get; set; }

    public AusgangsmailStatus Status { get; set; }

    public DateTime AngelegtAm { get; set; }
    public DateTime? GesendetAm { get; set; }

    /// <summary>Eintrag im Verlauf der Akte: erst „E-Mail vorbereitet“, nach dem Senden die Mail.</summary>
    public int? AktivitaetId { get; set; }
    public Aktivitaet? Aktivitaet { get; set; }
}

/// <summary>
/// Regeln für ausgehende Kundenmails — reine Textarbeit, ohne Datenbank und Postfach prüfbar:
/// Platzhalter der Textbausteine füllen, Mail aus eigenem Text, Baustein und Signatur
/// zusammensetzen, Empfänger prüfen, Testsystem-Umleitung.
/// </summary>
public static partial class MailRegeln
{
    public const int MaxEmpfaenger = 20;
    public const int MaxFreitext = 20_000;
    public const int MaxSignatur = 100_000;
    public const string SignaturPlatzhalter = "Mitarbeiter.Signatur";

    [GeneratedRegex(@"\{\{\s*(?<name>[^{}]{1,120}?)\s*\}\}")]
    private static partial Regex Platzhalter();

    [GeneratedRegex(@"^[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(\.[A-Za-z0-9\-]+)+$")]
    private static partial Regex Adresse();

    /// <summary>
    /// Ersetzt {{Platzhalter}}, für die es einen Wert gibt; alle anderen bleiben stehen (und
    /// zählen als offen). In HTML werden die Werte kodiert — nur die Signatur ist selbst HTML.
    /// </summary>
    public static string Fuellen(string? text, IReadOnlyDictionary<string, string?> werte, bool html) =>
        Platzhalter().Replace(text ?? "", m =>
        {
            var name = m.Groups["name"].Value;
            // Kein Wert (null) = offen lassen; ein leerer Wert ersetzt den Platzhalter durch nichts
            if (!werte.TryGetValue(name, out var wert) || wert is null)
                return m.Value;
            return html && !name.Equals(SignaturPlatzhalter, StringComparison.OrdinalIgnoreCase) ? Kodieren(wert) : wert;
        });

    /// <summary>
    /// Text für HTML entschärfen — nur die Zeichen, die dort eine Bedeutung haben. Umlaute bleiben
    /// lesbar (die Mail geht als UTF-8), statt als Zahlencode im Quelltext zu stehen.
    /// </summary>
    public static string Kodieren(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");

    /// <summary>Platzhalter, die noch im Text stehen — so darf keine Mail an einen Kunden gehen.</summary>
    public static List<string> Offene(params string?[] texte) =>
        texte.SelectMany(t => Platzhalter().Matches(t ?? "").Select(m => m.Groups["name"].Value.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static bool EnthaeltSignatur(string? html) =>
        Platzhalter().Matches(html ?? "").Any(m => m.Groups["name"].Value.Equals(SignaturPlatzhalter, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Mail zusammensetzen: eigener Text (Klartext, Leerzeile = neuer Absatz), darunter der
    /// Baustein, am Ende die Signatur. Setzt der Baustein die Signatur über seinen Platzhalter
    /// selbst ein (<see cref="EnthaeltSignatur"/>), gibt der Aufrufer hier keine mit.
    /// </summary>
    public static string Zusammensetzen(string? freitext, string? bausteinHtml, string? signaturHtml)
    {
        var teile = new List<string>();
        var eigen = (freitext ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (eigen.Length > 0)
            teile.AddRange(eigen.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(absatz => $"<p>{Kodieren(absatz).Replace("\n", "<br>")}</p>"));
        if (!string.IsNullOrWhiteSpace(bausteinHtml))
            teile.Add(bausteinHtml.Trim());
        if (!string.IsNullOrWhiteSpace(signaturHtml))
            teile.Add($"<div>{signaturHtml.Trim()}</div>");
        return string.Join("\n", teile);
    }

    /// <summary>Adressen aus einer Eingabe („a@x.de; b@y.de“ oder mit Komma, Leerzeichen, Zeilen).</summary>
    public static (List<string> Gueltig, List<string> Ungueltig) Adressen(string? eingabe)
    {
        var gueltig = new List<string>();
        var ungueltig = new List<string>();
        foreach (var teil in (eingabe ?? "").Split([';', ',', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var adresse = teil.Trim('<', '>').ToLowerInvariant();
            if (adresse.Length <= 254 && Adresse().IsMatch(adresse))
            {
                if (!gueltig.Contains(adresse))
                    gueltig.Add(adresse);
            }
            else
            {
                ungueltig.Add(teil);
            }
        }
        return (gueltig, ungueltig);
    }

    /// <summary>
    /// Empfänger, an die wirklich gesendet wird. Mit Umleitung (Testsystem, Probebetrieb) geht
    /// nie etwas an Kunden: alles an die Umleitungsadresse — fehlt sie, an niemanden.
    /// </summary>
    public static List<string> Empfaenger(IEnumerable<string> adressen, bool umleiten, string? umleitung)
    {
        if (!umleiten)
            return adressen.ToList();
        return adressen.Any() ? Adressen(umleitung).Gueltig.Take(1).ToList() : [];
    }

    /// <summary>Kundenmails sind nur mit einem ausdrücklichen „Ja“ freigegeben — leer, „Nein“ oder Tippfehler heißt gesperrt.</summary>
    public static bool Freigegeben(string? einstellung) =>
        string.Equals(einstellung?.Trim(), "Ja", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Was die Kundenzentrale über Kunde, Ansprechpartner und Mitarbeiter in Platzhalter
    /// einsetzen kann. Fehlt ein Wert, bleibt der Platzhalter offen. Die Signatur ist die
    /// Ausnahme: ohne Signatur verschwindet ihr Platzhalter.
    /// </summary>
    public static Dictionary<string, string?> Werte(
        string firma, string? kundennummer, string? ansprechpartner, string? ansprechpartnerEmail,
        string mitarbeiter, string? mitarbeiterEmail, string? signaturHtml)
    {
        static string? Wert(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["Kunde.Firma"] = Wert(firma),
            ["Kunde.Name"] = Wert(ansprechpartner) ?? Wert(firma),
            ["Kunde.Email"] = Wert(ansprechpartnerEmail),
            ["Kunde.Kundennummer"] = Wert(kundennummer),
            ["Mitarbeiter.Name"] = Wert(mitarbeiter),
            ["Mitarbeiter.Email"] = Wert(mitarbeiterEmail),
            [SignaturPlatzhalter] = signaturHtml?.Trim() ?? "",
        };
    }
}
