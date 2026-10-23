using Rausoft.Erp.Domain.Customers;

namespace Rausoft.Erp.Domain.Shared;

/// <summary>
/// Zusage oder Absprache mit dem Kunden („SSO über Entra ID", „Start KW 46") — in der
/// Akte unter „Vereinbart &amp; offen" gesammelt, damit z. B. die Technik nicht alle
/// Beteiligten fragen muss, was zugesagt wurde. Optional mit dem Verlaufseintrag
/// verknüpft, in dem sie getroffen wurde.
/// </summary>
public class Vereinbarung
{
    public int Id { get; set; }

    public int KundeId { get; set; }
    public Kunde? Kunde { get; set; }

    /// <summary>Gespräch, Mail oder Termin, aus dem die Vereinbarung stammt (optional).</summary>
    public int? AktivitaetId { get; set; }
    public Aktivitaet? Aktivitaet { get; set; }

    public required string Text { get; set; }

    public bool Erledigt { get; set; }

    public DateTime ErstelltAm { get; set; }
    public int? ErstelltVonId { get; set; }
    public Mitarbeiter? ErstelltVon { get; set; }

    public DateTime? ErledigtAm { get; set; }
    public int? ErledigtVonId { get; set; }
    public Mitarbeiter? ErledigtVon { get; set; }
}
