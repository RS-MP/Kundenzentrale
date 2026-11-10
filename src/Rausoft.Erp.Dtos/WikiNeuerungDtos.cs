namespace Rausoft.Erp.Dtos;

// ---------------------------------------------------------------------------
// Wiki: Neuerungen (mit Lese-/Bestätigungsprotokoll) und Meldungen je Thema.
// ---------------------------------------------------------------------------

/// <summary>Neuerung eines Wiki-Themas mit dem Lesestand des angemeldeten Benutzers und der Gesamtstatistik.</summary>
public record WikiNeuerungEintrag(
    int Id,
    string Thema,
    string Titel,
    string Text,
    int? PreislistenVersion,
    bool BestaetigungErforderlich,
    DateTime ErstelltAm,
    string? ErstelltVon,
    DateTime? VonMirGesehenAm,
    DateTime? VonMirBestaetigtAm,
    // Anzahl aktiver Mitarbeiter, die die Neuerung lesen sollen (Kreis der Neuerung)
    int LeserGesamt,
    int Gesehen,
    int Bestaetigt,
    // Nur für Redaktion/Verantwortliche gefüllt: wer hat gelesen/bestätigt, wer fehlt
    List<WikiLesung>? Lesungen,
    // Bestätigungspflicht, der Benutzer gehört zum Kreis und hat noch nicht bestätigt
    bool VonMirZuBestaetigen = false,
    // Kreis der Neuerung als Text (z. B. „Vertrieb, Support") — null = alle Mitarbeiter
    string? Zielkreis = null,
    // Kommunikationsleitlinie, deren Freigabe/Rücknahme die Neuerung erzeugt hat (nicht von Hand änderbar)
    int? SprachregelId = null);

public record WikiLesung(string Name, DateTime? GesehenAm, DateTime? BestaetigtAm);

public record WikiNeuerungSpeichern(string Titel, string Text, bool BestaetigungErforderlich);

/// <summary>Meldung „etwas fehlt oder ist falsch" zu einem Wiki-Thema.</summary>
public record WikiMeldungEintrag(
    int Id,
    string Thema,
    string? Bezug,
    string Text,
    DateTime ErstelltAm,
    string? ErstelltVon,
    DateTime? ErledigtAm,
    string? ErledigtVon,
    string? Antwort);

public record WikiMeldungSpeichern(string? Bezug, string Text);

public record WikiMeldungErledigen(string? Antwort);

/// <summary>Meldungen eines Themas plus: darf der Benutzer sie erledigen (Redaktion, Verantwortlich, Vertretung)?</summary>
public record WikiMeldungenAntwort(List<WikiMeldungEintrag> Eintraege, bool DarfErledigen, string? Verantwortlich, string? Vertreter);

/// <summary>Was wartet im Wiki auf den angemeldeten Benutzer — für das Abzeichen in der Navigation.</summary>
public record WikiHinweise(int UnbestaetigteNeuerungen, int OffeneMeldungen);
