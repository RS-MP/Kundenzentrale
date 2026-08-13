namespace Rausoft.Erp.Domain.Shared;

/// <summary>
/// Zentrales Audit-Log (Leitplanke). Wird beim Speichern automatisch aus dem
/// ChangeTracker befüllt; Änderungen als JSON (alte/neue Werte).
/// </summary>
public class AuditLogEintrag
{
    public long Id { get; set; }

    public DateTime Zeitpunkt { get; set; }

    public int? MitarbeiterId { get; set; }

    /// <summary>Name der geänderten Entität, z. B. "Kunde".</summary>
    public required string Entitaet { get; set; }

    public int EntitaetId { get; set; }

    public AuditAktion Aktion { get; set; }

    /// <summary>Geänderte Eigenschaften als JSON: { "Feld": { "alt": …, "neu": … } }</summary>
    public string? AenderungenJson { get; set; }
}

public enum AuditAktion
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
}
