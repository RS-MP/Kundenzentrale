namespace Rausoft.Erp.Dtos;

/// <summary>Benutzer der Kundenzentrale (Verwaltung im Admin-Bereich).</summary>
public record MitarbeiterEintrag(
    int Id,
    string? WindowsKonto,
    string Anzeigename,
    string Email,
    List<string> Rollen,
    bool Aktiv,
    bool IstIch,
    // Wiki-Themen, die dieser Redakteur pflegen darf (Schlüssel) — nur mit Rolle Wiki-Redaktion wirksam
    List<string> WikiThemen);

public record MitarbeiterAendern(string Anzeigename, string Email, List<string> Rollen, bool Aktiv, List<string>? WikiThemen = null);

/// <summary>Wiki-Thema zur Auswahl in der Benutzerverwaltung.</summary>
public record WikiThemaEintrag(string Schluessel, string Name, string Bereich);

/// <summary>Benutzer vorab anlegen (sonst entsteht er automatisch beim ersten Zugriff, ohne Rollen).</summary>
public record MitarbeiterAnlegen(string WindowsKonto, string Anzeigename);
