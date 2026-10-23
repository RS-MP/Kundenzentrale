namespace Rausoft.Erp.OdooImport;

/// <summary>Anzeigenamen für Odoo-Vorgänge in der Kundenakte.</summary>
public static class VorgangsName
{
    /// <summary>
    /// Stellt die Art vor den Namen, außer der Name beginnt schon damit:
    /// („Verkaufschance", „Verkaufschance von RHB") → „Verkaufschance von RHB",
    /// („Auftrag", „S01194") → „Auftrag S01194".
    /// </summary>
    public static string MitPraefix(string praefix, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return praefix;
        var n = name.Trim();
        return n.StartsWith(praefix, StringComparison.OrdinalIgnoreCase) ? n : $"{praefix} {n}";
    }

    /// <summary>Auftragsnummern aus dem Rechnungsfeld invoice_origin („S01194, S01195").</summary>
    public static IEnumerable<string> Herkunft(string? invoiceOrigin) =>
        string.IsNullOrWhiteSpace(invoiceOrigin)
            ? []
            : invoiceOrigin.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
