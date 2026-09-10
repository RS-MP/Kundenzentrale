namespace Rausoft.Erp.Dtos;

/// <summary>Benutzer der Kundenzentrale (Verwaltung im Admin-Bereich).</summary>
public record MitarbeiterEintrag(
    int Id,
    string? WindowsKonto,
    string Anzeigename,
    string Email,
    List<string> Rollen,
    bool Aktiv,
    bool IstIch);

public record MitarbeiterAendern(string Anzeigename, string Email, List<string> Rollen, bool Aktiv);

/// <summary>Benutzer vorab anlegen (sonst entsteht er automatisch beim ersten Zugriff, ohne Rollen).</summary>
public record MitarbeiterAnlegen(string WindowsKonto, string Anzeigename);
