namespace Rausoft.Erp.Domain.Shared;

/// <summary>
/// rausoft-Benutzer. Authentifizierung läuft vollständig über Entra ID —
/// hier liegen keine Passwörter oder Tokens, nur die Zuordnung
/// Entra-Konto → interner Datensatz.
/// </summary>
public class Mitarbeiter
{
    public int Id { get; set; }

    /// <summary>Objekt-ID des M365-Kontos in Entra ID.</summary>
    public Guid EntraObjectId { get; set; }

    public required string Anzeigename { get; set; }

    public required string Email { get; set; }

    public int? BereichId { get; set; }
    public Bereich? Bereich { get; set; }

    public MitarbeiterRolle Rollen { get; set; }

    /// <summary>Deaktivieren statt Löschen — Timeline und Audit-Log referenzieren Mitarbeiter dauerhaft.</summary>
    public bool Aktiv { get; set; } = true;
}

[Flags]
public enum MitarbeiterRolle
{
    Keine = 0,
    Admin = 1,
    Vertrieb = 2,
    Support = 4,
    Buchhaltung = 8,
}
