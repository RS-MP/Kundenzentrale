using Rausoft.Erp.Domain.Customers;
using Rausoft.Erp.Domain.Vorgaenge;

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

    // --- Vorgangsschritt (nur gesetzt, wenn die Aufgabe zu einem Vorgang gehört) -------

    public int? VorgangId { get; set; }
    public Vorgang? Vorgang { get; set; }

    /// <summary>Position im Vorgang, beginnend bei 1.</summary>
    public int? Reihenfolge { get; set; }

    /// <summary>Vorgänger-Schritt: Solange er nicht erledigt ist, liegt dieser Schritt in keinem Pool.</summary>
    public int? WartetAufId { get; set; }
    public Aufgabe? WartetAuf { get; set; }

    public SchrittHerkunft? Herkunft { get; set; }

    /// <summary>Frist in Tagen ab Freigabe; <see cref="Faellig"/> wird gesetzt, sobald der Schritt startet.</summary>
    public int? FristTage { get; set; }

    /// <summary>Wiedervorlage bei „Wartet auf Kunden" — am Stichtag ist der Schritt wieder offen.</summary>
    public DateOnly? Wiedervorlage { get; set; }

    /// <summary>Warum der Schritt entfallen ist: „entfernt" (von Hand) oder „bedingung" (nicht bestellt).</summary>
    public string? EntfallenGrund { get; set; }

    /// <summary>Beim Erledigen auszufüllendes Feld (z. B. „Mandanten-ID") und der eingetragene Wert.</summary>
    public string? Pflichtfeld { get; set; }
    public string? PflichtfeldWert { get; set; }

    public Benachrichtigung Benachrichtigung { get; set; }
    public AutoAbhaken AutoAbhaken { get; set; }

    /// <summary>
    /// Offen im Sinne der Akte und der Aufgabenlisten: nicht erledigt, nicht entfallen
    /// und nicht durch einen unerledigten Vorgänger gesperrt.
    /// </summary>
    public static readonly System.Linq.Expressions.Expression<Func<Aufgabe, bool>> IstOffen =
        a => a.Status != AufgabenStatus.Erledigt && a.Status != AufgabenStatus.Entfallen
             && (a.WartetAufId == null
                 || a.WartetAuf!.Status == AufgabenStatus.Erledigt || a.WartetAuf.Status == AufgabenStatus.Entfallen);
}

public enum AufgabenStatus
{
    Offen = 0,
    InArbeit = 1,
    Rueckfrage = 2,
    Zurueckgestellt = 3,
    Erledigt = 4,
    /// <summary>Bewusst gesetzt, mit <see cref="Aufgabe.Wiedervorlage"/> (Entscheidung 28.10.2026).</summary>
    WartetAufKunden = 5,
    /// <summary>Vorgangsschritt, der in diesem Vorgang nicht gebraucht wird (von Hand entfernt oder Bedingung nicht erfüllt).</summary>
    Entfallen = 6,
}

/// <summary>Woher ein Vorgangsschritt stammt.</summary>
public enum SchrittHerkunft
{
    Vorlage = 0,
    /// <summary>Von Hand zum einzelnen Vorgang hinzugefügt.</summary>
    Manuell = 1,
}
