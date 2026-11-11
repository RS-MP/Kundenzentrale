using System.Net;
using System.Text.RegularExpressions;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.OdooImport;

/// <summary>
/// Eine Chatter-Nachricht aus Odoo (mail.message), auf das für die Bewertung
/// Nötige reduziert. Gilt für alle Modelle (res.partner, crm.lead, …).
/// </summary>
public record ChatterNachricht(
    int Id,
    string Modell,
    int ResId,
    string? RecordName,
    string? MessageType,
    int? SubtypeId,
    string? SubtypeXmlId,
    string? SubtypeName,
    int? AutorPartnerId,
    string? AutorName,
    string? EmailFrom,
    DateTime? Datum,
    string? Betreff,
    string Klartext,
    bool IstIntern,
    int TrackingAnzahl,
    int AnhangAnzahl,
    /// <summary>Feldänderungen dieser Nachricht (Feldbezeichnung, alt, neu), falls geladen.</summary>
    IReadOnlyList<(string Feld, string Alt, string Neu)> Aenderungen,
    /// <summary>Der Vorgang (z. B. Lead), an dem die Nachricht hängt, ist in Odoo archiviert.</summary>
    bool QuelleArchiviert = false);

public enum ChatterEntscheidung
{
    /// <summary>Wird als eigener Timeline-Eintrag übernommen.</summary>
    Behalten,
    /// <summary>Wird zu einem knappen Systemeintrag verdichtet (z. B. Phasenwechsel).</summary>
    Verdichten,
    /// <summary>Wird nicht übernommen.</summary>
    Verwerfen,
}

public record ChatterBewertung(
    ChatterEntscheidung Entscheidung,
    string Grund,
    AktivitaetsTyp? Typ = null,
    AktivitaetsRichtung? Richtung = null);

/// <summary>
/// Filterregeln „nur das Wesentliche" für den Odoo-Chatter. Werden von der
/// Analyse (Modus <c>chatter</c>) und später vom Import gemeinsam benutzt,
/// damit beide dieselben Entscheidungen treffen.
///
/// Regeln (Stand 22.10.2026 nach Bewertung am Beispielkunden 25757):
/// - Benachrichtigungen, reine Feldänderungen (Odoo 19: Art „tracking"),
///   OdooBot-Meldungen und automatische Bestätigungen → Verwerfen.
///   Ausnahme: Änderung des Wartungs-Hakens → Verdichten (in Odoo ist das der
///   Vertragsbeginn bzw. die Kündigung).
/// - Phasenwechsel / gewonnen / verloren / angelegt im CRM → Verdichten.
/// - Statuswechsel eines Angebots/Auftrags (sale.order) → Verdichten.
/// - Rechnung gestellt / bezahlt (Untertypen an der Rechnung, account.move) →
///   Verdichten (Entscheidung 22.10.2026: Buchhaltung bleibt bei DATEV, die
///   Information gehört trotzdem in die Akte). Rechnungsentwurf und die
///   Duplikat-Meldung am Auftrag fallen weg.
/// - Meilensteine archivierter Vorgänge (z. B. Junk-Lead aus Auto-Antwort) → Verwerfen.
/// - „Nachricht senden" ist immer ausgehend, auch von Firmenpostfächern ohne Benutzer.
/// - Signaturbilder (image001.jpg …) werden als Anhang übersprungen.
/// - Anhänge an behaltenen Nachrichten werden beim Import mit übernommen
///   (Entscheidung 22.10.2026) — die Analyse listet sie mit Name und Größe.
/// - Passwort-Zeilen im Text werden maskiert (Zugangsdaten-Mails).
/// - Hinweis „Lead wurde aus Ticket erstellt" → Verdichten (Herkunft).
/// - Abwesenheitsnotizen und andere Auto-Antworten → Verwerfen.
/// - Manuelle Notizen → Notiz (intern); „Nachricht senden" → E-Mail ausgehend.
/// - Echte E-Mails → E-Mail, Richtung nach Absender (Mitarbeiter oder Kunde).
/// - Leere Texte ohne Anhang → Verwerfen.
/// </summary>
public class ChatterFilter(IReadOnlySet<int> internePartnerIds, int? odooBotPartnerId)
{
    /// <summary>Untertypen, die als Meilenstein verdichtet werden, mit dem Timeline-Typ des Eintrags.</summary>
    private static readonly Dictionary<string, AktivitaetsTyp> VerdichtenSubtypes = new()
    {
        ["crm.mt_lead_stage"] = AktivitaetsTyp.System,
        ["crm.mt_lead_won"] = AktivitaetsTyp.System,
        ["crm.mt_lead_lost"] = AktivitaetsTyp.System,
        ["crm.mt_lead_create"] = AktivitaetsTyp.System,
        ["crm.mt_lead_restored"] = AktivitaetsTyp.System,
        // Verkauf: Angebot gesendet / Auftrag bestätigt — als Meilenstein, nicht als Mail
        ["sale.mt_order_sent"] = AktivitaetsTyp.Angebot,
        ["sale.mt_order_confirmed"] = AktivitaetsTyp.Angebot,
        // Rechnung gestellt / bezahlt (Buchhaltung bleibt bei DATEV, die Info gehört in die Akte).
        // Bewusst nicht: mt_invoice_created (Entwurf) — erst die gebuchte Rechnung zählt.
        ["account.mt_invoice_validated"] = AktivitaetsTyp.Rechnung,
        ["account.mt_invoice_paid"] = AktivitaetsTyp.Rechnung,
    };

