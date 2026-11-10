namespace Rausoft.Erp.Dtos;

// Wiki: Kommunikationsleitlinien — Sprachregeln für Vertrieb, Support und Claude.

/// <summary>Eine Sprachregel in der Liste — mit den Rechten des angemeldeten Benutzers und dem Bestätigungsstand.</summary>
public record LeitlinieDto(
    int Id,
    // Verwenden, Vermeiden, Verboten, Pflichthinweis, Grundsatz
    string Art,
    string Aussage,
    string? Stattdessen,
    string? Anlass,
    string? Begruendung,
    bool BegruendungIntern,
    string? BeispielGut,
    string? BeispielSchlecht,
    List<string> Varianten,
    // Schlüssel: vertrieb, support, ki
    List<string> Zielgruppen,
    string Thema,
    string ThemaName,
    string? Produkt,
    string? ProduktName,
    // Schlüssel: email, telefon, angebot, website, chat — leer = alle Kanäle
    List<string> Kanaele,
    string? KiHinweis,
    // Entwurf, Freigegeben, Zurueckgezogen
    string Status,
    DateOnly? GueltigAb,
    int? VorgaengerId,
    DateTime ErstelltAm,
    string? ErstelltVon,
    DateTime? GeaendertAm,
    string? GeaendertVon,
    DateTime? FreigegebenAm,
    string? FreigegebenVon,
    DateTime? ZurueckgezogenAm,
    string? ZurueckgezogenVon,
    // Stand des Datensatzes (RowVersion) — geht beim Ändern und Freigeben zurück an die API,
    // damit niemand einen Wortlaut freigibt, den er so nicht gesehen hat
    string Version,
    bool DarfBearbeiten,
    bool DarfLoeschen,
    // Nur gefüllt, wenn der Benutzer zum Kreis der Bestätigungspflichtigen dieser Regel gehört
    LeitlinieLesung? MeineBestaetigung,
    // Nur für Admin und Compliance: wer hat bestätigt, wer fehlt
    LeitlinieBestaetigungsstand? Bestaetigungsstand);

/// <summary>Lesestand des angemeldeten Benutzers zur Freigabe-Neuerung einer Regel.</summary>
public record LeitlinieLesung(int NeuerungId, DateTime? GesehenAm, DateTime? BestaetigtAm);

/// <summary>Bestätigungsstand einer Regel über alle Betroffenen (Auswertung der Neuerungs-Lesungen).</summary>
public record LeitlinieBestaetigungsstand(int NeuerungId, int Gesamt, int Bestaetigt, List<WikiLesung> Lesungen);

/// <summary>Auswahlwert einer festen Liste mit Trefferzahl in der aktuellen Suche.</summary>
public record LeitlinienAuswahl(string Schluessel, string Name, int Anzahl);

/// <summary>Antwort der Liste: gefilterte Regeln, offene eigene Bestätigungen, Auswahllisten und Rechte.</summary>
public record LeitlinienListe(
    List<LeitlinieDto> Eintraege,
    // Regeln, deren Lesen der Benutzer noch bestätigen muss — unabhängig von Suche und Filtern
    List<LeitlinieDto> ZuBestaetigen,
    bool DarfFreigeben,
    // Zielgruppen, deren Export der Benutzer abrufen darf
    List<string> ExportZielgruppen,
    // Zielgruppe, die zur Rolle des Benutzers passt (Vorauswahl des Umschalters); null = keine eindeutige
    string? MeineZielgruppe,
    List<LeitlinienAuswahl> Themen,
    List<LeitlinienAuswahl> Produkte);

/// <summary>Vorschlagen oder Ändern einer Regel. <c>Version</c> ist beim Ändern Pflicht.</summary>
public record LeitlinieSpeichern(
    string Art,
    string Aussage,
    string? Stattdessen,
    string? Anlass,
    string? Begruendung,
    bool BegruendungIntern,
    string? BeispielGut,
    string? BeispielSchlecht,
    string? Varianten,
    List<string> Zielgruppen,
    string Thema,
    string? Produkt,
    List<string>? Kanaele,
    string? KiHinweis,
    DateOnly? GueltigAb,
    string? Version = null);

/// <summary>Status setzen: Freigegeben (aus Entwurf) oder Zurueckgezogen (aus Freigegeben) — für genau den gesehenen Stand.</summary>
public record LeitlinieStatusSetzen(string Status, string Version);

/// <summary>
/// Strukturierter Export (<c>format=json</c>): dieselben Regeln wie im Prompt-Text,
/// mit derselben Version. Grundlage für einen späteren automatischen Abruf und
/// einen Textprüfer (<see cref="LeitlinienExportRegel.Varianten"/>).
/// </summary>
public record LeitlinienExport(
    string Zielgruppe,
    DateOnly? Stand,
    int Anzahl,
    string Version,
    List<LeitlinienExportRegel> Regeln);

/// <summary>Eine freigegebene Regel im Export — die Begründung fehlt, wenn sie als intern markiert ist.</summary>
public record LeitlinienExportRegel(
    int Id,
    string Art,
    string Thema,
    string ThemaName,
    string? Produkt,
    string? ProduktName,
    List<string> Kanaele,
    string Aussage,
    List<string> Varianten,
    string? Stattdessen,
    string? Anlass,
    string? Begruendung,
    string? BeispielGut,
    string? BeispielSchlecht,
    string? KiHinweis,
    DateOnly? GueltigAb,
    DateTime? FreigegebenAm);
