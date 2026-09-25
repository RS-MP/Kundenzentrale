namespace Rausoft.Erp.Dtos;

/// <summary>Vorlage mit ihrer aktuellen freigegebenen Version (Übersicht /vorlagen).</summary>
public record VorlageEintrag(
    int Id,
    string Name,
    bool Aktiv,
    int? AktuelleVersion,
    DateTime? FreigegebenAm,
    string? FreigegebenVon,
    string? Notiz,
    int Verwendungen);

/// <summary>Auswahlliste in der Kundenakte: aktuellste freigegebene Version je aktiver Vorlage.</summary>
public record VorlageAuswahl(int VorlagenVersionId, string Name, int Version);

/// <summary>Entwurfsdatei im Vorlagen-Verzeichnis (Root, noch nicht freigegebener Stand).</summary>
public record VorlagenEntwurf(string Dateiname, DateTime GeaendertAm);

/// <summary>
/// Freigabe: Entwurfsdatei als neue Version einfrieren. Entweder zu einer
/// bestehenden Vorlage (VorlageId) oder als neue Vorlage (NeuerName).
/// </summary>
public record VorlageFreigeben(string Dateiname, int? VorlageId, string? NeuerName, string? Notiz);

public record VorlageAktivAenderung(bool Aktiv);

/// <summary>Wer hat welchen Vorlagen-Stand bekommen (Auswertung je Vorlage).</summary>
public record VorlagenVerwendung(
    int Version,
    int VertragId,
    string Vertragsnummer,
    int KundeId,
    string Kundennummer,
    string Firmenname,
    DateTime ErstelltAm);

/// <summary>Versions-Historie einer Vorlage.</summary>
public record VorlagenVersionEintrag(
    int Id,
    int Nummer,
    DateTime FreigegebenAm,
    string? FreigegebenVon,
    string? Notiz,
    int Verwendungen);
