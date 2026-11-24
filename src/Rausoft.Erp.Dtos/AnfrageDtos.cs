namespace Rausoft.Erp.Dtos;

/// <summary>Eingefügter Text einer Website-Formular-Mail.</summary>
public record AnfrageAuslesen(string? Text);

/// <summary>Anfrage speichern: derselbe Text wie beim Auslesen, optional gleich einem Kunden zugeordnet.</summary>
public record AnfrageSpeichern(string? Text, int? KundeId);

/// <summary>Zuordnung einer gespeicherten Anfrage setzen (<c>null</c> löst sie).</summary>
public record AnfrageKundeSetzen(int? KundeId);

public record AnfrageHerkunftsFeld(string Name, string Wert);

/// <summary>Vorhandener Kunde, der zur Anfrage passen könnte.</summary>
public record AnfrageDublette(
    int KundeId,
    string Kundennummer,
    string Firmenname,
    bool Archiviert,
    /// <summary>100 = sicher (gleiche Adresse/gleicher Name), darunter absteigend.</summary>
    int Uebereinstimmung,
    /// <summary>Woran der Treffer erkannt wurde, z. B. „gleiche E-Mail-Domäne (2 Ansprechpartner)".</summary>
    string Grund);

/// <summary>Ausgelesene Anfrage als Vorschlag, mit Auffälligkeiten und möglichen Dubletten im Kundenbestand.</summary>
public record AnfrageVorschlag(
    string Formular,
    string? Website,
    string? Firma,
    string? Vorname,
    string? Nachname,
    string? EMail,
    string? Telefon,
    string? Nachricht,
    bool DatenschutzBestaetigt,
    DateTime? Eingang,
    List<AnfrageHerkunftsFeld> Herkunft,
    List<string> Hinweise,
    List<AnfrageDublette> Dubletten,
    /// <summary>Lesbare Herkunft, z. B. „Google Ads · Kampagne Nr. 123".</summary>
    string HerkunftText,
    /// <summary>Dieselbe Anfrage (Formular, E-Mail, Eingangszeit) ist schon gespeichert.</summary>
    bool BereitsGespeichert);

/// <summary>Gespeicherte Anfrage für die Liste; Zeiten in deutscher Ortszeit.</summary>
public record AnfrageEintrag(
    int Id,
    DateTime Eingang,
    string Formular,
    string? Website,
    string? Firma,
    string? Kontakt,
    string? EMail,
    string? Telefon,
    string? Nachricht,
    string Kanal,
    string Herkunft,
    string? Einstiegsseite,
    string? LetzteSeite,
    /// <summary>Minuten zwischen erstem Besuch und Anfrage; fehlt, wenn der erste Besuch unbekannt ist.</summary>
    int? MinutenBisAnfrage,
    /// <summary>Tag, an dem die Klick-Kennungen gelöscht werden; fehlt, wenn keine (mehr) gespeichert sind.</summary>
    DateTime? KlickKennungBis,
    int? KundeId,
    string? Kundenname,
    string? Kundennummer,
    string? ErfasstVon,
    /// <summary>Nummer der Verkaufschance in Odoo, wenn die Anfrage von dort übernommen oder mit ihr verknüpft ist.</summary>
    int? OdooLeadId = null,
    /// <summary>Ausgang laut Odoo: „offen“, „gewonnen“ oder „verloren“; fehlt ohne Verkaufschance.</summary>
    string? Ergebnis = null,
    string? VerlustGrund = null,
    /// <summary>Stand des Anlegens in Odoo aus der Kundenzentrale; fehlt, solange nichts versucht wurde.</summary>
    AnfrageOdooAnlageInfo? OdooAnlage = null,
    /// <summary>Warum diese Anfrage (jetzt) nicht in Odoo angelegt werden kann; fehlt, wenn es geht.</summary>
    string? OdooSperre = null,
    /// <summary>Der zugeordnete Kunde hat heute ein laufendes Abo oder einen aktiven Vertrag (nur in der Liste gefüllt).</summary>
    bool HeuteKunde = false);

public record AnfrageKanalZahl(string Kanal, int Anzahl, int DavonKunden, int Gewonnen = 0, int Verloren = 0);

/// <summary>Anfragen eines Jahres mit Zählung je Kanal.</summary>
public record AnfragenUebersicht(
    int Jahr,
    List<int> Jahre,
    int Gesamt,
    /// <summary>Anfragen, deren zugeordneter Kunde heute ein laufendes Abo oder einen aktiven Vertrag hat.</summary>
    int DavonKunden,
    List<AnfrageKanalZahl> Kanaele,
    List<AnfrageEintrag> Eintraege,
    /// <summary>Wohin „In Odoo anlegen“ schreibt: „Aus“, „Test“ oder „Produktiv“.</summary>
    string OdooZiel = "Aus",
    /// <summary>Warum gerade gar nicht nach Odoo geschrieben wird (ausgeschaltet, Testdatenbank abgelaufen …).</summary>
    string? OdooSperre = null);

// ---------- Anlegen in Odoo (Stufe 2) ----------

/// <summary>Stand des Anlegens einer Anfrage in Odoo. Die Nummern gelten nur in der genannten Datenbank.</summary>
public record AnfrageOdooAnlageInfo(
    /// <summary>„Test“ oder „Produktiv“.</summary>
    string Ziel,
    bool Fertig,
    /// <summary>Abschluss bzw. Beginn in deutscher Ortszeit.</summary>
    DateTime Am,
    int? LeadNr,
    /// <summary>Verweis auf die Verkaufschance in Odoo; fehlt für eine inzwischen ersetzte Testdatenbank.</summary>
    string? LeadLink,
    /// <summary>Fester Text zum letzten fehlgeschlagenen Schritt.</summary>
    string? Fehler,
    /// <summary>Der Stand gehört zur Datenbank, in die gerade geschrieben würde.</summary>
    bool AktuelleDatenbank);