    /// <summary>
    /// Eingebettete Signaturbilder aus E-Mails (image001.jpg, …) — kleine Bilder mit
    /// generischem Namen, die als Anhang nur Rauschen wären.
    /// </summary>
    public static bool IstSignaturbild(string name, string? mimetype, long groesse) =>
        mimetype is not null && mimetype.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
        && groesse < 300 * 1024
        && Regex.IsMatch(name, @"^(image\d{3}|Outlook-[\w-]+|~WRD\d+|oledata)\.(jpe?g|png|gif|bmp|emz)$",
            RegexOptions.IgnoreCase);

    /// <summary>Betreff-/Textanfänge automatischer Antworten (Abwesenheit, Auto-Reply).</summary>
    private static readonly string[] AutoAntwortAnfaenge =
    [
        "Automatische Antwort", "Automatic reply", "Auto-Reply", "AutoReply", "Auto Reply",
        "Out of Office", "Out-of-Office", "Abwesenheit", "Abwesenheitsnotiz", "Außer Haus",
    ];

    /// <summary>Von Odoo erzeugte Notizen, die nur die Herkunft eines Leads nennen.</summary>
    private static readonly string[] TicketHerkunftAnfaenge =
    [
        "Dieser Lead wurde aus einem Ticket erstellt", "This lead was created from ticket",
        "Diese Verkaufschance wurde aus einem Ticket erstellt", "This opportunity was created from ticket",
    ];

    public ChatterBewertung Bewerten(ChatterNachricht n)
    {
        var bewertung = BewertenRoh(n);
        // Archivierte Vorgänge (z. B. ein aus einer Auto-Antwort entstandener Lead): echte
        // Notizen/Mails bleiben, aber ihre Meilensteine wären nur Rauschen in der Akte.
        if (n.QuelleArchiviert && bewertung.Entscheidung == ChatterEntscheidung.Verdichten)
            return new(ChatterEntscheidung.Verwerfen, "archivierter Vorgang");
        return bewertung;
    }

