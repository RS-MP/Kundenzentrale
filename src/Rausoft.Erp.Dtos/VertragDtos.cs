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
    string ErstelltVon);

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

public record VerlaufEintrag(
    int Id,
    string Typ,
    DateTime Zeitpunkt,
    string? Mitarbeiter,
    string? Ansprechpartner,
    string Betreff,
    string? Inhalt);
