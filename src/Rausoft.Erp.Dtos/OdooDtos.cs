namespace Rausoft.Erp.Dtos;

/// <summary>Zustand des manuellen Odoo-Imports (Administration → Odoo-Anbindung).</summary>
public record OdooImportStatus(
    bool Laeuft,
    DateTime? GestartetAm,
    DateTime? BeendetAm,
    string? Fehler,
    string LetzterLauf);
