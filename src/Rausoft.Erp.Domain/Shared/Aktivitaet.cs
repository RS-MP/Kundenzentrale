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
}