    private ChatterBewertung BewertenRoh(ChatterNachricht n)
    {
        var vonMitarbeiter = n.AutorPartnerId is not null && internePartnerIds.Contains(n.AutorPartnerId.Value);
        var leer = n.Klartext.Length < 3;

        if (n.AutorPartnerId is not null && n.AutorPartnerId == odooBotPartnerId)
            return new(ChatterEntscheidung.Verwerfen, "OdooBot");
        if (n.AutorName is not null && n.AutorName.Contains("OdooBot", StringComparison.OrdinalIgnoreCase))
            return new(ChatterEntscheidung.Verwerfen, "OdooBot");

        // Erledigte geplante Aktivität (Anruf, To-do …) mit Rückmeldung — gehört zur Kommunikation
        if (n.SubtypeXmlId == "mail.mt_activities")
            return new(ChatterEntscheidung.Behalten, "Aufgabe erledigt", AktivitaetsTyp.Aufgabe, AktivitaetsRichtung.Intern);

        if (n.SubtypeXmlId is not null && VerdichtenSubtypes.TryGetValue(n.SubtypeXmlId, out var meilensteinTyp))
        {
            var bereich = meilensteinTyp switch
            {
                AktivitaetsTyp.Angebot => "Verkaufs-Ereignis",
                AktivitaetsTyp.Rechnung => "Rechnungs-Ereignis",
                _ => "CRM-Ereignis",
            };
            return new(ChatterEntscheidung.Verdichten, $"{bereich} ({n.SubtypeName})",
                meilensteinTyp, AktivitaetsRichtung.Intern);
        }

        // Die Meldung „Rechnung … bezahlt" am Auftrag ist ein Duplikat des Ereignisses an
        // der Rechnung selbst (account.mt_invoice_paid) und fällt als Benachrichtigung weg.

        if (n.Aenderungen.Any(a => a.Feld.StartsWith("Wartung", StringComparison.OrdinalIgnoreCase)))
            return new(ChatterEntscheidung.Verdichten, "Wartungs-Haken geändert",
                AktivitaetsTyp.System, AktivitaetsRichtung.Intern);

        // Angebot → Verkaufsauftrag (bzw. storniert): Meilenstein des Verkaufsprozesses.
        // In Odoo 19 trägt diese Feldänderung nicht immer den sale-Untertyp, daher über das Feld.
        if (n.Modell == "sale.order" && n.Aenderungen.Any(a =>
                a.Feld.StartsWith("Status", StringComparison.OrdinalIgnoreCase)
                || a.Feld.StartsWith("State", StringComparison.OrdinalIgnoreCase)))
            return new(ChatterEntscheidung.Verdichten, "Auftragsstatus geändert",
                AktivitaetsTyp.Angebot, AktivitaetsRichtung.Intern);

        if (BeginntMit(n.Klartext, TicketHerkunftAnfaenge))
            return new(ChatterEntscheidung.Verdichten, "Herkunft aus Ticket",
                AktivitaetsTyp.System, AktivitaetsRichtung.Intern);

        switch (n.MessageType)
        {
            case "notification":
            case "user_notification":
                return new(ChatterEntscheidung.Verwerfen,
                    n.TrackingAnzahl > 0 ? "Feldänderung" : "Benachrichtigung");
            case "tracking": // Odoo 19: eigene Art für Feldänderungen
                return new(ChatterEntscheidung.Verwerfen, "Feldänderung");
            case "auto_comment":
                return new(ChatterEntscheidung.Verwerfen, "automatische Bestätigung");
            case "email":
                if (leer && n.AnhangAnzahl == 0)
                    return new(ChatterEntscheidung.Verwerfen, "leere E-Mail");
                if (BeginntMit(n.Betreff, AutoAntwortAnfaenge) || BeginntMit(n.Klartext, AutoAntwortAnfaenge))
                    return new(ChatterEntscheidung.Verwerfen, "Abwesenheitsnotiz/Auto-Antwort");
                return new(ChatterEntscheidung.Behalten, "E-Mail eingehend",
                    AktivitaetsTyp.EMail,
                    vonMitarbeiter ? AktivitaetsRichtung.Ausgehend : AktivitaetsRichtung.Eingehend);
            case "email_outgoing":
                if (leer && n.AnhangAnzahl == 0)
                    return new(ChatterEntscheidung.Verwerfen, "leere E-Mail");
                return new(ChatterEntscheidung.Behalten, "E-Mail ausgehend",
                    AktivitaetsTyp.EMail, AktivitaetsRichtung.Ausgehend);
            case "comment":
                if (leer && n.AnhangAnzahl == 0)
                    return new(ChatterEntscheidung.Verwerfen, "leer");
                if (n.SubtypeXmlId == "mail.mt_note" || n.IstIntern)
                    return new(ChatterEntscheidung.Behalten, "Notiz",
                        AktivitaetsTyp.Notiz, AktivitaetsRichtung.Intern);
                // „Nachricht senden" im Chatter geht als E-Mail an die Follower/den Kunden —
                // immer aus Odoo heraus, also ausgehend, auch wenn der Absender ein
                // Firmenpostfach ohne Benutzer ist (z. B. „Rausoft Buchhaltung")
                return new(ChatterEntscheidung.Behalten, "Nachricht gesendet",
                    AktivitaetsTyp.EMail, AktivitaetsRichtung.Ausgehend);
            default:
                return new(ChatterEntscheidung.Verwerfen, $"unbekannte Art '{n.MessageType}'");
        }
    }

