namespace Rausoft.Erp.Domain.Customers;

/// <summary>
/// Stammliste der vergebbaren Kunden-Merkmale (z. B. "Insolvenz", "Einmalkauf").
/// Verhindert Freitext-Wildwuchs; neue Merkmale können ergänzt werden und
/// stehen dann allen zur Auswahl.
/// </summary>
public class Merkmal
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public bool Aktiv { get; set; } = true;
}
