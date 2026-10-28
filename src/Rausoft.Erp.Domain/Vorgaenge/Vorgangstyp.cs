using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Domain.Vorgaenge;

/// <summary>
/// Vorlage eines Vorgangs (ANFORDERUNGEN.md, „Vorgänge", Entscheidung 28.10.2026):
/// Neue Bestellung, Upgrade/Upsell, Stundenauftrag, Verkauf ohne Zwischenschritte …
/// Pflegen dürfen Admins und Bereichsleiter. Nicht mehr benötigte Typen werden
/// deaktiviert statt gelöscht. Laufende Vorgänge kopieren die Schritte beim Anlegen
/// und bleiben von späteren Änderungen unberührt (kein „veraltet").
/// </summary>
public class Vorgangstyp
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Kurzbeschreibung für die Auswahl beim Anlegen.</summary>
    public string? Beschreibung { get; set; }

    /// <summary>Deaktivieren statt Löschen — inaktive Typen stehen für neue Vorgänge nicht zur Auswahl.</summary>
    public bool Aktiv { get; set; } = true;

    /// <summary>Wird bei jeder Änderung der Schritte hochgezählt; der Vorgang merkt sich, nach welchem Stand er angelegt wurde.</summary>
    public int Stand { get; set; } = 1;

    public DateTime GeaendertAm { get; set; }
    public int? GeaendertVonId { get; set; }
    public Mitarbeiter? GeaendertVon { get; set; }

    public List<VorgangstypSchritt> Schritte { get; set; } = [];
}

/// <summary>Ein Schritt der Vorlage — wird beim Anlegen eines Vorgangs zu einer Aufgabe im Bereichs-Pool.</summary>
public class VorgangstypSchritt
{
    public int Id { get; set; }

    public int VorgangstypId { get; set; }
    public Vorgangstyp? Vorgangstyp { get; set; }

    /// <summary>Position in der Vorlage, beginnend bei 1. „Wartet auf" verweist auf diese Nummer.</summary>
    public int Nr { get; set; }

    public required string Titel { get; set; }

    public int BereichId { get; set; }
    public Bereich? Bereich { get; set; }

    /// <summary>Frist in Tagen ab Start des Schritts (Vorgangsstart oder Erledigung des Vorgängers).</summary>
    public int FristTage { get; set; }

    /// <summary>Nummer des Schritts, auf den dieser wartet (optional, immer kleiner als <see cref="Nr"/>).</summary>
    public int? WartetAufNr { get; set; }

    /// <summary>Bedingung, z. B. „Schulung bestellt" — beim Anlegen per Häkchen; leer = immer.</summary>
    public string? Bedingung { get; set; }

    public Benachrichtigung Benachrichtigung { get; set; }

    /// <summary>Feld, das beim Erledigen ausgefüllt werden muss (z. B. „Mandanten-ID").</summary>
    public string? Pflichtfeld { get; set; }

    public AutoAbhaken AutoAbhaken { get; set; }
}

/// <summary>Benachrichtigung, wenn der Schritt in den Pool des Bereichs kommt (je Schritt wählbar, Entscheidung 28.10.2026).</summary>
public enum Benachrichtigung
{
    Keine = 0,
    Mail = 1,
    Teams = 2,
}

/// <summary>Wann das System den Schritt selbst abhakt.</summary>
public enum AutoAbhaken
{
    Nie = 0,
    /// <summary>Der Odoo-Import findet die Rechnung zum Auftrag (Übergangszeit).</summary>
    OdooRechnung = 1,
    /// <summary>Der Vertrag steht in der Kundenzentrale auf „unterschrieben".</summary>
    VertragUnterschrieben = 2,
}
