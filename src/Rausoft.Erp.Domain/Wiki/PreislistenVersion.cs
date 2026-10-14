using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Domain.Wiki;

/// <summary>
/// Importierte Preisliste (Wiki, Thema „Preisliste"). Quelle ist der
/// JSON-Export des Preislisten-Projekts (ANLEITUNG-PREISLISTEN-EXPORT.md);
/// die Kundenzentrale speichert die Datei unverändert und wertet sie bei der
/// Anzeige aus. Jede Version ist eine neue Zeile — die Historie bleibt.
/// </summary>
public class PreislistenVersion
{
    public int Id { get; set; }

    /// <summary>Fortlaufende Nummer aus dem Export (eindeutig).</summary>
    public int Version { get; set; }

    /// <summary>„JJJJ-MM" aus dem Export.</summary>
    public required string Stand { get; set; }

    public DateOnly? GueltigAb { get; set; }

    public required string Produkt { get; set; }

    public required string Schema { get; set; }

    /// <summary>Die importierte JSON — enthält auch interne Preise; Sichtbarkeit filtert die API.</summary>
    public required string Daten { get; set; }

    public required string Sha256 { get; set; }

    /// <summary>Dateinamen der PDFs im Preislisten-Ordner (Einstellung Preisliste.Verzeichnis).</summary>
    public string? DateiIntern { get; set; }
    public string? DateiExtern { get; set; }

    public DateTime ImportiertAm { get; set; }
    public int? ImportiertVonId { get; set; }
    public Mitarbeiter? ImportiertVon { get; set; }

    public string? Notiz { get; set; }
}

/// <summary>Zuständigkeit und Prozess je Wiki-Thema (für Themen ohne eigene Datensätze wie die Preisliste).</summary>
public class WikiThemaInfo
{
    /// <summary>Schlüssel aus <see cref="WikiThema"/>.</summary>
    public required string Thema { get; set; }

    public int? VerantwortlichId { get; set; }
    public Mitarbeiter? Verantwortlich { get; set; }

    public int? VertreterId { get; set; }
    public Mitarbeiter? Vertreter { get; set; }

    public string? Prozess { get; set; }

    /// <summary>
    /// Reihenfolge der Abschnitte auf der Themenseite, von der Wiki-Redaktion
    /// angeordnet — Abschnitts-Schlüssel durch Komma getrennt (z. B.
    /// „stufen,addons,worldcheck"). null = Standardreihenfolge der Seite.
    /// Unbekannte Schlüssel werden ignoriert, fehlende hinten angehängt.
    /// </summary>
    public string? Abschnitte { get; set; }

    public DateTime? GeaendertAm { get; set; }
    public int? GeaendertVonId { get; set; }
    public Mitarbeiter? GeaendertVon { get; set; }
}
