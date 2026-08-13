namespace Rausoft.Erp.Domain.Customers;

/// <summary>
/// Adresse eines Kunden. Abweichende Rechnungsanschrift = eigene Adresse mit
/// Typ <see cref="AdressTyp.Rechnung"/>.
/// </summary>
public class Adresse
{
    public int Id { get; set; }

    public int KundeId { get; set; }
    public Kunde? Kunde { get; set; }

    public AdressTyp Typ { get; set; } = AdressTyp.Hauptsitz;

    /// <summary>Adresszusatz, z. B. "Gebäude B, 3. OG".</summary>
    public string? Zusatz { get; set; }

    public required string Strasse { get; set; }
    public required string Plz { get; set; }
    public required string Ort { get; set; }

    /// <summary>Ländercode ISO 3166-1 alpha-2.</summary>
    public string Land { get; set; } = "DE";

    public bool Aktiv { get; set; } = true;
}

public enum AdressTyp
{
    Hauptsitz = 0,
    Rechnung = 1,
    Sonstige = 2,
}
