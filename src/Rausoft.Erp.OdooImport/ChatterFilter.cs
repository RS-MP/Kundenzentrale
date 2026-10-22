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

    private static readonly Regex ZeilenumbruchTags = new(@"<\s*(br|/p|/div|/li|/tr|/h[1-6])\s*/?\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AlleTags = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex MehrfachLeerzeichen = new(@"[ \t ]+", RegexOptions.Compiled);
    private static readonly Regex MehrfachUmbrueche = new(@"(\r?\n\s*){2,}", RegexOptions.Compiled);

    /// <summary>
    /// Zeilen wie „Passwort: geheim123" → „Passwort: •••". Zugangsdaten aus
    /// alten Odoo-Mails dürfen nicht im Klartext in die Kundenzentrale wandern
    /// (Verschlüsselungs-Konvention); der Rest der Mail bleibt lesbar.
    /// </summary>
    private static readonly Regex PasswortZeile = new(
        @"(?im)^(.*\b(?:passwor[dt]|kennwort|pwd?|initial-?passwor[dt])\b\s*[:=]\s*)\S.*$",
        RegexOptions.Compiled);

    /// <summary>
    /// HTML-Body aus Odoo → lesbarer Klartext (Absätze bleiben als Zeilen
    /// erhalten), Passwort-Zeilen maskiert.
    /// </summary>
    public static string Klartext(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;
        var text = ZeilenumbruchTags.Replace(html, "\n");
        text = AlleTags.Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = MehrfachLeerzeichen.Replace(text, " ");
        text = MehrfachUmbrueche.Replace(text, "\n");
        text = PasswortZeile.Replace(text, "$1•••");
        return text.Trim();
    }
}
