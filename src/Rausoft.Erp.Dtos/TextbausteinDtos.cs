namespace Rausoft.Erp.Dtos;

// ---------------------------------------------------------------------------
// E-Mail-Textbausteine (ANFORDERUNGEN.md, E-Mail): Betreff + formatierter Text,
// übernommen aus den Odoo-Mailvorlagen oder hier angelegt.
// ---------------------------------------------------------------------------

public record TextbausteinUebersicht(List<TextbausteinZeile> Bausteine, bool DarfPflegen);

public record TextbausteinZeile(
    int Id, string Name, string Betreff, string? Verwendung, bool Aktiv, bool AusOdoo, bool LokalGeaendert,
    bool HatHinweise, DateTime GeaendertAm);

/// <param name="Hinweise">Was bei der Übernahme aus Odoo nicht umgesetzt werden konnte.</param>
public record TextbausteinDetail(
    int Id, string Name, string Betreff, string InhaltHtml, string? Verwendung, List<string> Hinweise, bool Aktiv,
    bool AusOdoo, bool LokalGeaendert, DateTime GeaendertAm, string? GeaendertVon);

public record TextbausteinSpeichern(string Name, string Betreff, string InhaltHtml, string? Verwendung, bool Aktiv);

public record TextbausteinUebernahme(
    bool Probelauf, int Gelesen, int Neu, int Aktualisiert, int Unveraendert, int LokalGeaendert, int MitHinweisen, string? Hinweis);