    private static bool BeginntMit(string? text, string[] anfaenge) =>
        text is not null && anfaenge.Any(a => text.StartsWith(a, StringComparison.OrdinalIgnoreCase));

    // Was der Browser nicht anzeigt: Kommentare sowie Kopf-, Stil- und Skriptbereiche samt Inhalt
    private static readonly Regex NichtAngezeigt = new(
        @"<!--.*?-->|<\s*(head|style|script|title)\b[^>]*>.*?<\s*/\s*\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Vorformatiert = new(
        @"(<\s*pre\b[^>]*>)(.*?)(<\s*/\s*pre\s*>)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex QuelltextLeerraum = new(@"[ \t\r\n\f]+", RegexOptions.Compiled);
    private static readonly Regex HtmlTag = new(@"<\s*(?<zu>/)?\s*(?<name>[a-zA-Z][a-zA-Z0-9]*)?[^>]*>", RegexOptions.Compiled);
    private static readonly Regex EnthaeltTag = new(@"<\s*/?\s*[a-zA-Z][^>]*>", RegexOptions.Compiled);

    // Marken für die Umwandlung (kommen in echten Texten nicht vor, werden vorab entfernt)
    private const char Blockgrenze = '\u0001';
    private const char Listenmarke = '\u0002';
    private static readonly Regex Steuerzeichen = new(@"[\u0001\u0002]", RegexOptions.Compiled);

    /// <summary>Elemente, die im Browser eine eigene Zeile bilden.</summary>
    private static readonly HashSet<string> BlockElemente = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "tr", "table", "blockquote", "pre", "hr",
        "section", "article", "header", "footer", "address", "dl", "dt", "dd", "center", "form",
    };
    private static readonly Regex MehrfachLeerzeichen = new(@"[ \t ]+", RegexOptions.Compiled);
    private static readonly Regex MehrfachUmbrueche = new(@"\n{3,}", RegexOptions.Compiled);

    // Unsichtbare Zeichen, die nach dem Entfernen der Tags als „Text" übrig bleiben und
    // Leerzeilen am Zusammenfassen hindern. Mailvorlagen (auch die Odoo-Rechnungsmail)
    // füllen damit den Vorschautext auf: je Zeile ein U+034F, dazu Null-Breite-Zeichen.
    // Enthalten: weiches Trennzeichen, Combining Grapheme Joiner, arabische Schriftmarke,
    // Hangul-Füllzeichen, mongolische Variantenwähler, Null-Breite-Leerzeichen/-Nichtverbinder,
    // Richtungsmarken und -einbettungen, Wortverbinder und unsichtbare Operatoren, BOM.
    // Der Null-Breite-Verbinder U+200D fehlt bewusst — er hält zusammengesetzte Emoji zusammen.
    private static readonly Regex UnsichtbareZeichen = new(
        @"[­͏؜ᅟᅠ᠋-᠎​‌‎‏‪-‮⁠-⁤⁦-⁯ㅤ﻿ﾠ]",
        RegexOptions.Compiled);

