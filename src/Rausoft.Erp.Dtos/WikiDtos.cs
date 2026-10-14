namespace Rausoft.Erp.Dtos;

/// <summary>Sanktionsliste im Wiki-Katalog (Anzeige, gruppiert nach Region).</summary>
public record SanktionslisteEintrag(
    int Id,
    string Region,
    string RegionName,
    string? Kuerzel,
    string Name,
    string? BezeichnungIntern,
    string? Aliase,
    string? Beschreibung,
    string? Art,
    string? Herausgeber,
    string? Geltungsbereich,
    bool Standardumfang,
    string? DownloadUrl,
    // „Offen", „Automatisch", „Manuell", „NichtImEinsatz"
    string Import,
    string? ImportHinweis,
    int? VerantwortlichId,
    string? Verantwortlich,
    int? VertreterId,
    string? Vertreter,
    string? Prozess,
    bool Aktiv,
    DateTime? GeaendertAm,
    string? GeaendertVon);

/// <summary>Katalog + ob der Benutzer pflegen darf (Compliance/Admin).</summary>
public record SanktionslistenKatalog(
    List<SanktionslisteEintrag> Listen,
    bool DarfBearbeiten,
    // Auswahl für Region (Code + Name, in Anzeigereihenfolge)
    List<RegionEintrag> Regionen,
    // Standardprozess je Importart — gilt für Listen ohne eigenen Prozess
    List<StandardprozessEintrag> Standardprozesse);

public record StandardprozessEintrag(string Import, string? Text, DateTime? GeaendertAm, string? GeaendertVon);

public record StandardprozessSpeichern(string? Text);

public record RegionEintrag(string Code, string Name);

/// <summary>Anlegen/Ändern einer Sanktionsliste.</summary>
public record SanktionslisteSpeichern(
    string Region,
    string? Kuerzel,
    string Name,
    string? BezeichnungIntern,
    string? Aliase,
    string? Beschreibung,
    string? Art,
    string? Herausgeber,
    string? Geltungsbereich,
    bool Standardumfang,
    string? DownloadUrl,
    string Import,
    string? ImportHinweis,
    int? VerantwortlichId,
    int? VertreterId,
    string? Prozess,
    bool Aktiv);

/// <summary>Auswahl für Verantwortlich/Vertreter (aktive Benutzer).</summary>
public record PersonAuswahl(int Id, string Name);
