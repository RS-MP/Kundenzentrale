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
    // Zuletzt gewählte Phase, Aktivitätsart, Zuweisung, Quelle und Medium beim Anlegen in Odoo (als Namen)
    public const string AnfragenOdooVorbelegung = "Anfragen.OdooVorbelegung";

    // Odoo-Testdatenbank (läuft nach 14 Tagen ab, eigener API-Key als Geheimnis OdooTest.ApiKey)
    // und das Ziel schreibender Odoo-Funktionen — gepflegt über Administration → Odoo-Testdatenbank
    public const string OdooTestUrl = "OdooTest.Url";
    public const string OdooTestDatenbank = "OdooTest.Datenbank";
    public const string OdooTestBenutzer = "OdooTest.Benutzer";
    public const string OdooTestGueltigBis = "OdooTest.GueltigBis";
    public const string OdooSchreibZiel = "OdooTest.SchreibZiel";
    public const string OdooTestPraefix = "OdooTest.";

    // Offene Angebote und Aufträge aus Odoo: Nachfass-Aufgaben und Wochenübersicht per Mail
    public const string VerkaufNachfassTage = "Verkauf.NachfassTage";
    public const string VerkaufNachfassWiederholungTage = "Verkauf.NachfassWiederholungTage";
    public const string VerkaufRueckblickTage = "Verkauf.RueckblickTage";
    public const string VerkaufBerichtEmpfaenger = "Verkauf.BerichtEmpfaenger";
    public const string VerkaufBerichtTag = "Verkauf.BerichtTag";
    public const string VerkaufLetzterBericht = "Verkauf.LetzterBericht";

    // Outlook-Anbindung: App-Registrierung in Entra ID (öffentlicher Client, keine Geheimnisse)
    public const string OutlookTenantId = "Outlook.TenantId";
    public const string OutlookClientId = "Outlook.ClientId";
    /// <summary>
    /// „Ja“ = Mails aus der Akte gehen an den Kunden. Alles andere (Standard „Nein“) = Probebetrieb:
    /// Jede Mail geht an den Mitarbeiter selbst. So lässt sich auf dem Produktivsystem testen, ohne
    /// dass ein Kunde Post bekommt (MP 26.11.2026).
    /// </summary>
    public const string OutlookKundenmailsFreigegeben = "Outlook.KundenmailsFreigegeben";

    // Kontoauszüge: Eingangsordner, Buchen in Odoo, Meldung offener Posten
    public const string ZahlungenEingangVerzeichnis = "Zahlungen.EingangVerzeichnis";
    public const string ZahlungenAutomatischBuchen = "Zahlungen.AutomatischBuchen";
    public const string ZahlungenOdooJournal = "Zahlungen.OdooJournal";
    public const string ZahlungenEmpfaenger = "Zahlungen.Empfaenger";
    public const string ZahlungenBerichtTag = "Zahlungen.BerichtTag";
    public const string ZahlungenLetzterBericht = "Zahlungen.LetzterBericht";
    public const string ZahlungenLetzterLauf = "Zahlungen.LetzterLauf";
}
