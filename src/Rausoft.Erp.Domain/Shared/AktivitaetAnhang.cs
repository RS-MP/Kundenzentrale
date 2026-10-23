namespace Rausoft.Erp.Domain.Shared;

/// <summary>
/// Datei an einem Timeline-Eintrag (E-Mail-Anhang, Notiz-Anhang). Entweder liegt
/// die Datei in der Dokumentablage (Einstellung „Dokumentablage.Verzeichnis")
/// unter <see cref="RelativerPfad"/>, oder — Standard beim Odoo-Import im
/// Testbetrieb — nur der Verweis auf das Original in Odoo (<see cref="OdooId"/>,
/// <see cref="RelativerPfad"/> = null). Vor der Odoo-Abschaltung werden die
/// Dateien einmal archiviert (Import mit „Anhänge herunterladen").
/// </summary>
public class AktivitaetAnhang
{
    public int Id { get; set; }

    public int AktivitaetId { get; set; }
    public Aktivitaet? Aktivitaet { get; set; }

    /// <summary>Ursprünglicher Dateiname, so wie er angezeigt wird.</summary>
    public required string Dateiname { get; set; }

    public string? MimeType { get; set; }

    public long Groesse { get; set; }

    /// <summary>
    /// Pfad relativ zum Basisverzeichnis der Dokumentablage, z. B. „25757\Odoo-Chatter\4711_Angebot.pdf";
    /// null = Datei nicht lokal, nur das Original in Odoo.
    /// </summary>
    public string? RelativerPfad { get; set; }

    /// <summary>Odoo ir.attachment-Id beim Chatter-Import — verhindert doppelte Übernahme.</summary>
    public int? OdooId { get; set; }
}
