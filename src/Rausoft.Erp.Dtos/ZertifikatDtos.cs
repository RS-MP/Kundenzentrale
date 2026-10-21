namespace Rausoft.Erp.Dtos;

// ---------------------------------------------------------------------------
// SSL-Zertifikate: Verwaltung, Ablauf-Erinnerung per E-Mail, Bestätigung.
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
    // „ok" · „bald" · „abgelaufen" (bezogen auf GueltigBis)
    string Status,
    int TageBisAblauf,
    DateTime? ErneuertAm,
    string? ErneuertVon,
    DateTime? LetzteErinnerungAm,
    int ErinnerungenGesendet,
    string? LetzterMailFehler,
    DateTime? LiveGeprueftAm,
    DateOnly? LiveGueltigBis,
    string? LiveAussteller,
    string? LiveFehler,
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
    bool Aktiv);

/// <summary>Bestätigung „neues Zertifikat ist online" mit dem neuen Ablaufdatum.</summary>
public record ZertifikatErneuern(DateOnly NeuesGueltigBis);

/// <summary>Übersicht: Zertifikate + ob der Benutzer pflegen darf + Hinweis, ob E-Mail-Versand eingerichtet ist.</summary>
public record ZertifikatUebersicht(
    List<ZertifikatEintrag> Zertifikate,
    bool DarfBearbeiten,
    bool MailEingerichtet,
    string? StandardEmpfaenger,
    int ErinnerungIntervallTage,
    List<ServerAuswahl> Server);

public record ServerAuswahl(int Id, string Alias);

public record TestmailSenden(string Empfaenger);

/// <summary>Ergebnis eines manuell angestoßenen Erinnerungslaufs.</summary>
public record ErinnerungsLauf(int Geprueft, int Gesendet, List<string> Fehler);