    // U+200D ohne sichtbaren Nachbarn auf einer Seite ist ebenfalls nur Füllung
    private static readonly Regex VerwaisterVerbinder = new(@"(?<=^|[\s\u0001\u0002])‍+|‍+(?=[\s\u0001\u0002]|$)", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Kennzeile für Zugangsdaten: „Passwort: …", „Kennwort = …", „PW: …",
    /// auch zusammengesetzt wie „Passwort-Einmal-Link:" oder „Initialpasswort:".
    /// Der Wert steht dahinter oder — bei leerem Rest — in der nächsten Zeile.
    /// </summary>
    private static readonly Regex PasswortKennzeile = new(
        @"^(?<k>.*?(?:passw(?:or)?[dt]|kennwort|\bpwd?\b)[\w-]*\s*[:=])\s*(?<v>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Links von Diensten zum einmaligen Teilen von Geheimnissen — sie sind bis zum Abruf ein Zugangsschlüssel.</summary>
    private static readonly Regex EinmalLink = new(
        @"https?://\S*(?:keepersecurity\.(?:com|eu)/vault/share|onetimesecret\.com/secret|pwpush\.com/p/|share\.1password\.com|password\.link/|send\.bitwarden\.com|vault\.bitwarden\.com/#/send|privnote\.com/|1ty\.me/)\S*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public const string Maske = "•••";

    /// <summary>
    /// Zugangsdaten aus alten Odoo-Mails dürfen nicht im Klartext in die
    /// Kundenzentrale wandern (Verschlüsselungs-Konvention): Passwortzeilen und
    /// Einmal-Links werden maskiert, der Rest der Mail bleibt lesbar.
    /// </summary>
    public static string ZugangsdatenMaskieren(string text)
    {
        var zeilen = text.Split('\n');
        for (var i = 0; i < zeilen.Length; i++)
        {
            var treffer = PasswortKennzeile.Match(zeilen[i].TrimEnd('\r'));
            if (!treffer.Success)
                continue;
            if (treffer.Groups["v"].Value.Trim().Length > 0)
                zeilen[i] = treffer.Groups["k"].Value + " " + Maske;
            else
            {
                // Wert in der nächsten Zeile mit Inhalt — eine Leerzeile dazwischen ändert daran nichts
                var naechste = i + 1;
                while (naechste < zeilen.Length && naechste <= i + 2 && zeilen[naechste].Trim().Length == 0)
                    naechste++;
                if (naechste < zeilen.Length && naechste <= i + 2)
                {
                    zeilen[naechste] = Maske;
                    i = naechste;
                }
            }
        }
        return EinmalLink.Replace(string.Join('\n', zeilen), Maske + " (Einmal-Link entfernt)");
    }

    /// <summary>
    /// HTML-Body aus Odoo → lesbarer Klartext, so wie der Browser ihn umbricht:
    /// Zeilenumbrüche im Quelltext zählen nicht (nur Absätze, &lt;br&gt; und Listen),
    /// leere Absätze bleiben als eine Leerzeile, Aufzählungen bekommen ihr Zeichen.
    /// Zugangsdaten werden maskiert.
    /// </summary>
    public static string Klartext(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;
        var text = Steuerzeichen.Replace(html, string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        if (EnthaeltTag.IsMatch(text))
        {
            text = NichtAngezeigt.Replace(text, string.Empty);
            // In <pre> sind die Umbrüche des Quelltexts echte Umbrüche, überall sonst nur Leerraum
            text = Vorformatiert.Replace(text,
                m => m.Groups[1].Value + m.Groups[2].Value.Replace("\n", "<br>") + m.Groups[3].Value);
            text = QuelltextLeerraum.Replace(text, " ");
            text = TagsUmwandeln(text);
        }
        text = WebUtility.HtmlDecode(text);
        // Erst nach dem Dekodieren: die Füllzeichen stehen im HTML meist als &#847; / &zwnj; / &shy;
        text = UnsichtbareZeichen.Replace(text, string.Empty);
        text = VerwaisterVerbinder.Replace(text, string.Empty);
        text = MehrfachUmbrueche.Replace(BloeckeZusammensetzen(text), "\n\n");
        return ZugangsdatenMaskieren(text.Trim('\n'));
    }

    /// <summary>
    /// Ersetzt Tags durch Marken: Blockgrenze für Absätze und andere Blockelemente,
    /// „\n" für &lt;br&gt;, Listenmarke samt Aufzählungszeichen für &lt;li&gt;. Alles andere fällt weg.
    /// </summary>
    private static string TagsUmwandeln(string html)
    {
        // Je offener Liste: 0 = Aufzählung, sonst die nächste Nummer
        var listen = new Stack<int>();
        return HtmlTag.Replace(html, m =>
        {
            var name = m.Groups["name"].Value.ToLowerInvariant();
            var zu = m.Groups["zu"].Success;
            switch (name)
            {
                case "br":
                    return "\n";
                case "ul" or "ol":
                    if (!zu)
                    {
                        listen.Push(name == "ol" ? 1 : 0);
                        return Blockgrenze.ToString();
                    }
                    if (listen.Count > 0)
                        listen.Pop();
                    // Nach der äußersten Liste eine Leerzeile, wie der Abstand im Browser
                    return listen.Count == 0 ? $"{Blockgrenze}\n{Blockgrenze}" : Blockgrenze.ToString();
                case "li":
                    if (zu) // leere Marke: ein nicht verbrauchtes Zeichen gilt nicht für den Text danach
                        return $"{Blockgrenze}{Listenmarke}{Listenmarke}";
                    var einzug = new string(' ', 2 * Math.Max(0, listen.Count - 1));
                    var zeichen = "• ";
                    if (listen.Count > 0 && listen.Peek() > 0)
                    {
                        var nummer = listen.Pop();
                        listen.Push(nummer + 1);
                        zeichen = $"{nummer}. ";
                    }
                    return $"{Blockgrenze}{Listenmarke}{einzug}{zeichen}{Listenmarke}";
                case "td" or "th":
                    return zu ? " " : string.Empty;
                default:
                    return BlockElemente.Contains(name) ? Blockgrenze.ToString() : string.Empty;
            }
        });
    }

    /// <summary>
    /// Setzt die Blöcke zu Zeilen zusammen. Ein Block ohne Inhalt erzeugt keine Zeile;
    /// ein Block, der nur aus einem Umbruch oder geschützten Leerzeichen besteht
    /// (leerer Absatz in Odoo bzw. Outlook), ergibt eine Leerzeile. Ein Umbruch am
    /// Blockende zählt wie im Browser nicht.
    /// </summary>
    private static string BloeckeZusammensetzen(string text)
    {
        var zeilen = new List<string>();
        string? listenzeichen = null;
        foreach (var block in text.Split(Blockgrenze))
        {
            var inhalt = block;
            if (inhalt.Length > 0 && inhalt[0] == Listenmarke && inhalt.IndexOf(Listenmarke, 1) is > 0 and var ende)
            {
                listenzeichen = ende > 1 ? inhalt[1..ende] : null;
                inhalt = inhalt[(ende + 1)..];
            }
            inhalt = inhalt.Replace(Listenmarke.ToString(), string.Empty);
            if (inhalt.Trim(' ', '\t').Length == 0)
                continue;
            if (inhalt.Trim(' ', '\t', ' ', '\n').Length == 0)
            {
                if (listenzeichen is null)
                    zeilen.Add(string.Empty);
                continue;
            }
            if (inhalt.TrimEnd(' ', '\t', ' ').EndsWith('\n'))
                inhalt = inhalt[..inhalt.LastIndexOf('\n')];
            foreach (var roh in inhalt.Split('\n'))
            {
                var zeile = MehrfachLeerzeichen.Replace(roh, " ").Trim();
                if (zeile.Length > 0 && listenzeichen is not null)
                {
                    zeile = listenzeichen + zeile;
                    listenzeichen = null;
                }
                zeilen.Add(zeile);
            }
        }
        return string.Join('\n', zeilen);
    }

    /// <summary>
    /// Anzeigestufe für die Filterentscheidung: Behalten = wesentlich, Verdichten =
    /// Meilenstein; Verworfenes wird trotzdem importiert, aber als Feldänderung bzw.
    /// Systemmeldung standardmäßig ausgeblendet (Entscheidung 23.10.2026).
    /// </summary>
    public static AktivitaetsStufe StufeVon(ChatterBewertung b) => b.Entscheidung switch
    {
        ChatterEntscheidung.Behalten => AktivitaetsStufe.Wesentlich,
        ChatterEntscheidung.Verdichten => AktivitaetsStufe.Meilenstein,
        _ => b.Grund == "Feldänderung" ? AktivitaetsStufe.Feldaenderung : AktivitaetsStufe.Systemmeldung,
    };
}
