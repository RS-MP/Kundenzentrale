using Rausoft.Erp.Domain.Customers;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Domain.Vorgaenge;

/// <summary>
/// Vorgang = Bestellung und ihre Teilaufgaben je Bereich (ANFORDERUNGEN.md, „Vorgänge").
/// Die Schritte sind normale <see cref="Aufgabe"/>n mit Vorgangsbezug. Der Status wird
/// nicht gepflegt, sondern aus den Schritten berechnet (<see cref="VorgangsLogik.Status"/>).
/// Im Übergang mit Odoo wird der Vorgang von Hand angelegt, mit Odoo-Auftragsnummer —
/// darüber bekommt er denselben Strang wie der importierte Odoo-Verlauf.
/// </summary>
public class Vorgang
{
    public int Id { get; set; }

    public int KundeId { get; set; }
    public Kunde? Kunde { get; set; }

    public int VorgangstypId { get; set; }
    public Vorgangstyp? Vorgangstyp { get; set; }

    /// <summary>Stand der Vorlage beim Anlegen (die Schritte sind kopiert, spätere Vorlagenänderungen wirken nicht).</summary>
    public int VorgangstypStand { get; set; }

    /// <summary>Anzeigename, z. B. „Neue Bestellung S01241".</summary>
    public required string Titel { get; set; }

    /// <summary>Odoo-Auftragsnummer (S…) im Parallelbetrieb.</summary>
    public string? OdooAuftrag { get; set; }

    /// <summary>
    /// Hinweis, der überall sichtbar ist, wo am Vorgang gearbeitet wird —
    /// z. B. „ACHTUNG: alten Vertragsstand versenden". Änderungen landen im Verlauf.
    /// </summary>
    public string? Hinweis { get; set; }

    public DateOnly Start { get; set; }

    public DateTime ErstelltAm { get; set; }

    /// <summary>Optimistic Concurrency: gleichzeitige Änderungen (Takt, Knopf, Bearbeitung) kollidieren statt sich zu überschreiben.</summary>
    public byte[] RowVersion { get; set; } = [];
    public int? ErstelltVonId { get; set; }
    public Mitarbeiter? ErstelltVon { get; set; }

    /// <summary>Gesetzt, sobald alle Schritte erledigt oder entfallen sind (für Filter und Sortierung).</summary>
    public DateTime? AbgeschlossenAm { get; set; }

    /// <summary>Strang der Kundenakte, z. B. „verkauf:lead:5" (aus dem Odoo-Import) oder „vorgang:12".</summary>
    public required string StrangSchluessel { get; set; }
    public required string StrangName { get; set; }

    public List<Aufgabe> Schritte { get; set; } = [];
}
