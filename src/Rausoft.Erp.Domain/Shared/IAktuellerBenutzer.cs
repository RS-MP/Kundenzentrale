namespace Rausoft.Erp.Domain.Shared;

/// <summary>
/// Liefert die Identität des angemeldeten Benutzers (Entra-ID-Token oder
/// Windows-Konto aus der lokalen AD). Wird u. a. vom Audit-Log genutzt,
/// um Änderungen einem Mitarbeiter zuzuordnen.
/// </summary>
public interface IAktuellerBenutzer
{
    /// <summary>Entra-Objekt-ID (oid-Claim) des angemeldeten Kontos; null bei Systemvorgängen/Windows-Auth.</summary>
    Guid? EntraObjectId { get; }

    /// <summary>Windows-Konto (z. B. "RS\mp") bei Windows-Authentifizierung; null sonst.</summary>
    string? WindowsKonto { get; }
}
