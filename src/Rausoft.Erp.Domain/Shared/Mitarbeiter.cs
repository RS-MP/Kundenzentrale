namespace Rausoft.Erp.Domain.Shared;

/// <summary>
/// rausoft-Benutzer. Authentifizierung läuft über die lokale AD
/// (Windows-Authentifizierung) oder Entra ID — hier liegen keine Passwörter
/// oder Tokens, nur die Zuordnung Konto → interner Datensatz.
/// </summary>
public class Mitarbeiter
{
    public int Id { get; set; }

    /// <summary>Objekt-ID des M365-Kontos in Entra ID (falls Entra genutzt wird).</summary>
    public Guid? EntraObjectId { get; set; }

    /// <summary>Windows-Konto in der lokalen AD, z. B. "RS\mp" (falls Windows-Auth genutzt wird).</summary>
    public string? WindowsKonto { get; set; }

    /// <summary>Filter „gehört dieses Konto zu diesem Mitarbeiter?" — eine Stelle für alle Abfragen.</summary>
    public static System.Linq.Expressions.Expression<Func<Mitarbeiter, bool>> KontoFilter(
        Guid? entraObjectId, string? windowsKonto) =>
        m => (entraObjectId != null && m.EntraObjectId == entraObjectId)
             || (windowsKonto != null && m.WindowsKonto == windowsKonto);

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
