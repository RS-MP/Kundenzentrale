namespace Rausoft.Erp.Dtos;

// ---------------------------------------------------------------------------
// Offene Angebote und Aufträge aus Odoo (ANFORDERUNGEN.md): Angebote, auf die der
// Kunde nicht reagiert hat, und bestätigte Aufträge ohne Vorgang. Ohne Preise.
// ---------------------------------------------------------------------------

/// <param name="Auftraege">Bestätigt, ohne Rechnung, weder Vorgang noch Odoo-Projekt — niemand hat angefangen.</param>
/// <param name="InOdoo">Bestätigt, ohne Vorgang, aber mit laufendem Projekt in Odoo.</param>
/// <param name="ProjektHinweis">Gesetzt, wenn die Odoo-Projekte nicht lesbar waren — dann fehlt die Liste <paramref name="InOdoo"/>.</param>
public record VerkaufUebersicht(
    List<VerkaufZeile> Angebote,
    List<VerkaufZeile> Auftraege,
    List<VerkaufZeile> InOdoo,
    List<VerkaufZeile> Ausgeblendet,
    int NachfassTage,
    int RueckblickTage,
    bool OdooErreichbar,
    string? Hinweis,
    string? ProjektHinweis = null);

/// <summary>Stand eines Odoo-Projekts zu einem bestätigten Auftrag.</summary>
/// <param name="Vermutet">Nur über Kunde und Zeitpunkt zugeordnet — Odoo verknüpft Projekt und Auftrag nicht selbst.</param>
/// <param name="AufgabenErledigt">Leer, wenn Odoo den Zustand der Aufgaben nicht preisgibt.</param>
/// <param name="TageOhneBewegung">Tage seit der letzten Änderung am Projekt oder an einer Aufgabe.</param>
public record VerkaufProjekt(
    string Name,
    bool Vermutet,
    string? Phase,
    string? Status,
    string? Leitung,
    DateOnly? AngelegtAm,
    int Aufgaben,
    int? AufgabenErledigt,
    DateOnly? LetzteBewegung,
    int? TageOhneBewegung,
    bool Abgeschlossen,
    string? OdooLink);

/// <param name="Tage">Tage seit dem Angebots- bzw. Auftragsdatum.</param>
/// <param name="Abgelaufen">Die Gültigkeit des Angebots ist überschritten.</param>
/// <param name="AufgabeBei">Wer die Nachfass-Aufgabe hat (Mitarbeiter oder Name des Odoo-Verkäufers).</param>
/// <param name="Projekte">Odoo-Projekte zum bestätigten Auftrag; leer bei Angeboten und wenn es keins gibt.</param>
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
    string? AusblendGrund,
    List<VerkaufProjekt>? Projekte = null);

public record VerkaufAusblenden(string Nummer, string? Grund);

public record VerkaufLaufErgebnis(int AufgabenAngelegt, int AufgabenGeschlossen, string? Hinweis);
