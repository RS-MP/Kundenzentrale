namespace Rausoft.Erp.Web.Komponenten;

/// <summary>Dokumentarten für Auswahllisten — Reihenfolge wie die Gruppierung in der Vertragsakte (API: Dienste/Dokumentarten).</summary>
public static class Dokumentarten
{
    public static readonly string[] Namen =
    [
        "Wartungsvertrag", "Lizenzvertrag", "AVV", "NDA", "Sondervereinbarung", "Nachtrag",
        "Bestellung", "Installationsprotokoll", "Kündigung", "Sonstiges",
    ];

    /// <summary>Anzeige der Datumsherkunft — „geschätzt", wenn nur das Datei-Änderungsdatum vorlag.</summary>
    public static string? QuelleHinweis(string? quelle) => quelle switch
    {
        "Dateiname" => "Datum aus dem Dateinamen",
        "Dateidatum" => "Datum geschätzt aus dem Datei-Änderungsdatum — bei Bedarf korrigieren",
        "Erzeugt" => "Aus Vorlage erzeugt",
        "Manuell" => "Datum manuell gepflegt",
        _ => null,
    };

    public static string Mehrzahl(string art) => art switch
    {
        "Wartungsvertrag" => "Wartungsverträge",
        "Lizenzvertrag" => "Lizenzverträge",
        "Sondervereinbarung" => "Sondervereinbarungen",
        "Nachtrag" => "Nachträge",
        "Bestellung" => "Bestellungen",
        "Installationsprotokoll" => "Installationsprotokolle",
        "Kündigung" => "Kündigungen",
        _ => art,
    };
}
