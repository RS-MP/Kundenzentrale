namespace Rausoft.Erp.Domain.Shared;

/// <summary>
/// Liefert die Identität des angemeldeten Benutzers (aus dem Entra-ID-Token).
/// Wird u. a. vom Audit-Log genutzt, um Änderungen einem Mitarbeiter zuzuordnen.
/// </summary>
public interface IAktuellerBenutzer
{
    /// <summary>Entra-Objekt-ID (oid-Claim) des angemeldeten Kontos; null bei Systemvorgängen.</summary>
    Guid? EntraObjectId { get; }
}
