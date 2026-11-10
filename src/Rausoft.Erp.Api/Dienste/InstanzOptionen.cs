namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Kennzeichnung der Instanz (appsettings-Sektion "Instanz") — trennt Test- und
/// Produktivsystem auf demselben Server (DEPLOYMENT.md, Test- und Produktivsystem).
/// Im Testsystem gilt hart, unabhängig von den Einstellungen in der Datenbank:
/// keine Hintergrund-Takte, kein Push ans Service Portal, E-Mails nur an die
/// Umleitungsadresse (oder gar nicht), Ablagen nur unterhalb der Ablage-Wurzel.
/// </summary>
public class InstanzOptionen
{
    public bool Testsystem { get; set; }

    /// <summary>Text im Band des Frontends; leer = „Testsystem".</summary>
    public string Bezeichnung { get; set; } = "";

    /// <summary>Eigener Data-Protection-Schlüsselbund; leer = Standard unter ProgramData. Im Testsystem Pflicht.</summary>
    public string SchluesselOrdner { get; set; } = "";

    /// <summary>Nur Testsystem, Pflicht: alle Ablage-Verzeichnisse müssen unterhalb dieses Ordners liegen.</summary>
    public string AblageWurzel { get; set; } = "";

    /// <summary>Nur Testsystem: alle E-Mails gehen ausschließlich an diese Adresse; leer = es wird nichts versendet.</summary>
    public string MailUmleitung { get; set; } = "";

    /// <summary>Bricht den Start ab, wenn ein Testsystem nicht vollständig abgegrenzt ist (lieber gar nicht starten).</summary>
    public void Pruefen()
    {
        if (!Testsystem)
            return;
        if (string.IsNullOrWhiteSpace(SchluesselOrdner))
            throw new InvalidOperationException(
                "Instanz:Testsystem = true verlangt Instanz:SchluesselOrdner (eigener Schlüsselbund, getrennt vom Produktivsystem).");
        if (string.IsNullOrWhiteSpace(AblageWurzel))
            throw new InvalidOperationException(
                "Instanz:Testsystem = true verlangt Instanz:AblageWurzel (Ordner für alle Test-Ablagen, nie das Kunden Archiv).");
    }
}
