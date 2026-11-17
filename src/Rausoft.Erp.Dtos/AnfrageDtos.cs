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
    string? ErfasstVon);

public record AnfrageKanalZahl(string Kanal, int Anzahl, int DavonKunden);

/// <summary>Anfragen eines Jahres mit Zählung je Kanal.</summary>
public record AnfragenUebersicht(
    int Jahr,
    List<int> Jahre,
    int Gesamt,
    /// <summary>Anfragen, deren zugeordneter Kunde heute ein laufendes Abo oder einen aktiven Vertrag hat.</summary>
    int DavonKunden,
    List<AnfrageKanalZahl> Kanaele,
    List<AnfrageEintrag> Eintraege);
