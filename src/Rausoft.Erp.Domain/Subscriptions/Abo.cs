using Rausoft.Erp.Domain.Customers;

namespace Rausoft.Erp.Domain.Subscriptions;

/// <summary>
/// Abonnement eines Kunden (aus Odoo sale.order mit is_subscription importiert).
/// Phase-1-Vorgriff auf Phase 4: bewusst ohne Preise — hier geht es um
/// Laufzeiten und die gebuchten Lizenzen („Betrieb &amp; Lizenzen" der Akte).
/// Die Fakturierungsseite (Preise, Rechnungen) kommt in Phase 4 dazu.
/// </summary>
public class Abo
{
    public int Id { get; set; }

    public int KundeId { get; set; }
    public Kunde? Kunde { get; set; }

    /// <summary>Odoo-Auftragsnummer, z. B. "S01193".</summary>
    public required string Nummer { get; set; }

    public AboStatus Status { get; set; }

    /// <summary>Abrechnungs-/Verlängerungsintervall in Monaten (12 = jährlich).</summary>
    public int IntervallMonate { get; set; }

    public DateOnly? Beginn { get; set; }

    /// <summary>Vertraglicher Laufzeitbeginn (Odoo-Studio-Feld), falls gepflegt.</summary>
    public DateOnly? LaufzeitBeginn { get; set; }

    /// <summary>Nächste Verlängerung/Abrechnung.</summary>
    public DateOnly? NaechsteVerlaengerung { get; set; }

    public DateOnly? Ende { get; set; }

    /// <summary>sale.order-Id in Odoo (Import-Referenz).</summary>
    public int? OdooId { get; set; }

    public List<AboPosition> Positionen { get; set; } = [];
}

/// <summary>Gebuchte Position (Lizenz/Modul/Leistung) eines Abos.</summary>
public class AboPosition
{
    public int Id { get; set; }

    public int AboId { get; set; }
    public Abo? Abo { get; set; }

    public required string Produkt { get; set; }

    public decimal Menge { get; set; }
}

public enum AboStatus
{
    Entwurf = 0,
    Laufend = 1,
    /// <summary>Durch ein neueres Abo abgelöst (Odoo "renewed").</summary>
    Abgeloest = 2,
    Beendet = 3,
}
