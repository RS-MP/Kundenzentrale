namespace Rausoft.Erp.Dtos;

/// <summary>Zustand des manuellen Odoo-Imports (Administration → Odoo-Anbindung).</summary>
public record OdooImportStatus(
    bool Laeuft,
    DateTime? GestartetAm,
    DateTime? BeendetAm,
    string? Fehler,
    string LetzterLauf);

/// <summary>Stand des Chatter-Laufs (Analyse, Dry-Run oder Import) samt gepuffertem Bericht.</summary>
public record ChatterImportStatus(
    bool Laeuft,
    string? Modus,
    string? Kunde,
    DateTime? GestartetAm,
    DateTime? BeendetAm,
    string? Fehler,
    string Bericht);

/// <summary>
/// Modus „analyse", „dryrun" oder „import"; Kunde = KundenNr oder Odoo-ID, leer = alle.
/// <paramref name="AnhaengeLaden"/> = Dateien zusätzlich in die Dokumentablage kopieren
/// (Archiv vor der Odoo-Abschaltung); sonst nur Verweise auf Odoo.
/// </summary>
public record ChatterImportStart(string Modus, string? Kunde, bool AnhaengeLaden = false);
