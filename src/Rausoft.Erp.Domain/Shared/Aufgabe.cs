using Rausoft.Erp.Domain.Customers;

namespace Rausoft.Erp.Domain.Shared;

/// <summary>
/// Aufgabe (Grundstock der Aufgabenverwaltung, ANFORDERUNGEN.md): zuständig ist
/// eine Person oder ein Bereichs-Pool, aus dem sich ein freier Mitarbeiter die
/// Aufgabe nimmt. Im Parallelbetrieb kommen offene Aufgaben auch aus den geplanten
/// Aktivitäten in Odoo (mail.activity, <see cref="OdooId"/>). Erledigte Aufgaben
/// erscheinen im Kundenverlauf.
/// </summary>
public class Aufgabe
{
    public int Id { get; set; }

    public int? KundeId { get; set; }
    public Kunde? Kunde { get; set; }

    public required string Titel { get; set; }

    /// <summary>Art wie in Odoo: „To-do", „Anruf", „E-Mail", „Termin" …</summary>
    public string? Art { get; set; }

    public string? Beschreibung { get; set; }

    public DateOnly? Faellig { get; set; }

    public int? ZustaendigId { get; set; }
    public Mitarbeiter? Zustaendig { get; set; }

    /// <summary>Bereichs-Pool, wenn keine Person zuständig ist.</summary>
    public int? ZustaendigBereichId { get; set; }
    public Bereich? ZustaendigBereich { get; set; }

    /// <summary>Zuständiger als Text, wenn der Odoo-Benutzer keinem Mitarbeiter zugeordnet werden kann.</summary>
    public string? ZustaendigName { get; set; }

    public AufgabenStatus Status { get; set; } = AufgabenStatus.Offen;

    /// <summary>Vorgang als Text (z. B. „Verkauf S01194") und als Strang-Schlüssel für den Filter der Akte.</summary>
    public string? Bezug { get; set; }
    public string? StrangSchluessel { get; set; }

    public AktivitaetsQuelle Quelle { get; set; } = AktivitaetsQuelle.Kundenzentrale;

    /// <summary>Odoo mail.activity-Id beim Import — macht den Import wiederholbar.</summary>
    public int? OdooId { get; set; }

    public DateTime ErstelltAm { get; set; }
    public int? ErstelltVonId { get; set; }
    public Mitarbeiter? ErstelltVon { get; set; }

    public DateTime? ErledigtAm { get; set; }
    public int? ErledigtVonId { get; set; }
    public Mitarbeiter? ErledigtVon { get; set; }
}

public enum AufgabenStatus
{
    Offen = 0,
    InArbeit = 1,
    Rueckfrage = 2,
    Zurueckgestellt = 3,
    Erledigt = 4,
}
