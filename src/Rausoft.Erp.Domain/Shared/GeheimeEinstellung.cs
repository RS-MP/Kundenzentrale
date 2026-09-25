namespace Rausoft.Erp.Domain.Shared;

/// <summary>
/// Verschlüsselt gespeicherte Einstellung (z. B. der Push-Token des Service
/// Portals, später SMTP-Zugangsdaten). Der Wert wird mit ASP.NET Core Data
/// Protection (Windows: DPAPI-geschützte Schlüssel) verschlüsselt abgelegt
/// und nie im Klartext angezeigt — nur "gesetzt am" ist sichtbar.
/// Diese Entität wird bewusst NICHT auditiert (kein Geheimnis in Logs).
/// </summary>
public class GeheimeEinstellung
{
    public int Id { get; set; }

    public required string Schluessel { get; set; }

    /// <summary>Data-Protection-verschlüsselter Wert (Base64).</summary>
    public required string WertVerschluesselt { get; set; }

    public DateTime GesetztAm { get; set; }
}

public static class GeheimnisSchluessel
{
    public const string ServicePortalPushToken = "ServicePortal.PushToken";
    public const string OdooApiKey = "Odoo.ApiKey";
    public const string SignaturPfxPasswort = "Signatur.PfxPasswort";
}

/// <summary>
/// Gemeinsame Data-Protection-Ablage von Web-App und Odoo-Import-Aufgabe:
/// Der Schlüsselbund liegt maschinenweit unter ProgramData (DPAPI-geschützt,
/// Machine-Scope), damit beide Prozesse — auch unter verschiedenen Konten —
/// dieselben Geheimnisse entschlüsseln können. Zugriff auf den Ordner per
/// NTFS-ACL einschränken (siehe DEPLOYMENT.md).
/// </summary>
public static class GeheimnisAblage
{
    public const string Anwendungsname = "Kundenzentrale";
    public const string Zweck = "Kundenzentrale.GeheimeEinstellungen";

    public static string SchluesselOrdner => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Rausoft", "Kundenzentrale", "Schluessel");
}