public record OdooAuswahl(int Id, string Name);

public record AnfrageOdooLead(int Id, string Name, string? Phase, string? Zustaendig, string Link);

/// <summary>Firma in Odoo, die zur Anfrage passen könnte.</summary>
public record AnfrageOdooFirma(
    int Id,
    string Name,
    int Uebereinstimmung,
    string Grund,
    /// <summary>Bisheriger Hauptansprechpartner der Firma, wenn es einen gibt.</summary>
    string? Hauptansprechpartner,
    /// <summary>Der bisherige Hauptansprechpartner ist die anfragende Person.</summary>
    bool HauptansprechpartnerIstKontakt,
    string Link,
    List<AnfrageOdooLead> OffeneVerkaufschancen);

/// <summary>Der Ansprechpartner: vorhandener Kontakt in Odoo (mit <see cref="Id"/>) oder der neu anzulegende.</summary>
public record AnfrageOdooKontakt(
    int? Id,
    /// <summary>Odoo-Feld „Name“ (Titel + Nachname).</summary>
    string Name,
    string? Vorname,
    string EMail,
    string? Telefon,
    /// <summary>Firma, zu der ein vorhandener Kontakt in Odoo gehört.</summary>
    string? Firma,
    bool HatAnrede,
    string? Link);

/// <summary>
/// Vorschau: was „In Odoo anlegen“ anlegen oder wiederverwenden würde. Gelesen aus Odoo,
/// geschrieben wurde noch nichts.
/// </summary>
public record AnfrageOdooVorschau(
    /// <summary>„Test“ oder „Produktiv“.</summary>
    string Ziel,
    /// <summary>Rechner und Datenbank, z. B. „firma-test.odoo.com/firma-test“.</summary>
    string Datenbank,
    /// <summary>Schritte, die ein früherer, abgebrochener Lauf schon erledigt hat.</summary>
    List<string> Erledigt,
    string? LetzterFehler,
    List<AnfrageOdooFirma> Firmen,
    int? FirmaVorschlag,
    /// <summary>Die Firma steht aus einem früheren Lauf fest und ist nicht mehr wählbar.</summary>
    bool FirmaFest,
    /// <summary>Name der Firma, die neu angelegt würde; fehlt, wenn die Anfrage keinen Firmennamen hat.</summary>
    string? NeueFirma,
    AnfrageOdooKontakt Kontakt,
    /// <summary>„Herr“ oder „Frau“ bei eindeutigem Vornamen, sonst leer.</summary>
    string? AnredeVorschlag,
    /// <summary>Die Anrede muss gewählt werden (neuer Kontakt oder vorhandener ohne Anrede).</summary>
    bool AnredeWaehlbar,
    List<AnfrageOdooLead> VerkaufschancenZumKontakt,
    string LeadTitel,
    /// <summary>Verkaufschance aus einem früheren Lauf; dann wird nur noch die Aktivität nachgezogen.</summary>
    int? LeadFest,
    List<OdooAuswahl> Phasen,
    int? PhaseVorschlag,
    List<OdooAuswahl> Stichwoerter,
    List<OdooAuswahl> Benutzer,
    int? BenutzerVorschlag,
    List<OdooAuswahl> Aktivitaetsarten,
    int? AktivitaetsartVorschlag,
    string AktivitaetsText,
    DateOnly Faellig,
    List<OdooAuswahl> Quellen,
    int? QuelleVorschlag,
    List<OdooAuswahl> Medien,
    int? MediumVorschlag,
    /// <summary>Felder der Verkaufschance, die gefüllt würden (Feldname in Odoo und Wert).</summary>
    List<AnfrageHerkunftsFeld> Uebertragen,
    List<string> Beschreibung,
    List<string> NichtUebertragen,
    List<string> Hinweise,
    /// <summary>
    /// Domäne, über die Odoo eine neue Firma anreichern kann (Adresse, USt-ID); fehlt bei
    /// Freemailern — dann gibt es nichts anzureichern.
    /// </summary>
    string? AnreicherungDomaene = null);

/// <summary>Bestätigung der Vorschau mit der Wahl des Mitarbeiters.</summary>
public record AnfrageOdooAnlegen(
    /// <summary>Vorhandene Firma; leer = neue Firma anlegen.</summary>
    int? FirmaId,
    /// <summary>„Herr“, „Frau“ oder „Keine“.</summary>
    string? Anrede,
    bool HauptansprechpartnerUmstellen,
    /// <summary>Offene Verkaufschance, die ergänzt wird; leer = neue anlegen.</summary>
    int? LeadId,
    int? PhaseId,
    List<int>? StichwortIds,
    int? BenutzerId,
    int? AktivitaetsartId,
    int? QuelleId,
    int? MediumId,
    /// <summary>Eine neue Firma von Odoo über die E-Mail-Domäne anreichern lassen.</summary>
    bool FirmaAnreichern = false);

public record AnfrageOdooErgebnis(
    bool Fertig,
    List<string> Schritte,
    /// <summary>Fester Text, wenn ein Schritt fehlschlug — die erledigten Schritte bleiben stehen.</summary>
    string? Fehler,
    AnfrageEintrag Eintrag);
