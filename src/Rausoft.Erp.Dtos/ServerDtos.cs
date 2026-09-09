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
    DateTime? GeprueftAm,
    string? ReleaseStand,
    int KundenAnzahl);

public record ServerKunde(int Id, string Kundennummer, string Firmenname, string? MandantenKennung, string Kategorie);

/// <summary>
/// Ampel-Logik eines Servers — eine Stelle für API und Frontend, damit
/// Kundenliste, Kundenakte und Server-Übersicht identisch urteilen.
/// </summary>
public static class ServerAmpel
{
    /// <summary>"gut" | "warnung" | "kritisch" | null (noch nie geprüft).</summary>
    public static string? Berechne(bool? istErreichbar, bool? connectorAktiv) =>
        istErreichbar is null ? null
        : istErreichbar == false ? "kritisch"
        : connectorAktiv == false ? "warnung"
        : "gut";
}

/// <summary>Server-Zuordnung eines Kunden ändern (null = keinem Server zugeordnet).</summary>
public record KundeServerZuordnung(int? ServerId, string? MandantenKennung);

public record ServerAendern(string Alias, string Url, string PruefMandant, bool Aktiv);

public record MeldeBericht(
    DateTime Zeitpunkt,
    List<string> Verarbeitet,
    List<string> Unbekannt,
    List<ServerEintrag> Server);

/// <summary>Geheime Einstellung setzen (write-only — der Wert ist nie wieder abrufbar).</summary>
public record GeheimnisSetzen(string Wert);

public record GeheimnisStatus(string Schluessel, DateTime GesetztAm);
