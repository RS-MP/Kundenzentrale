using Rausoft.Erp.Domain.Shared;

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

    /// <summary>Dokumentart des erzeugten Vertragsbestandteils (z. B. NDA).</summary>
    public BestandteilTyp Typ { get; set; } = BestandteilTyp.Lizenzvertrag;

    /// <summary>
    /// Nutzungsfreigabe: welche Rollen die Vorlage in der Kundenakte verwenden
    /// dürfen. Keine = alle Mitarbeiter; Compliance und Admin dürfen immer.
    /// </summary>
    public MitarbeiterRolle ErlaubteRollen { get; set; }

    public bool ErlaubtFuer(MitarbeiterRolle rollen) =>
        ErlaubteRollen == MitarbeiterRolle.Keine
        || (rollen & (ErlaubteRollen | MitarbeiterRolle.Compliance | MitarbeiterRolle.Admin)) != 0;

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

    /// <summary>
    /// Ab wann die Version in der Kundenakte gilt — bis dahin bleibt die
    /// Vorversion die aktuelle (Freigabe vorab zu einem Stichtag).
    /// </summary>
    public DateOnly GueltigAb { get; set; }

    public int? FreigegebenVonId { get; set; }
    public Mitarbeiter? FreigegebenVon { get; set; }

    /// <summary>Name der Entwurfsdatei im Vorlagen-Verzeichnis, aus der freigegeben wurde —
    /// ordnet spätere Änderungen derselben Datei der Vorlage zu („geändert seit Freigabe").</summary>
    public string? QuellDateiname { get; set; }

    /// <summary>Änderungsnotiz: was ist neu gegenüber der Vorversion.</summary>
    public string? Notiz { get; set; }
}
