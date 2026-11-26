namespace Rausoft.Erp.Dtos;

// ---------------------------------------------------------------------------
// E-Mail-Textbausteine (ANFORDERUNGEN.md, E-Mail): Betreff + formatierter Text,
// übernommen aus den Odoo-Mailvorlagen oder hier angelegt; mit den Dateien, die
// Odoo an die Vorlage hängt.
// ---------------------------------------------------------------------------

/// <param name="Entfernt">Nur für Admins: entfernte Bausteine, zum Wiederherstellen.</param>
public record TextbausteinUebersicht(List<TextbausteinZeile> Bausteine, bool DarfPflegen, List<TextbausteinEntfernt>? Entfernt = null);

public record TextbausteinZeile(
    int Id, string Name, string Betreff, string? Verwendung, bool Aktiv, bool AusOdoo, bool LokalGeaendert,
    bool HatHinweise, DateTime GeaendertAm, int Anhaenge = 0);

public record TextbausteinEntfernt(int Id, string Name, string? Verwendung, DateTime EntferntAm, string? EntferntVon);

public record TextbausteinAnhangZeile(int Id, string Dateiname, long Groesse);

/// <param name="Hinweise">Was bei der Übernahme aus Odoo nicht umgesetzt werden konnte.</param>
public record TextbausteinDetail(
    int Id, string Name, string Betreff, string InhaltHtml, string? Verwendung, List<string> Hinweise, bool Aktiv,
    bool AusOdoo, bool LokalGeaendert, DateTime GeaendertAm, string? GeaendertVon, List<TextbausteinAnhangZeile>? Anhaenge = null);

public record TextbausteinSpeichern(string Name, string Betreff, string InhaltHtml, string? Verwendung, bool Aktiv);

/// <param name="Entfernt">Vorlagen, die hier entfernt wurden und deshalb nicht wieder übernommen werden.</param>
/// <param name="AnhaengeNeu">Neu oder geändert übernommene Dateien (im Probelauf: die es wären).</param>
/// <param name="AnhangHinweise">Dateien, die nicht übernommen wurden, mit Grund.</param>
public record TextbausteinUebernahme(
    bool Probelauf, int Gelesen, int Neu, int Aktualisiert, int Unveraendert, int LokalGeaendert, int MitHinweisen, string? Hinweis,
    int Entfernt = 0, int AnhaengeNeu = 0, int AnhaengeEntfernt = 0, List<string>? AnhangHinweise = null);
