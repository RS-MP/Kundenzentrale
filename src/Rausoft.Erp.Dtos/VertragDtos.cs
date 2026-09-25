namespace Rausoft.Erp.Dtos;

public record VertragListenEintrag(
    int Id,
    string Vertragsnummer,
    int KundeId,
    string KundeName,
    string Kundennummer,
    string Status,
    DateOnly Beginn,
    DateOnly? NaechstesEnde,
    List<string> Bestandteile);

public record VertragDetail(
    int Id,
    string Vertragsnummer,
    int KundeId,
    string KundeName,
    string Kundennummer,
    string Status,
    DateOnly Beginn,
    int? MindestlaufzeitMonate,
    int? VerlaengerungMonate,
    int? KuendigungsfristMonate,
    DateOnly? GekuendigtZum,
    DateOnly? NaechstesEnde,
    string? Notiz,
    List<BestandteilDetail> Bestandteile);

public record BestandteilDetail(
    int Id,
    string Typ,
    string Titel,
    string? Beschreibung,
    DateOnly? UnterzeichnetAm,
    List<DokumentEintrag> Dokumente);

public record DokumentEintrag(
    int Id,
    int Version,
    string Dateipfad,
    string DateinameOriginal,
    string Sha256,
    DateTime ErstelltAm,
    string ErstelltVon,
    string? OrdnerPfad,
    string? VorlagenStand);

public record VertragAnlegen(
    int KundeId,
    DateOnly Beginn,
    int? MindestlaufzeitMonate,
    int? VerlaengerungMonate,
    int? KuendigungsfristMonate,
    string? Notiz);

public record VertragAendern(
    string Status,
    DateOnly Beginn,
    int? MindestlaufzeitMonate,
    int? VerlaengerungMonate,
    int? KuendigungsfristMonate,
    DateOnly? GekuendigtZum,
    string? Notiz);

public record BestandteilAnlegen(
    string Typ,
    string Titel,
    string? Beschreibung,
    DateOnly? UnterzeichnetAm);

/// <summary>Registriert eine Dokumentversion: Pfad relativ zum Vertragsablage-Verzeichnis.</summary>
public record DokumentRegistrieren(string RelativerPfad);

/// <summary>Vertrag aus einer Word-Vorlage erzeugen (Dateiname aus dem Vorlagen-Verzeichnis).</summary>
public record VertragAusVorlage(int KundeId, int VorlagenVersionId);

public record VorlagenErgebnis(
    int VertragId,
    string Vertragsnummer,
    string Datei,
    List<string> OffenePlatzhalter);

/// <summary>Ergebnis der Admin-Löschung: Dateien bleiben im Ablageverzeichnis liegen.</summary>
public record VertragGeloescht(string Vertragsnummer, List<string> VerbleibendeDateien);

public record VerlaufEintrag(
    int Id,
    string Typ,
    DateTime Zeitpunkt,
    string? Mitarbeiter,
    string? Ansprechpartner,
    string Betreff,
    string? Inhalt,
    string? VerweisTyp,
    int? VerweisId);

public record AboEintrag(
    int Id,
    string Nummer,
    string Status,
    int IntervallMonate,
    DateOnly? LaufzeitBeginn,
    DateOnly? NaechsteVerlaengerung,
    List<AboPositionEintrag> Positionen);

public record AboPositionEintrag(string Produkt, decimal Menge);

public record VertragsKennzahlen(
    int Gesamt,
    int LaufzeitFehlt,
    int EndeIn90Tagen,
    int Gekuendigt);
