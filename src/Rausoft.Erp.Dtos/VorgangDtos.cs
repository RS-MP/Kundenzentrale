namespace Rausoft.Erp.Dtos;

// ---------------------------------------------------------------------------
// Vorgänge: Bestellung → Teilaufgaben je Bereich (ANFORDERUNGEN.md, „Vorgänge").
// Statuswerte als Text, damit das Frontend ohne Domain-Referenz auskommt:
//   Vorgang: "neu" · "laeuft" · "wartet" · "haengt" · "fertig"
//   Schritt: "gesperrt" · "offen" · "inarbeit" · "wartet" · "wiedervorlage" (Wiedervorlage
//            erreicht) · "erledigt" · "entfallen"
//   Benachrichtigung: "Keine" · "Mail" · "Teams"
//   AutoAbhaken: "Nie" · "OdooRechnung" · "VertragUnterschrieben"
// ---------------------------------------------------------------------------

public record VorgangUebersicht(
    List<VorgangZeile> Vorgaenge,
    List<AuswahlEintrag> Bereiche,
    int? IchId,
    int? MeinBereichId,
    bool DarfTypenPflegen);

public record VorgangZeile(
    int Id,
    int KundeId,
    string Kunde,
    string Kundennummer,
    string Typ,
    string Titel,
    string? OdooAuftrag,
    string? Hinweis,
    DateOnly Start,
    string Status,
    int Erledigt,
    int Gesamt,
    string? NaechsterSchritt,
    string? NaechsterBereich,
    string? NaechsterPerson,
    DateOnly? NaechsterFaellig,
    bool NaechsterUeberfaellig,
    List<int> AktiveBereiche,
    /// <summary>Ein aktiver Schritt liegt bei mir oder unbesetzt im Pool meines Bereichs.</summary>
    bool IchBinDran,
    DateTime? AbgeschlossenAm);

public record VorgangDetail(
    int Id,
    int KundeId,
    string Kunde,
    string Kundennummer,
    int TypId,
    string Typ,
    int TypStand,
    string Titel,
    string? OdooAuftrag,
    string? Hinweis,
    DateOnly Start,
    string Status,
    DateTime ErstelltAm,
    string? ErstelltVon,
    DateTime? AbgeschlossenAm,
    string StrangName,
    List<VorgangSchritt> Schritte,
    List<VorgangVerlaufEintrag> Verlauf,
    List<AuswahlEintrag> Bereiche,
    List<AuswahlEintrag> Mitarbeiter,
    int? IchId);

public record VorgangSchritt(
    int Id,
    int Nr,
    string Titel,
    int? BereichId,
    string? Bereich,
    int? ZustaendigId,
    string? Zustaendig,
    string Status,
    DateOnly? Faellig,
    bool Ueberfaellig,
    DateOnly? Wiedervorlage,
    int? WartetAufId,
    string? WartetAufTitel,
    bool Manuell,
    string? EntfallenGrund,
    string? Pflichtfeld,
    string? PflichtfeldWert,
    string Benachrichtigung,
    string AutoAbhaken,
    int FristTage,
    DateTime? ErledigtAm,
    string? ErledigtVon);

public record VorgangVerlaufEintrag(DateTime Zeitpunkt, string? Wer, string Text, string? Inhalt);

/// <summary>Neuer Vorgang: Vorlage plus Anpassungen im Einzelfall.</summary>
public record VorgangAnlegen(
    int KundeId,
    int VorgangstypId,
    string? OdooAuftrag,
    string? Hinweis,
    DateOnly? Start,
    /// <summary>Nummern der Vorlagenschritte, die in diesem Vorgang entfallen.</summary>
    List<int>? EntfernteSchritte,
    /// <summary>Angekreuzte Bedingungen, z. B. „Schulung bestellt".</summary>
    List<string>? ErfuellteBedingungen,
    List<ManuellerSchrittAnlegen>? ManuelleSchritte);

/// <summary>Manueller Schritt beim Anlegen; wartet optional auf einen Vorlagenschritt (Nummer).</summary>
public record ManuellerSchrittAnlegen(string Titel, int BereichId, int FristTage, int? WartetAufNr, int? ZustaendigId);

/// <summary>Manueller Schritt am laufenden Vorgang; wartet optional auf einen vorhandenen Schritt.</summary>
public record SchrittHinzufuegen(string Titel, int BereichId, int FristTage, int? WartetAufId, int? ZustaendigId);

public record SchrittErledigen(string? Wert, string? Notiz);

public record SchrittWarten(DateOnly Wiedervorlage, string? Notiz);

public record SchrittNotiz(string Text);

public record SchrittZuweisen(int? ZustaendigId);

public record HinweisSetzen(string? Text);

/// <summary>Odoo-Auftrag nachträglich eintragen, ändern oder entfernen (null).</summary>
public record OdooAuftragSetzen(string? Nummer);

/// <summary>
/// Auswahl der Odoo-Aufträge eines Kunden. Quelle "odoo" = live abgefragt; "bestand" =
/// Odoo nicht erreichbar, Auswahl aus importierten Abos und dem Odoo-Verlauf der Akte.
/// </summary>
public record AuftragsAuswahl(List<AuftragsVorschlag> Auftraege, string Quelle, string? Hinweis);

public record AuftragsVorschlag(
    string Nummer,
    DateOnly? Datum,
    bool Abo,
    /// <summary>Gebuchte Produkte zum Wiedererkennen, gekürzt.</summary>
    string? Inhalt,
    string? Referenz,
    /// <summary>Vorgang zu dieser Nummer, falls es schon einen gibt (laufender bevorzugt).</summary>
    int? VorgangId,
    string? VorgangTitel,
    bool VorgangLaeuft);

// --- Vorgangstypen (Vorlagen) ------------------------------------------------

public record VorgangstypenUebersicht(
    List<VorgangstypEintrag> Typen,
    List<AuswahlEintrag> Bereiche,
    bool DarfPflegen);

public record VorgangstypEintrag(
    int Id,
    string Name,
    string? Beschreibung,
    bool Aktiv,
    int Stand,
    int AnzahlSchritte,
    int LaufendeVorgaenge);

public record VorgangstypDetail(
    int Id,
    string Name,
    string? Beschreibung,
    bool Aktiv,
    int Stand,
    DateTime GeaendertAm,
    string? GeaendertVon,
    int LaufendeVorgaenge,
    List<string> Bedingungen,
    List<VorgangstypSchrittDaten> Schritte);

public record VorgangstypSchrittDaten(
    int Nr,
    string Titel,
    int BereichId,
    int FristTage,
    int? WartetAufNr,
    string? Bedingung,
    string Benachrichtigung,
    string? Pflichtfeld,
    string AutoAbhaken);

public record VorgangstypSpeichern(string Name, string? Beschreibung, List<VorgangstypSchrittDaten> Schritte);

public record VorgangstypKopieren(string Name);

public record AktivSetzen(bool Aktiv);

// --- Bereichsleitung (Administration) ----------------------------------------

public record BereichEintrag(int Id, string Name, bool Aktiv, int? LeiterId, string? Leiter);

public record BereichLeiterSetzen(int? LeiterId);
