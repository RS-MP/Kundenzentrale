using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Domain.Wiki;

/// <summary>
/// Wiki-Katalog „Sanktionslisten" (FEEDBACK Nr. 22): wie heißt die Liste offiziell,
/// bei uns und bei Kunden/Wettbewerbern, woher kommt sie, wie wird sie
/// importiert, wer überwacht sie (mit Vertretung) und wie ist der Prozess.
/// Startbestand aus der Sanktionslisten-Übersicht von sanktionslisten.de (12/2025).
/// </summary>
public class Sanktionsliste
{
    public int Id { get; set; }

    /// <summary>Gruppierung: Länder-/Regionscode, z. B. „EU", „US", „DE", „UN", „INT".</summary>
    public required string Region { get; set; }

    /// <summary>Gängiges Kürzel, z. B. „SDN", „CFSP".</summary>
    public string? Kuerzel { get; set; }

    /// <summary>Offizielle Bezeichnung der Liste.</summary>
    public required string Name { get; set; }

    /// <summary>Wie wir die Liste intern nennen (z. B. im Sanction Admin oder im Support).</summary>
    public string? BezeichnungIntern { get; set; }

    /// <summary>Andere gebräuchliche Namen (Kunden, Wettbewerber) — kommagetrennt, durchsuchbar.</summary>
    public string? Aliase { get; set; }

    public string? Beschreibung { get; set; }

    /// <summary>Für wen die Liste relevant ist (eigener Absatz — Grundlage für die Sanktionslisten-Übersicht).</summary>
    public string? Relevanz { get; set; }

    /// <summary>Finanzsanktionen, Exportkontrolle, Sonstige (ggf. mehrere, kommagetrennt).</summary>
    public string? Art { get; set; }

    public string? Herausgeber { get; set; }

    public string? Geltungsbereich { get; set; }

    /// <summary>Im Standardumfang von sanktionslisten.de (sonst optional verfügbar).</summary>
    public bool Standardumfang { get; set; }

    /// <summary>Offizielle Quelle bzw. Download der Liste.</summary>
    public string? DownloadUrl { get; set; }

    public SanktionslistenImport Import { get; set; } = SanktionslistenImport.Offen;

    /// <summary>Details zum Import, z. B. Intervall, Dateiformat, Besonderheiten.</summary>
    public string? ImportHinweis { get; set; }

    /// <summary>Wer überwacht die Liste (Aktualisierungen, Import, Auffälligkeiten).</summary>
    public int? VerantwortlichId { get; set; }
    public Mitarbeiter? Verantwortlich { get; set; }

    public int? VertreterId { get; set; }
    public Mitarbeiter? Vertreter { get; set; }

    /// <summary>Ablauf in Textform: Was ist bei einer Aktualisierung zu tun?</summary>
    public string? Prozess { get; set; }

    /// <summary>Ausgeblendet statt gelöscht (z. B. Liste eingestellt).</summary>
    public bool Aktiv { get; set; } = true;

    public DateTime? GeaendertAm { get; set; }
    public int? GeaendertVonId { get; set; }
    public Mitarbeiter? GeaendertVon { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

public enum SanktionslistenImport
{
    /// <summary>Noch nicht festgelegt.</summary>
    Offen = 0,
    /// <summary>Automatisch über den Sanction Admin.</summary>
    Automatisch = 1,
    /// <summary>Von Hand.</summary>
    Manuell = 2,
    NichtImEinsatz = 3,
}

/// <summary>
/// Standardprozess je Importart: gilt für alle Listen dieser Importart, die
/// keinen eigenen <see cref="Sanktionsliste.Prozess"/> haben — spart Doppelpflege.
/// </summary>
public class SanktionslistenStandardprozess
{
    public SanktionslistenImport Import { get; set; }

    public string? Text { get; set; }

    public DateTime? GeaendertAm { get; set; }
    public int? GeaendertVonId { get; set; }
    public Mitarbeiter? GeaendertVon { get; set; }
}
