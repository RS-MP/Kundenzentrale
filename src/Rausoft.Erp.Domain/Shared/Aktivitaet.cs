using Rausoft.Erp.Domain.Customers;

namespace Rausoft.Erp.Domain.Shared;

/// <summary>
/// Generische Aktivitäten-Tabelle = Kunden-Timeline (Leitplanke).
/// Phase 1 schreibt Vertrags- und Kontaktereignisse; E-Mail, Anrufe und
/// Aufgaben (Phase 2) nutzen dieselbe Tabelle.
/// Jeder Eintrag zeigt das Gesprächspaar: rausoft-Mitarbeiter ↔ Kunden-Ansprechpartner.
/// </summary>
public class Aktivitaet
{
    public int Id { get; set; }

    public int KundeId { get; set; }
    public Kunde? Kunde { get; set; }

    public AktivitaetsTyp Typ { get; set; }

    public DateTime Zeitpunkt { get; set; }

    /// <summary>rausoft-Seite des Gesprächspaars; null bei Systemvorgängen.</summary>
    public int? MitarbeiterId { get; set; }
    public Mitarbeiter? Mitarbeiter { get; set; }

    /// <summary>Kundenseite des Gesprächspaars.</summary>
    public int? AnsprechpartnerId { get; set; }
    public Ansprechpartner? Ansprechpartner { get; set; }

    public AktivitaetsRichtung? Richtung { get; set; }

    /// <summary>Anzeigetext in der Timeline.</summary>
    public required string Betreff { get; set; }

    public string? Inhalt { get; set; }

    /// <summary>Sprungziel des Eintrags (z. B. Vertrag), typisiert + Id.</summary>
    public AktivitaetsVerweisTyp? VerweisTyp { get; set; }
    public int? VerweisId { get; set; }

    /// <summary>Woher der Eintrag stammt — in der Akte umschaltbar (z. B. Odoo-Verlauf aus-/einblenden).</summary>
    public AktivitaetsQuelle Quelle { get; set; } = AktivitaetsQuelle.Kundenzentrale;

    /// <summary>Odoo mail.message-Id beim Chatter-Import — macht den Import wiederholbar.</summary>
    public int? OdooId { get; set; }

    /// <summary>
    /// Gewicht des Eintrags für die Anzeige. Eigene Einträge der Kundenzentrale sind
    /// immer <see cref="AktivitaetsStufe.Wesentlich"/>; beim Odoo-Import setzt der
    /// Chatter-Filter die Stufe, die Akte blendet nach Kategorie ein und aus.
    /// </summary>
    public AktivitaetsStufe Stufe { get; set; } = AktivitaetsStufe.Wesentlich;

    /// <summary>Begründung des Filters beim Import (z. B. „Feldänderung", „Abwesenheitsnotiz").</summary>
    public string? FilterGrund { get; set; }

    /// <summary>Odoo-Vorgang, an dem die Nachricht hing (z. B. „sale.order") — Schlüssel für Marke und Filter.</summary>
    public string? OdooModell { get; set; }

    /// <summary>Datensatz-Id des Odoo-Vorgangs (zusammen mit <see cref="OdooModell"/>).</summary>
    public int? OdooResId { get; set; }

    /// <summary>
    /// Fachlicher Zusammenhang für die Stränge der Akte, z. B. „verkauf:lead:5" — ein Verkauf
    /// umfasst Verkaufschance, Angebote/Aufträge und die Rechnungen daraus; „partner:61917"
    /// für Notizen an Firma oder Kontakt. Aufeinanderfolgende Einträge mit gleichem Schlüssel
    /// werden gebündelt; der Schlüssel ist auch der Vorgangsfilter.
    /// </summary>
    public string? StrangSchluessel { get; set; }

    /// <summary>Anzeigename des Strangs, z. B. „Verkauf S01194".</summary>
    public string? StrangName { get; set; }

    /// <summary>
    /// Anzeigename des Urhebers, wenn weder Mitarbeiter noch Ansprechpartner
    /// zugeordnet werden konnten (externer Absender, unbekannter Odoo-Benutzer).
    /// </summary>
    public string? AutorName { get; set; }

    /// <summary>
    /// Empfänger einer Nachricht als Anzeigetext, z. B. „Frau Berger, Herr Kunz" —
    /// beim Odoo-Import aus den Empfängern der Nachricht (partner_ids).
    /// </summary>
    public string? Empfaenger { get; set; }

    /// <summary>
    /// Vorgang, an dem der Eintrag ursprünglich hing, als Text — z. B. „Auftrag S01194",
    /// „Verkaufschance von …", „Rechnung RE/2026/00774". Für Odoo-Vorgänge gibt es
    /// kein eigenes Ziel in der Kundenzentrale; der Text hält den Zusammenhang.
    /// </summary>
    public string? Bezug { get; set; }

    public List<AktivitaetAnhang> Anhaenge { get; set; } = [];

    /// <summary>Alle Beteiligten auf beiden Seiten (Termin mit mehreren Personen, Mail an mehrere Empfänger).</summary>
    public List<AktivitaetTeilnehmer> Teilnehmer { get; set; } = [];
}

