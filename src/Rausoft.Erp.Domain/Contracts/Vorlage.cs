namespace Rausoft.Erp.Domain.Contracts;

/// <summary>
/// Vertragsvorlage mit Freigabe-Versionierung (FEEDBACK Nr. 17, Kern):
/// Entwurfsdateien liegen im Vorlagen-Verzeichnis (Root); die Freigabe durch
/// Compliance friert eine Kopie im Unterordner "Freigegeben" als nummerierte
/// Version ein. Erzeugte Verträge referenzieren die Version — so ist immer
/// nachvollziehbar, welcher Kunde welchen Vorlagen-Stand bekommen hat.
/// </summary>
public class Vorlage
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Zurückgezogene Vorlagen erscheinen nicht mehr in der Auswahl; Historie bleibt.</summary>
    public bool Aktiv { get; set; } = true;

    public List<VorlagenVersion> Versionen { get; set; } = [];
}

public class VorlagenVersion
{
    public int Id { get; set; }

    public int VorlageId { get; set; }
    public Vorlage? Vorlage { get; set; }

    /// <summary>Fortlaufend je Vorlage: v1, v2, …</summary>
    public int Nummer { get; set; }

    /// <summary>Eingefrorene Kopie, relativ zum Vorlagen-Verzeichnis (Unterordner "Freigegeben").</summary>
    public required string Dateipfad { get; set; }

    /// <summary>SHA-256 der eingefrorenen Datei — nachträgliche Änderungen fallen auf.</summary>
    public required string Sha256 { get; set; }

    public DateTime FreigegebenAm { get; set; }

    public int? FreigegebenVonId { get; set; }
    public Domain.Shared.Mitarbeiter? FreigegebenVon { get; set; }

    /// <summary>Änderungsnotiz: was ist neu gegenüber der Vorversion.</summary>
    public string? Notiz { get; set; }
}
