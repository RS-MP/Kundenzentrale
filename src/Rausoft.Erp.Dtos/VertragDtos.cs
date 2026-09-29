namespace Rausoft.Erp.Dtos;

public record VertragListenEintrag(
    int Id,
    string Vertragsnummer,
    // Vertragspartei: Kunde ODER Partner (Partnerverträge) — KundeName/Kundennummer tragen dann den Partner
    int? KundeId,
    string KundeName,
    string Kundennummer,
    string Status,
    DateOnly Beginn,
    DateOnly? NaechstesEnde,
    List<BestandteilKurz> Bestandteile,
    // Jüngstes Dokument der Akte (FEEDBACK Nr. 18), z. B. „Bestellung 3 · 08.09.2026".
    StandEintrag? AktuellerStand,
    int? PartnerId,
    // Für alle sichtbare Kurzinfos der Bestandteile (z. B. „Provision 15 %")
    List<string> Kurzinfos);

/// <summary>Dokumentart-Kürzel für Listen (FEEDBACK Nr. 19): „BE" · „Bestellungen" · 5.</summary>
public record BestandteilKurz(string Kuerzel, string Name, int Anzahl);

public record StandEintrag(string Kuerzel, string Bezeichnung, DateOnly Datum);

public record VertragDetail(
    int Id,
    string Vertragsnummer,
    int? KundeId,
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
    List<BestandteilDetail> Bestandteile,
    StandEintrag? AktuellerStand,
    // Angemeldeter Benutzer hat die Rolle „Vertrauliche Verträge"
    bool DarfVertrauliche,
    int? PartnerId);

public record BestandteilDetail(
    int Id,
    string Typ,
    string Kuerzel,
    string Titel,
    string? Beschreibung,
    DateOnly? UnterzeichnetAm,
    DateOnly? Dokumentdatum,
    // Herkunft des Dokumentdatums: „Dateiname", „Dateidatum", „Erzeugt", „Manuell".
    string? DokumentdatumQuelle,
    // Laufende Nummer innerhalb der Dokumentart (nur bei mehreren), z. B. Bestellung 3.
    int? LaufendeNummer,
    bool IstAktuellster,
    // Vertraulich (FEEDBACK Nr. 20): Kurzinfo sehen alle; Gesperrt = Titel/Beschreibung/Dokumente ausgeblendet
    bool Vertraulich,
    bool Gesperrt,
    string? Kurzinfo,
    List<DokumentEintrag> Dokumente);

/// <summary>Bestandteil korrigieren — z. B. falsch erkanntes Dokumentdatum (Quelle wird „Manuell").</summary>
public record BestandteilAendern(
    string Typ,
    string Titel,
    string? Beschreibung,
    DateOnly? UnterzeichnetAm,
    DateOnly? Dokumentdatum,
    bool Vertraulich = false,
    string? Kurzinfo = null);

public record DokumentdatenNachgetragen(int Geprueft, int AusDateiname, int AusDateidatum, int OhneDatei);

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
    int? KundeId,
    DateOnly Beginn,
    int? MindestlaufzeitMonate,
    int? VerlaengerungMonate,
    int? KuendigungsfristMonate,
    string? Notiz,
    // Statt KundeId: Partnervertrag
    int? PartnerId = null);

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
    DateOnly? UnterzeichnetAm,
    bool Vertraulich = false,
    string? Kurzinfo = null);

/// <summary>Registriert eine Dokumentversion: Pfad relativ zum Vertragsablage-Verzeichnis.</summary>
public record DokumentRegistrieren(string RelativerPfad);

/// <summary>Vertrag aus einer Word-Vorlage erzeugen (Dateiname aus dem Vorlagen-Verzeichnis).</summary>
public record VertragAusVorlage(
    int KundeId,
    int VorlagenVersionId,
    // Angebot bzw. Abo für „Angebot S0…" im Vertrag; leer = neuestes offenes Angebot, sonst laufendes Abo; 0 = ohne
    int? AboId = null);

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
