namespace Rausoft.Erp.Dtos;

// ---------------------------------------------------------------------------
// Offene Angebote und Aufträge aus Odoo (ANFORDERUNGEN.md): Angebote, auf die der
// Kunde nicht reagiert hat, und bestätigte Aufträge ohne Vorgang. Ohne Preise.
// ---------------------------------------------------------------------------

public record VerkaufUebersicht(
    List<VerkaufZeile> Angebote,
    List<VerkaufZeile> Auftraege,
    List<VerkaufZeile> Ausgeblendet,
    int NachfassTage,
    int RueckblickTage,
    bool OdooErreichbar,
    string? Hinweis);

/// <param name="Tage">Tage seit dem Angebots- bzw. Auftragsdatum.</param>
/// <param name="Abgelaufen">Die Gültigkeit des Angebots ist überschritten.</param>
/// <param name="AufgabeBei">Wer die Nachfass-Aufgabe hat (Mitarbeiter oder Name des Odoo-Verkäufers).</param>
public record VerkaufZeile(
    string Nummer,
    bool Bestaetigt,
    int? KundeId,
    string Kunde,
    string? Kundennummer,
    DateOnly? Datum,
    int? Tage,
    DateOnly? GueltigBis,
    bool Abgelaufen,
    string? Verkaeufer,
    string? Inhalt,
    string? Referenz,
    bool Abo,
    string? OdooLink,
    bool AufgabeOffen,
    DateOnly? AufgabeFaellig,
    string? AufgabeBei,
    int Nachfassungen,
    DateTime? AusgeblendetAm,
    string? AusgeblendetVon,
    string? AusblendGrund);

public record VerkaufAusblenden(string Nummer, string? Grund);

public record VerkaufLaufErgebnis(int AufgabenAngelegt, int AufgabenGeschlossen, string? Hinweis);
