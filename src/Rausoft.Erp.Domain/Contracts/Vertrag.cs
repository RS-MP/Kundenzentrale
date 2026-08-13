using Rausoft.Erp.Domain.Customers;

namespace Rausoft.Erp.Domain.Contracts;

/// <summary>
/// Vertrag eines Kunden. Kündigungstermine werden aus Beginn/Laufzeit/Frist
/// berechnet, nicht gespeichert. Die Kette Angebot → Vertrag → Abo → Rechnung
/// ist mitgedacht: AngebotId (FK) folgt in Phase 3, Abos docken in Phase 4 an.
/// </summary>
public class Vertrag
{
    public int Id { get; set; }

    /// <summary>Aus dem zentralen Nummernkreis vergeben.</summary>
    public required string Vertragsnummer { get; set; }

    public int KundeId { get; set; }
    public Kunde? Kunde { get; set; }

    public VertragsStatus Status { get; set; } = VertragsStatus.Entwurf;

    public DateOnly Beginn { get; set; }

    public int? MindestlaufzeitMonate { get; set; }

    /// <summary>Automatische Verlängerung um n Monate, wenn nicht gekündigt.</summary>
    public int? VerlaengerungMonate { get; set; }

    public int? KuendigungsfristMonate { get; set; }

    public DateOnly? GekuendigtZum { get; set; }

    public string? Notiz { get; set; }

    public int? OdooId { get; set; }

    public List<Vertragsbestandteil> Bestandteile { get; set; } = [];

    public byte[] RowVersion { get; set; } = [];

    /// <summary>
    /// Nächstmögliches Vertragsende bei Kündigung am Stichtag: Ende der
    /// Mindestlaufzeit bzw. der jeweils aktuellen Verlängerungsperiode.
    /// Null, wenn keine Laufzeitangaben gepflegt sind.
    /// </summary>
    public DateOnly? NaechstesVertragsende(DateOnly stichtag)
    {
        if (GekuendigtZum is not null)
            return GekuendigtZum;
        if (MindestlaufzeitMonate is null)
            return null;

        var ende = Beginn.AddMonths(MindestlaufzeitMonate.Value);
        var frist = KuendigungsfristMonate ?? 0;

        // Solange die Kündigungsfrist zum Stichtag nicht mehr einzuhalten ist,
        // verschiebt sich das Ende um die Verlängerungsperiode.
        while (stichtag.AddMonths(frist) > ende)
        {
            if (VerlaengerungMonate is null or <= 0)
                return ende; // keine automatische Verlängerung vereinbart
            ende = ende.AddMonths(VerlaengerungMonate.Value);
        }

        return ende;
    }
}

public enum VertragsStatus
{
    Entwurf = 0,
    Aktiv = 1,
    Gekuendigt = 2,
    Beendet = 3,
}
