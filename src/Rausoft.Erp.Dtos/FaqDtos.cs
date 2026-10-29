namespace Rausoft.Erp.Dtos;

// Wiki: FAQ-Datenbank für den Support (Symptom → Ursache → Lösung).

/// <summary>Ein Supportfall in Liste und Detailansicht — mit den Rechten des angemeldeten Benutzers daran.</summary>
public record FaqEintragDto(
    int Id,
    string Titel,
    string Produkt,
    string ProduktName,
    string Kategorie,
    string KategorieName,
    string Symptom,
    string? Ursache,
    string Loesung,
    List<string> Schlagwoerter,
    string Status,
    int? KundeId,
    string? KundeName,
    int Vorkommen,
    DateTime? ZuletztVorgekommenAm,
    DateTime ErstelltAm,
    string? ErstelltVon,
    DateTime? GeaendertAm,
    string? GeaendertVon,
    bool DarfBearbeiten,
    bool DarfLoeschen);

/// <summary>Auswahlwert einer festen Liste (Produkt, Kategorie) mit Trefferzahl in der aktuellen Suche.</summary>
public record FaqAuswahl(string Schluessel, string Name, int Anzahl);

/// <summary>Antwort der Liste: gefilterte Einträge plus Auswahllisten und das Freigaberecht des Benutzers.</summary>
public record FaqListe(
    List<FaqEintragDto> Eintraege,
    int Gesamt,
    bool DarfFreigeben,
    List<FaqAuswahl> Produkte,
    List<FaqAuswahl> Kategorien);

/// <summary>Anlegen oder Ändern eines Falls. <c>SofortFreigeben</c> wirkt nur, wenn der Benutzer freigeben darf.</summary>
public record FaqSpeichern(
    string Titel,
    string Produkt,
    string Kategorie,
    string Symptom,
    string? Ursache,
    string Loesung,
    string? Schlagwoerter,
    int? KundeId,
    bool SofortFreigeben = false);

/// <summary>Status setzen: Entwurf, Freigegeben oder Veraltet.</summary>
public record FaqStatusSetzen(string Status);
