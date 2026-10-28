namespace Rausoft.Erp.Domain.Shared;

/// <summary>
/// Organisationsbereich (Vertrieb, Support, Buchhaltung …).
/// Grundlage der zweistufigen Aufgaben-Zuweisung ab Phase 2:
/// Bereich immer, Person optional (leer = Pool).
/// </summary>
public class Bereich
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public bool Aktiv { get; set; } = true;

    /// <summary>Bereichsleiter: pflegt zusammen mit den Admins die Vorgangstypen (Entscheidung 28.10.2026).</summary>
    public int? LeiterId { get; set; }
    public Mitarbeiter? Leiter { get; set; }
}
