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
    public const string PreislisteVerzeichnis = "Preisliste.Verzeichnis";
    public const string ServicePortalUrl = "ServicePortal.Url";
    public const string ServicePortalIntervallMinuten = "ServicePortal.IntervallMinuten";
    public const string ServicePortalLetzterPush = "ServicePortal.LetzterPush";
    public const string OdooUrl = "Odoo.Url";
    public const string OdooDatenbank = "Odoo.Datenbank";
    public const string OdooBenutzer = "Odoo.Benutzer";
    public const string OdooLetzterImport = "Odoo.LetzterImport";

    // E-Mail-Versand (SMTP) — Passwort liegt als Geheimnis (Mail.SmtpPasswort)
    public const string MailSmtpHost = "Mail.SmtpHost";
    public const string MailSmtpPort = "Mail.SmtpPort";
    public const string MailSmtpBenutzer = "Mail.SmtpBenutzer";
    public const string MailSmtpSsl = "Mail.SmtpSsl";
    public const string MailAbsender = "Mail.Absender";

    // SSL-Zertifikate: Erinnerungstakt und Standardempfänger
    public const string ZertifikateErinnerungIntervallTage = "Zertifikate.ErinnerungIntervallTage";
    public const string ZertifikateStandardEmpfaenger = "Zertifikate.StandardEmpfaenger";
    public const string ZertifikateEskalationTage = "Zertifikate.EskalationTage";
    public const string ZertifikateEskalationEmpfaenger = "Zertifikate.EskalationEmpfaenger";
    public const string ZertifikateAbgelaufenEmpfaenger = "Zertifikate.AbgelaufenEmpfaenger";
    public const string ZertifikatePruefstoerungTage = "Zertifikate.PruefstoerungTage";
    public const string ZertifikateWochenberichtTag = "Zertifikate.WochenberichtTag";
    public const string ZertifikateLetzterWochenbericht = "Zertifikate.LetzterWochenbericht";
    public const string ZertifikateServerAutomatisch = "Zertifikate.ServerAutomatisch";
    // Schutz der Live-Prüfung (SICHERHEIT.md N1): nur diese Ports, Statusmails nur an diese Domänen
    public const string ZertifikateErlaubtePorts = "Zertifikate.ErlaubtePorts";
    public const string ZertifikateEmpfaengerDomaenen = "Zertifikate.EmpfaengerDomaenen";

    // Website-Anfragen: lesbare Namen zu den Nummern der Google-Ads-Kampagnen („Nummer = Name; …")
    public const string AnfragenKampagnen = "Anfragen.Kampagnen";
    // Nummern der Odoo-Verkaufschancen, die die Übernahme auslässt (ihre Anfrage wurde hier gelöscht)
    public const string AnfragenOdooUebersprungen = "Anfragen.OdooUebersprungen";

    // Odoo-Testdatenbank (läuft nach 14 Tagen ab, eigener API-Key als Geheimnis OdooTest.ApiKey)
    // und das Ziel schreibender Odoo-Funktionen — gepflegt über Administration → Odoo-Testdatenbank
    public const string OdooTestUrl = "OdooTest.Url";
    public const string OdooTestDatenbank = "OdooTest.Datenbank";
    public const string OdooTestBenutzer = "OdooTest.Benutzer";
    public const string OdooTestGueltigBis = "OdooTest.GueltigBis";
    public const string OdooSchreibZiel = "OdooTest.SchreibZiel";
    public const string OdooTestPraefix = "OdooTest.";
}
