using Rausoft.Erp.Domain.Customers;

namespace Rausoft.Erp.Domain.Shared;

/// <summary>
/// Beteiligte Person an einem Verlaufseintrag — auf rausoft-Seite ein Mitarbeiter,
/// auf Kundenseite ein Ansprechpartner. Grundlage für „Wer mit wem" in der Akte;
/// ein Termin kann beliebig viele Teilnehmer auf beiden Seiten haben.
/// Ist die Person nicht zuordenbar (externer Absender), steht nur <see cref="Name"/>.
/// </summary>
public class AktivitaetTeilnehmer
{
    public int Id { get; set; }

    public int AktivitaetId { get; set; }
    public Aktivitaet? Aktivitaet { get; set; }

    public TeilnehmerSeite Seite { get; set; }

    public int? MitarbeiterId { get; set; }
    public Mitarbeiter? Mitarbeiter { get; set; }

    public int? AnsprechpartnerId { get; set; }
    public Ansprechpartner? Ansprechpartner { get; set; }

    /// <summary>Anzeigename, wenn weder Mitarbeiter noch Ansprechpartner zugeordnet sind.</summary>
    public string? Name { get; set; }
}

public enum TeilnehmerSeite
{
    Rausoft = 0,
    Kunde = 1,
}
