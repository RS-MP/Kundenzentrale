namespace Rausoft.Erp.Domain.Customers;

/// <summary>
/// Kundenstammsatz. Leitplanke: Kunde ≠ Ansprechpartner ≠ Adresse ≠ Rechnungsempfänger.
/// Die beiden Partner-Verweise bilden die realen Modelle ab:
/// CSB (Vermittlung mit Tipp-Provision, Kunde bleibt Rechnungspartner) und
/// FineSolutions (Partner ist Rechnungsempfänger, Endkunde wird trotzdem als Kunde geführt).
/// </summary>
public class Kunde
{
    public int Id { get; set; }

    /// <summary>Aus dem zentralen Nummernkreis vergeben.</summary>
    public required string Kundennummer { get; set; }

    public required string Firmenname { get; set; }

    public string? Kundengruppe { get; set; }

    public Betriebsart? Betriebsart { get; set; }

    public KundenStatus Status { get; set; } = KundenStatus.Aktiv;

    /// <summary>Tipp-Geber (CSB-Modell); Kunde bleibt selbst Rechnungspartner.</summary>
    public int? VermitteltDurchPartnerId { get; set; }
    public Partner? VermitteltDurchPartner { get; set; }

    /// <summary>Gesetzt = „Rechnung an Partner" (FineSolutions-Modell); leer = Rechnung an den Kunden selbst.</summary>
    public int? RechnungsempfaengerPartnerId { get; set; }
    public Partner? RechnungsempfaengerPartner { get; set; }

    /// <summary>Umsatzsteuer-Identifikationsnummer, z. B. "DE262976662" — für Rechnungen/ZUGFeRD.</summary>
    public string? UStIdNr { get; set; }

    /// <summary>Referenz auf den Odoo-Datensatz für den Import und die Übergangszeit.</summary>
    public int? OdooId { get; set; }

    public string? Notiz { get; set; }

    public List<Ansprechpartner> Ansprechpartner { get; set; } = [];
    public List<Adresse> Adressen { get; set; } = [];

    public byte[] RowVersion { get; set; } = [];
}

public enum Betriebsart
{
    SharedCloud = 0,
    PrivateCloud = 1,
    OnPrem = 2,
}

public enum KundenStatus
{
    Aktiv = 0,
    Inaktiv = 1,
    // Lead folgt in Phase 3 (CRM)
}
