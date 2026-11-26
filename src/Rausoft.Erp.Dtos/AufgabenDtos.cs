namespace Rausoft.Erp.Dtos;

// ---------------------------------------------------------------------------
// Meine Aufgaben / mein Bereich (Aufgabenverwaltung Phase 2): alle offenen
// Aufgaben — Vorgangsschritte, manuelle Aufgaben aus der Akte, aus Odoo
// übernommene Aktivitäten. Gesperrte Vorgangsschritte erscheinen erst, wenn
// sie dran sind.
// ---------------------------------------------------------------------------

public record AufgabenUebersicht(
    List<AufgabeZeile> Aufgaben,
    List<AuswahlEintrag> Bereiche,
    int? IchId,
    int? MeinBereichId,
    /// <summary>Gezeigter Bereich (Pool bzw. ganzer Bereich).</summary>
    int? BereichId);

public record AufgabeZeile(
    int Id,
    string Titel,
    string? Art,
    string? Beschreibung,
    int? KundeId,
    string? Kunde,
    string? Kundennummer,
    /// <summary>Maßgebliches Datum: Frist, bei „Wartet auf Kunden" die Wiedervorlage.</summary>
    DateOnly? Faellig,
    /// <summary>"ueberfaellig" · "heute" · "woche" · "spaeter" · "wartet"</summary>
    string Gruppe,
    /// <summary>"offen" · "inarbeit" · "wartet" · "wiedervorlage" · "rueckfrage" · "zurueckgestellt"</summary>
    string Status,
    int? ZustaendigId,
    string? Zustaendig,
    int? BereichId,
    string? Bereich,
    bool IstPool,
    int? VorgangId,
    string? Vorgang,
    string? VorgangHinweis,
    string? Pflichtfeld,
    string? Bezug,
    string Quelle,
    /// <summary>In der Kundenzentrale angelegt, ohne Vorgang (AufgabenRegeln).</summary>
    bool DarfBearbeiten = false,
    /// <summary>Wie <see cref="DarfBearbeiten"/>, dazu angelegt von mir, bei mir oder ich bin Admin.</summary>
    bool DarfLoeschen = false,
    /// <summary>Achtung-Hinweise am Kunden der Aufgabe.</summary>
    List<string>? KundenHinweise = null);

/// <summary>Für das Abzeichen in der Navigation: fällig bis heute, bei mir oder unbesetzt in meinem Pool.</summary>
public record AufgabenZaehler(int FaelligBisHeute, int Ueberfaellig);

/// <summary>Ergebnis des Abgleichs mit Odoo (Strang nachziehen, Rechnungen abhaken).</summary>
public record VorgangsAbgleichErgebnis(int StraengeVerknuepft, int SchritteAbgehakt, string? Hinweis);
