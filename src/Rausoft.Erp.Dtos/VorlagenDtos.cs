namespace Rausoft.Erp.Dtos;

/// <summary>Vorlage mit ihrer aktuell gültigen Version (Übersicht /vorlagen).</summary>
public record VorlageEintrag(
    int Id,
    string Name,
    bool Aktiv,
    // Dokumentart des erzeugten Bestandteils (z. B. „NDA") + Kürzel
    string Typ,
    string Kuerzel,
    // Rollen, die die Vorlage verwenden dürfen — leer = alle Mitarbeiter
    List<string> ErlaubteRollen,
    int? AktuelleVersion,
    DateTime? FreigegebenAm,
    string? FreigegebenVon,
    string? Notiz,
    // Bereits freigegebene, aber erst künftig gültige Version (Stichtag)
    int? KommendeVersion,
    DateOnly? KommendeGueltigAb,
    int Verwendungen);

/// <summary>Auswahlliste in der Kundenakte: aktuell gültige Version je aktiver, für die eigene Rolle freigegebener Vorlage.</summary>
public record VorlageAuswahl(int VorlagenVersionId, string Name, int Version, string Kuerzel, DateOnly GueltigAb);

/// <summary>Entwurfsdatei im Vorlagen-Verzeichnis (Root, noch nicht freigegebener Stand).</summary>
public record VorlagenEntwurf(string Dateiname, DateTime GeaendertAm);

/// <summary>
/// Freigabe: Entwurfsdatei als neue Version einfrieren. Entweder zu einer
/// bestehenden Vorlage (VorlageId) oder als neue Vorlage (NeuerName + Typ).
/// GueltigAb leer = sofort.
/// </summary>
public record VorlageFreigeben(
    string Dateiname, int? VorlageId, string? NeuerName, string? Notiz,
    string? Typ = null, DateOnly? GueltigAb = null);

public record VorlageAktivAenderung(bool Aktiv);

/// <summary>Dokumentart und Nutzungsfreigabe einer Vorlage ändern (Compliance).</summary>
public record VorlageEinstellungen(string Typ, List<string> ErlaubteRollen);

/// <summary>Wer hat welchen Vorlagen-Stand bekommen (Auswertung je Vorlage).</summary>
public record VorlagenVerwendung(
    int Version,
    int VertragId,
    string Vertragsnummer,
    int? KundeId,
    string Kundennummer,
    string Firmenname,
    DateTime ErstelltAm);

/// <summary>Versions-Historie einer Vorlage.</summary>
public record VorlagenVersionEintrag(
    int Id,
    int Nummer,
    DateTime FreigegebenAm,
    DateOnly GueltigAb,
    string? FreigegebenVon,
    string? Notiz,
    int Verwendungen);

/// <summary>
/// Datei im Vorlagen-Verzeichnis mit Zuordnung (Dateitabelle /vorlagen).
/// Status: „neu" (keiner Vorlage zugeordnet), „geaendert" (zur Vorlage gehörig,
/// Inhalt weicht von allen freigegebenen Versionen ab), „aktiv" (= gültige
/// Version), „zukuenftig" (= freigegeben, gilt erst ab Stichtag), „ersetzt"
/// (= ältere Version), „zurueckgezogen" (Vorlage zurückgezogen).
/// </summary>
public record VorlagenDatei(
    string Dateiname,
    DateTime GeaendertAm,
    string Status,
    int? VorlageId,
    string? VorlageName,
    // Version mit identischem Inhalt (SHA-256), falls vorhanden
    int? Version,
    // Unbekannte {{Platzhalter}} — würden im Vertrag stehen bleiben
    List<string> UnbekanntePlatzhalter,
    // Datei nicht lesbar (z. B. geöffnet/gesperrt oder kein gültiges Word-Dokument)
    string? Fehler,
    // Inhaltsgleiche andere Dateien im Verzeichnis (Duplikate)
    List<string>? GleicherInhaltWie = null);

/// <summary>Prüfung der eingefrorenen Versionsdateien (Unterordner „Freigegeben").</summary>
public record EingefroreneVersion(
    int VorlageId,
    string VorlageName,
    int Version,
    string Dateipfad,
    // „ok", „fehlt" oder „veraendert"
    string Zustand);

public record VorlagenDateiUebersicht(
    List<VorlagenDatei> Dateien,
    List<EingefroreneVersion> Eingefroren,
    List<string> BekanntePlatzhalter);
