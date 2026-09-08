namespace Rausoft.Erp.Dtos;

public record ServerEintrag(
    int Id,
    string Alias,
    string Url,
    string PruefMandant,
    bool Aktiv,
    bool? IstErreichbar,
    bool? ConnectorAktiv,
    string? SanktionslistenStand,
    string? Fehlertext,
    DateTime? GeprueftAm);

public record ServerAendern(string Alias, string Url, string PruefMandant, bool Aktiv);

public record MeldeBericht(
    DateTime Zeitpunkt,
    List<string> Verarbeitet,
    List<string> Unbekannt,
    List<ServerEintrag> Server);

/// <summary>Geheime Einstellung setzen (write-only — der Wert ist nie wieder abrufbar).</summary>
public record GeheimnisSetzen(string Wert);

public record GeheimnisStatus(string Schluessel, DateTime GesetztAm);
