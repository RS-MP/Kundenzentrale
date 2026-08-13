namespace Rausoft.Erp.Domain.Customers;

/// <summary>
/// Ansprechpartner eines Kunden. Anrede, Titel, Vor- und Nachname bewusst als
/// getrennte Felder (Odoo-Ärgernis); die Briefanrede wird nie gespeichert,
/// sondern immer über <see cref="Briefanrede"/> berechnet.
/// </summary>
public class Ansprechpartner
{
    public int Id { get; set; }

    public int KundeId { get; set; }
    public Kunde? Kunde { get; set; }

    public Anrede Anrede { get; set; } = Anrede.Keine;

    /// <summary>Akademischer Titel, z. B. "Dr.".</summary>
    public string? Titel { get; set; }

    /// <summary>Leer erlaubt, z. B. für Sammelpostfächer.</summary>
    public string? Vorname { get; set; }

    public required string Nachname { get; set; }

    /// <summary>Funktion beim Kunden, z. B. "GF", "Einkauf".</summary>
    public string? Position { get; set; }

    /// <summary>Grundlage der automatischen E-Mail-Zuordnung ab Phase 2.</summary>
    public string? Email { get; set; }

    public string? TelefonFestnetz { get; set; }
    public string? TelefonMobil { get; set; }

    public AnsprechpartnerRolle Rollen { get; set; }

    public bool Aktiv { get; set; } = true;

    public int? OdooId { get; set; }

    public string? Notiz { get; set; }

    public byte[] RowVersion { get; set; } = [];

    /// <summary>
    /// Berechnete Briefanrede — gilt überall (E-Mails, Mahnungen, Angebote, Serienbriefe):
    /// „Sehr geehrte Frau X" / „Sehr geehrter Herr Dr. Y" / ohne Anrede
    /// „Sehr geehrte Damen und Herren".
    /// </summary>
    public string Briefanrede()
    {
        var titelTeil = string.IsNullOrWhiteSpace(Titel) ? string.Empty : Titel.Trim() + " ";
        return Anrede switch
        {
            Anrede.Herr => $"Sehr geehrter Herr {titelTeil}{Nachname}",
            Anrede.Frau => $"Sehr geehrte Frau {titelTeil}{Nachname}",
            _ => "Sehr geehrte Damen und Herren",
        };
    }

    public string Anzeigename()
    {
        var teile = new[] { Titel, Vorname, Nachname }
            .Where(t => !string.IsNullOrWhiteSpace(t));
        return string.Join(' ', teile);
    }
}

public enum Anrede
{
    /// <summary>Keine Anrede, z. B. Sammelpostfächer → „Sehr geehrte Damen und Herren".</summary>
    Keine = 0,
    Herr = 1,
    Frau = 2,
}

[Flags]
public enum AnsprechpartnerRolle
{
    Keine = 0,
    Vertrag = 1,
    Technik = 2,
    Rechnungsempfang = 4,
    Hauptansprechpartner = 8,
}
