namespace Rausoft.Erp.Dtos;

// ---------------------------------------------------------------------------
// Kundenmail aus der Akte (ANFORDERUNGEN.md, „E-Mail aus der Akte“): Die
// Kundenzentrale stellt die Mail zusammen; ins Postfach kommt sie über den Browser
// des Mitarbeiters (Microsoft Graph) — als Entwurf zum Öffnen oder direkt gesendet.
// ---------------------------------------------------------------------------

/// <summary>Was das Fenster „E-Mail“ zum Kunden anbietet.</summary>
/// <param name="Umgeleitet">Testsystem oder Probebetrieb: Mails gehen nicht an den Kunden.</param>
/// <param name="Umleitung">Dorthin gehen sie stattdessen; leer = es wird nichts angelegt.</param>
/// <param name="UmleitungHinweis">Erklärung für den Mitarbeiter, oben im Fenster.</param>
public record MailAuswahl(
    int KundeId,
    string Kunde,
    List<MailAnsprechpartner> Ansprechpartner,
    List<MailBaustein> Bausteine,
    bool HatSignatur,
    bool Umgeleitet,
    string? Umleitung,
    string? UmleitungHinweis);

public record MailAnsprechpartner(int Id, string Name, string Email);

public record MailBaustein(int Id, string Name, string? Verwendung, int Anhaenge);

/// <param name="An">Eingabe des Mitarbeiters; mehrere Adressen mit Semikolon oder Komma.</param>
/// <param name="Freitext">Eigener Text (Klartext), steht über dem Baustein.</param>
public record MailZusammenstellen(
    int KundeId, int? AnsprechpartnerId, int? TextbausteinId, string? An, string? Cc, string? Betreff, string? Freitext, bool MitSignatur);

/// <summary>Die fertige Mail, so wie sie ins Postfach geht.</summary>
/// <param name="An">Wirkliche Empfänger — im Testsystem die Umleitungsadresse.</param>
/// <param name="OffenePlatzhalter">Solange hier etwas steht, lässt sich die Mail nicht direkt senden.</param>
/// <param name="Hinweise">Ungültige Adressen, Umleitung im Testsystem, fehlende Signatur.</param>
public record MailFertig(
    List<string> An, List<string> Cc, string Betreff, string Html,
    List<string> OffenePlatzhalter, List<MailAnhang> Anhaenge, List<string> Hinweise);

public record MailAnhang(int Id, string Dateiname, long Groesse);

/// <summary>Meldung des Browsers: Die Mail liegt jetzt im Postfach (als Entwurf oder gesendet).</summary>
public record MailAngelegt(
    int KundeId, int? AnsprechpartnerId, int? TextbausteinId, List<string> An, List<string>? Cc, string Betreff, string Html,
    List<string>? Anhaenge, string GraphId, string? NachrichtenKennung, bool Gesendet);

/// <summary>Was aus einem Entwurf geworden ist — mit der wirklich gesendeten Fassung.</summary>
/// <param name="Stand">„gesendet“, „geloescht“ oder „entwurf“ (unverändert).</param>
public record MailStand(
    string Stand, DateTime? GesendetAm, string? Betreff, string? Html, List<string>? An, List<string>? Cc, string? NachrichtenKennung);

/// <summary>Eigener Entwurf, der noch nicht als gesendet bekannt ist.</summary>
public record MailOffen(int Id, string GraphId, int KundeId, string Kunde, string Betreff, string An, DateTime AngelegtAm);

public record MailSignatur(string Html);

/// <param name="Hinweis">Warum nichts gefunden wurde; leer bei Erfolg.</param>
public record MailSignaturAusOdoo(string? Html, string? Hinweis);
