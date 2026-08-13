using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Domain.Contracts;

/// <summary>
/// Unveränderliche Dokumentversion eines Vertragsbestandteils (Leitplanke).
/// Die Datei bleibt im bestehenden Ablageverzeichnis; gespeichert werden
/// Pfad + SHA-256-Prüfsumme. Zeilen werden nie geändert oder gelöscht —
/// eine neue Version ist eine neue Zeile (im DbContext technisch erzwungen).
/// </summary>
public class Vertragsdokument
{
    public int Id { get; set; }

    public int VertragsbestandteilId { get; set; }
    public Vertragsbestandteil? Vertragsbestandteil { get; set; }

    /// <summary>Aufsteigend je Bestandteil, eindeutig.</summary>
    public int Version { get; set; }

    /// <summary>UNC- oder lokaler Pfad im bestehenden Ablageverzeichnis.</summary>
    public required string Dateipfad { get; set; }

    public required string DateinameOriginal { get; set; }

    /// <summary>SHA-256-Prüfsumme (Hex, 64 Zeichen).</summary>
    public required string Sha256 { get; set; }

    public DateTime ErstelltAm { get; set; }

    public int ErstelltVonId { get; set; }
    public Mitarbeiter? ErstelltVon { get; set; }
}
