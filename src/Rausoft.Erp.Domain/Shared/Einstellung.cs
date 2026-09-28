namespace Rausoft.Erp.Domain.Shared;

/// <summary>
/// Systemweite Einstellung (Schlüssel/Wert), pflegbar im Admin-Bereich.
/// Hier liegen z. B. die Ablage-Verzeichnisse — bewusst in der DB statt in
/// appsettings.json, damit sie ohne Deployment anpassbar sind.
/// Keine Geheimnisse hier ablegen (dafür gilt die Verschlüsselungs-Regel).
/// </summary>
public class Einstellung
{
    public int Id { get; set; }

    /// <summary>Eindeutiger Schlüssel, siehe <see cref="EinstellungsSchluessel"/>.</summary>
    public required string Schluessel { get; set; }

    public string Wert { get; set; } = string.Empty;

    /// <summary>Erklärung für den Admin-Bereich.</summary>
    public string? Beschreibung { get; set; }
}

/// <summary>Bekannte Einstellungs-Schlüssel (zentral, damit nichts doppelt entsteht).</summary>
public static class EinstellungsSchluessel
{
    public const string VertragsablageVerzeichnis = "Vertragsablage.Verzeichnis";
    public const string DokumentablageVerzeichnis = "Dokumentablage.Verzeichnis";
    public const string VorlagenVerzeichnis = "Vorlagen.Verzeichnis";
    public const string VertragserzeugungZielverzeichnis = "Vertragserzeugung.Zielverzeichnis";
    public const string VertragserzeugungSignaturZertifikat = "Vertragserzeugung.SignaturZertifikat";
    public const string VertraulicheAblageVerzeichnis = "VertraulicheAblage.Verzeichnis";
    public const string ServicePortalUrl = "ServicePortal.Url";
    public const string ServicePortalIntervallMinuten = "ServicePortal.IntervallMinuten";
    public const string ServicePortalLetzterPush = "ServicePortal.LetzterPush";
    public const string OdooUrl = "Odoo.Url";
    public const string OdooDatenbank = "Odoo.Datenbank";
    public const string OdooBenutzer = "Odoo.Benutzer";
    public const string OdooLetzterImport = "Odoo.LetzterImport";
}
