namespace Rausoft.Erp.Dtos;

// ---------------------------------------------------------------------------
// SSL-Zertifikate: Verwaltung, Live-Prüfung, Ablauf-Erinnerung per E-Mail mit
// Eskalation, automatische Erkennung der Erneuerung, Verlauf.
// ---------------------------------------------------------------------------

public record ZertifikatEintrag(
    int Id,
    string Bezeichnung,
    string? Hostname,
    int Port,
    int? ServerId,
    string? ServerAlias,
    string Anbieter,
    int LaufzeitMonate,
    DateOnly GueltigBis,
    int VorlaufTage,
    string? Empfaenger,
    string? Notiz,
    bool Aktiv,
    bool AutoErkennung,
    bool AutomatischAngelegt,
    // „ok" · „bald" · „abgelaufen" (bezogen auf GueltigBis)
    string Status,
    // „keine" · „erinnerung" · „eskalation" · „abgelaufen"
    string Stufe,
    int TageBisAblauf,
    DateTime? ErneuertAm,
    // null = automatisch erkannt
    string? ErneuertVon,
    DateTime? LetzteErinnerungAm,
    int ErinnerungenGesendet,
    string? LetzterMailFehler,
    DateTime? LiveGeprueftAm,
    DateOnly? LiveGueltigBis,
    string? LiveAussteller,
    string? LiveFehler,
    DateTime? LiveFehlerSeit,
    string? LiveProtokoll,
    string? LiveWarnungen,
    // Vorschlag für „neues Zertifikat ist online"
    DateOnly VorschlagNeuesGueltigBis);

public record ZertifikatSpeichern(
    string Bezeichnung,
    string? Hostname,
    int Port,
    int? ServerId,
    string Anbieter,
    int LaufzeitMonate,
    DateOnly GueltigBis,
    int VorlaufTage,
    string? Empfaenger,
    string? Notiz,
    bool Aktiv,
    bool AutoErkennung);

/// <summary>Bestätigung „neues Zertifikat ist online" mit dem neuen Ablaufdatum.</summary>
public record ZertifikatErneuern(DateOnly NeuesGueltigBis);

public record ZertifikatEreignisEintrag(int Id, DateTime Zeitpunkt, string Art, string Text, string? Von);

/// <summary>Übersicht: Zertifikate + ob der Benutzer pflegen darf + Einstellungen, die die Seite erklärt.</summary>
public record ZertifikatUebersicht(
    List<ZertifikatEintrag> Zertifikate,
    bool DarfBearbeiten,
    bool MailEingerichtet,
    string? StandardEmpfaenger,
    string? EskalationEmpfaenger,
    string? AbgelaufenEmpfaenger,
    int ErinnerungIntervallTage,
    int EskalationTage,
    bool ServerAutomatisch,
    List<ServerAuswahl> Server,
    // Schutzregeln der Live-Prüfung (SICHERHEIT.md N1), für Hinweise im Formular
    List<int> ErlaubtePorts,          // Ports, zu denen die Live-Prüfung verbinden darf
    List<string> EmpfaengerDomaenen); // Domänen, an die Statusmails gehen dürfen; leer = keine Einschränkung

public record ServerAuswahl(int Id, string Alias);

public record TestmailSenden(string Empfaenger);

/// <summary>Ergebnis eines manuell angestoßenen Erinnerungslaufs.</summary>
public record ErinnerungsLauf(int Geprueft, int Gesendet, List<string> Fehler);

/// <summary>Ergebnis „Zertifikate aus der Serverliste anlegen".</summary>
public record ServerZertifikateErgebnis(int Angelegt, List<string> Hostnamen);

/// <summary>Für Abzeichen in Navigation und Server-Übersicht: was braucht Aufmerksamkeit?</summary>
public record ZertifikatHinweise(int Bald, int Abgelaufen, int Pruefstoerungen, int Warnungen);
