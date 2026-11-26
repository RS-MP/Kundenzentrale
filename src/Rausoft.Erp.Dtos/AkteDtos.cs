namespace Rausoft.Erp.Dtos;

/// <summary>
/// Beteiligte Person in der Akte. <paramref name="Schluessel"/>: „m:5" (Mitarbeiter),
/// „a:7" (Ansprechpartner) oder „n:Name" (nur Name bekannt) — auch Filter „Wer mit wem".
/// <paramref name="Zusatz"/>: Bereich (rausoft) bzw. Funktion/Position (Kunde).
/// </summary>
public record AktePerson(string Schluessel, string Name, string? Zusatz);

/// <summary>Kopf der Akte: was lief in den letzten <paramref name="Tage"/> Tagen, wer mit wem.</summary>
public record AkteUeberblick(
    int Tage,
    int Kontakte,
    LetzterKontakt? Letzter,
    List<AktePerson> Rausoft,
    List<AktePerson> Kunde,
    List<GespraechsZelle> Zellen,
    int AufgabenOffen,
    int AufgabenUeberfaellig,
    int VereinbarungenOffen,
    /// <summary>Die letzten Gespräche mit Betreff — worum ging es zuletzt?</summary>
    List<KontaktZeile>? Letzte = null,
    /// <summary>Je Ansprechpartner beim Kunden: wie oft, wann zuletzt, mit wem bei uns.</summary>
    List<PersonKontakt>? JePerson = null);

public record KontaktZeile(int AktivitaetId, DateTime Zeitpunkt, string Art, string Betreff, List<AktePerson> Rausoft, List<AktePerson> Kunde);

/// <summary><paramref name="Mit"/>: Personen bei uns, häufigste zuerst.</summary>
public record PersonKontakt(AktePerson Person, int Anzahl, DateTime Zuletzt, List<AktePerson> Mit);

public record LetzterKontakt(DateTime Zeitpunkt, string Art, List<AktePerson> Rausoft, List<AktePerson> Kunde);

/// <summary>Zelle der Wer-mit-wem-Tabelle: Anzahl Kontakte zwischen zwei Personen und wann zuletzt.</summary>
public record GespraechsZelle(string Rausoft, string Kunde, int Anzahl, DateTime Zuletzt);

public record AufgabeEintrag(
    int Id,
    string Titel,
    string? Art,
    string? Beschreibung,
    DateOnly? Faellig,
    string? Zustaendig,
    string? ZustaendigBereich,
    bool IstPool,
    bool Erledigt,
    string? Bezug,
    string? Vorgang,
    string Quelle,
    DateTime? ErledigtAm,
    /// <summary>Gesetzt bei Vorgangsschritten — erledigt wird dann im Vorgang.</summary>
    int? VorgangId = null,
    bool WartetAufKunden = false,
    /// <summary>Hinweis des Vorgangs — nur am ersten offenen Schritt je Vorgang, damit er einmal sichtbar ist.</summary>
    string? VorgangHinweis = null,
    /// <summary>Letzte Nachricht im Vorgang (Notiz, Erledigt-/Warte-Notiz) — ebenfalls am ersten offenen Schritt.</summary>
    VorgangNachricht? Nachricht = null,
    /// <summary>In der Kundenzentrale angelegt, ohne Vorgang (AufgabenRegeln).</summary>
    bool DarfBearbeiten = false,
    /// <summary>Wie <see cref="DarfBearbeiten"/>, dazu angelegt von mir, bei mir oder ich bin Admin.</summary>
    bool DarfLoeschen = false);

public record VorgangNachricht(DateTime Zeitpunkt, string? Von, string Betreff, string Text);

/// <summary>
/// Anlegen und Ändern einer Aufgabe. Zuständig ist entweder <paramref name="ZustaendigId"/> (Person)
/// oder <paramref name="ZustaendigBereichId"/> (Pool).
/// </summary>
public record AufgabeAnlegen(string Titel, string? Art, DateOnly? Faellig, int? ZustaendigId, int? ZustaendigBereichId, string? Vorgang,
    string? Beschreibung = null);

/// <summary>Eine Aufgabe zum Bearbeiten (nur in der Kundenzentrale angelegte, ohne Vorgang).</summary>
public record AufgabeDetail(
    int Id,
    int? KundeId,
    string Titel,
    string? Art,
    string? Beschreibung,
    DateOnly? Faellig,
    int? ZustaendigId,
    int? ZustaendigBereichId,
    /// <summary>Anzeigename der zuständigen Person — falls sie inzwischen deaktiviert ist und in der Auswahl fehlt.</summary>
    string? Zustaendig,
    /// <summary>Strang-Schlüssel und Name des Vorgangs aus dem Verlauf.</summary>
    string? Vorgang,
    string? Bezug);

public record ErledigtSetzen(bool Erledigt);

public record VereinbarungEintrag(
    int Id,
    string Text,
    bool Erledigt,
    DateTime ErstelltAm,
    string? ErstelltVon,
    int? AktivitaetId,
    string? QuelleArt,
    DateTime? QuelleZeitpunkt,
    string? QuelleBetreff,
    List<AktePerson> QuelleRausoft,
    List<AktePerson> QuelleKunde);

public record VereinbarungAnlegen(string Text, int? AktivitaetId);

/// <summary>Achtung-Hinweis am Kunden — für alle sichtbar, überall wo am Kunden gearbeitet wird.</summary>
public record KundenHinweisEintrag(int Id, string Text, DateTime ErstelltAm, string? ErstelltVon);

public record KundenHinweisAnlegen(string Text);

/// <summary>
/// Manueller Verlaufseintrag. <paramref name="Typ"/>: „Notiz", „Anruf" oder „Termin";
/// <paramref name="Richtung"/> nur beim Anruf („Eingehend"/„Ausgehend");
/// <paramref name="Zeitpunkt"/> in Ortszeit; Vereinbarungen werden gleich mit angelegt.
/// </summary>
public record EintragAnlegen(
    string Typ,
    DateTime Zeitpunkt,
    string? Richtung,
    string Betreff,
    string? Inhalt,
    List<int> MitarbeiterIds,
    List<int> AnsprechpartnerIds,
    List<string> Vereinbarungen,
    string? Vorgang);

public record AuswahlEintrag(int Id, string Name, string? Zusatz);
public record VorgangAuswahl(string Schluessel, string Name);

/// <summary>Auswahllisten für die Erfassungsformulare der Akte.</summary>
public record ErfassungsAuswahl(
    List<AuswahlEintrag> Mitarbeiter,
    List<AuswahlEintrag> Ansprechpartner,
    List<AuswahlEintrag> Bereiche,
    List<VorgangAuswahl> Vorgaenge,
    int? IchId);
