using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Domain.Customers;

/// <summary>
/// Achtung-Hinweis am Kunden (MP 26.11.2026): eine Anweisung an alle, die mit dem Kunden zu
/// tun haben — „immer mit Herr Dr. ansprechen“, „Abrechnung erst nach der Schulung“. Steht
/// überall dort deutlich sichtbar, wo am Kunden gearbeitet wird (Akte, E-Mail, Vorgang,
/// Vertrag, Aufgaben, Outlook), nicht nur in der Akte. Anders als eine Vereinbarung (Zusage an
/// den Kunden, wird erledigt) gilt ein Hinweis, bis ihn jemand entfernt.
/// </summary>
public class KundenHinweis
{
    public const int MaxLaenge = 500;

    /// <summary>Mehr wäre kein Hinweis mehr, sondern eine Liste, die niemand liest.</summary>
    public const int MaxJeKunde = 5;

    public int Id { get; set; }

    public int KundeId { get; set; }
    public Kunde? Kunde { get; set; }

    public required string Text { get; set; }

    public DateTime ErstelltAm { get; set; }
    public int? ErstelltVonId { get; set; }
    public Mitarbeiter? ErstelltVon { get; set; }
}
