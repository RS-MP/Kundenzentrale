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
}
