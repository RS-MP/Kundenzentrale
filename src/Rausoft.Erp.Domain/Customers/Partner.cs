namespace Rausoft.Erp.Domain.Customers;

/// <summary>
/// Vertriebs-/Tipp-Partner — eigener Kontakttyp mit Provisionsmodell (Leitplanke).
/// Beispiele: CSB (Tipp-Provision 15 %), FineSolutions (Vertriebspartner Schweiz).
/// </summary>
public class Partner
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public string? Strasse { get; set; }
    public string? Plz { get; set; }
    public string? Ort { get; set; }

    /// <summary>Ländercode ISO 3166-1 alpha-2, z. B. "DE", "CH".</summary>
    public string Land { get; set; } = "DE";

    public string? Email { get; set; }
    public string? Telefon { get; set; }

    public Provisionsmodell Provisionsmodell { get; set; }

    /// <summary>Provisionssatz in Prozent, z. B. 15.00.</summary>
    public decimal? ProvisionProzent { get; set; }

    public int? OdooId { get; set; }

    public bool Aktiv { get; set; } = true;

    public byte[] RowVersion { get; set; } = [];
}

public enum Provisionsmodell
{
    /// <summary>Partner vermittelt, stellt rausoft die Provision in Rechnung; Endkunde bleibt Rechnungspartner von rausoft.</summary>
    TippProvision = 0,

    /// <summary>Partner verkauft; rausoft stellt die Rechnung an den Partner, Endkunden werden trotzdem als Kunden geführt.</summary>
    Vertriebspartner = 1,
}