public enum AktivitaetsStufe
{
    /// <summary>Eigener Eintrag mit Inhalt: Notiz, E-Mail, Gespräch, Ereignis der Kundenzentrale.</summary>
    Wesentlich = 0,
    /// <summary>Verdichtetes Ereignis: Verkaufschance gewonnen, Rechnung bezahlt, Wartung aktiviert …</summary>
    Meilenstein = 1,
    /// <summary>Systemmeldung ohne Mehrwert: „Kontakt erstellt", Zuweisungen, OdooBot, Abwesenheitsnotizen.</summary>
    Systemmeldung = 2,
    /// <summary>Reine Feldänderung (Odoo-Tracking): Telefonnummer, Adresse, Beträge …</summary>
    Feldaenderung = 3,
}

/// <summary>Umschaltbare Kategorien der Verlaufsanzeige (Schalter in der Kundenakte).</summary>
public enum VerlaufKategorie
{
    Notizen = 0,
    EMails = 1,
    Meilensteine = 2,
    Systemmeldungen = 3,
    Feldaenderungen = 4,
}

public static class VerlaufKategorien
{
    /// <summary>Beim Öffnen der Akte eingeblendet: die Kommunikation (Entscheidung 23.10.2026).</summary>
    public static readonly VerlaufKategorie[] Standard = [VerlaufKategorie.Notizen, VerlaufKategorie.EMails];

    /// <summary>Kategorie eines Eintrags aus Stufe und Typ.</summary>
    public static VerlaufKategorie Von(AktivitaetsStufe stufe, AktivitaetsTyp typ) => stufe switch
    {
        AktivitaetsStufe.Feldaenderung => VerlaufKategorie.Feldaenderungen,
        AktivitaetsStufe.Systemmeldung => VerlaufKategorie.Systemmeldungen,
        AktivitaetsStufe.Meilenstein => VerlaufKategorie.Meilensteine,
        _ => typ switch
        {
            AktivitaetsTyp.EMail => VerlaufKategorie.EMails,
            // Gespräche und Aufgaben gehören zur Kommunikation
            AktivitaetsTyp.Notiz or AktivitaetsTyp.Anruf or AktivitaetsTyp.Termin or AktivitaetsTyp.Aufgabe => VerlaufKategorie.Notizen,
            // Vertrags-, Angebots-, Rechnungs- und Systemereignisse der Kundenzentrale
            _ => VerlaufKategorie.Meilensteine,
        },
    };

    /// <summary>
    /// Datenbankfilter „Eintrag gehört zu einer der gewählten Kategorien" — dieselbe
    /// Zuordnung wie <see cref="Von"/>, als Ausdruck für EF Core.
    /// </summary>
    public static System.Linq.Expressions.Expression<Func<Aktivitaet, bool>> Filter(IReadOnlyCollection<VerlaufKategorie> gewaehlt)
    {
        var notizen = gewaehlt.Contains(VerlaufKategorie.Notizen);
        var mails = gewaehlt.Contains(VerlaufKategorie.EMails);
        var meilensteine = gewaehlt.Contains(VerlaufKategorie.Meilensteine);
        var system = gewaehlt.Contains(VerlaufKategorie.Systemmeldungen);
        var felder = gewaehlt.Contains(VerlaufKategorie.Feldaenderungen);
        return a =>
            (felder && a.Stufe == AktivitaetsStufe.Feldaenderung)
            || (system && a.Stufe == AktivitaetsStufe.Systemmeldung)
            || (meilensteine && (a.Stufe == AktivitaetsStufe.Meilenstein
                || (a.Stufe == AktivitaetsStufe.Wesentlich && a.Typ != AktivitaetsTyp.EMail
                    && a.Typ != AktivitaetsTyp.Notiz && a.Typ != AktivitaetsTyp.Anruf
                    && a.Typ != AktivitaetsTyp.Termin && a.Typ != AktivitaetsTyp.Aufgabe)))
            || (mails && a.Stufe == AktivitaetsStufe.Wesentlich && a.Typ == AktivitaetsTyp.EMail)
            || (notizen && a.Stufe == AktivitaetsStufe.Wesentlich
                && (a.Typ == AktivitaetsTyp.Notiz || a.Typ == AktivitaetsTyp.Anruf
                    || a.Typ == AktivitaetsTyp.Termin || a.Typ == AktivitaetsTyp.Aufgabe));
    }
}

public enum AktivitaetsQuelle
{
    Kundenzentrale = 0,
    /// <summary>Aus dem Odoo-Chatter übernommen (Notizen, E-Mails, verdichtete Ereignisse).</summary>
    OdooImport = 1,
}

public enum AktivitaetsTyp
{
    Notiz = 0,
    Anruf = 1,
    EMail = 2,
    Vertrag = 3,
    Angebot = 4,
    Rechnung = 5,
    Ticket = 6,
    Aufgabe = 7,
    System = 8,
    /// <summary>Termin oder Besprechung, meist mit mehreren Teilnehmern.</summary>
    Termin = 9,
}

public enum AktivitaetsRichtung
{
    Eingehend = 0,
    Ausgehend = 1,
    Intern = 2,
}

public enum AktivitaetsVerweisTyp
{
    Vertrag = 0,
    Angebot = 1,
    Rechnung = 2,
    Ticket = 3,
    Aufgabe = 4,
    EMail = 5,
    Vorgang = 6,
    /// <summary>Anfrage aus einem Website-Formular (<see cref="Customers.Anfrage"/>).</summary>
    Anfrage = 7,
}
