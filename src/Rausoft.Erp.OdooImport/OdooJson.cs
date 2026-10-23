using System.Globalization;
using System.Text.Json;

namespace Rausoft.Erp.OdooImport;

/// <summary>Kleine Lesehelfer für Odoo-Datensätze (Odoo liefert false statt null).</summary>
internal static class OdooJson
{
    public static string? Text(JsonElement element, string feld) =>
        element.TryGetProperty(feld, out var wert) && wert.ValueKind == JsonValueKind.String
            ? (string.IsNullOrWhiteSpace(wert.GetString()) ? null : wert.GetString()!.Trim())
            : null;

    public static bool Wahr(JsonElement element, string feld) =>
        element.TryGetProperty(feld, out var wert) && wert.ValueKind == JsonValueKind.True;

    public static int? Zahl(JsonElement element, string feld) =>
        element.TryGetProperty(feld, out var wert) && wert.ValueKind == JsonValueKind.Number
            ? wert.GetInt32()
            : null;

    public static int? Many2OneId(JsonElement element, string feld) =>
        element.TryGetProperty(feld, out var wert) && wert.ValueKind == JsonValueKind.Array && wert.GetArrayLength() > 0
            ? wert[0].GetInt32()
            : null;

    public static string? Many2OneLabel(JsonElement element, string feld) =>
        element.TryGetProperty(feld, out var wert) && wert.ValueKind == JsonValueKind.Array && wert.GetArrayLength() > 1
            ? wert[1].GetString()
            : null;

    public static int AnzahlIds(JsonElement element, string feld) =>
        element.TryGetProperty(feld, out var wert) && wert.ValueKind == JsonValueKind.Array
            ? wert.GetArrayLength()
            : 0;

    public static List<int> Ids(JsonElement element, string feld) =>
        element.TryGetProperty(feld, out var wert) && wert.ValueKind == JsonValueKind.Array
            ? wert.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number).Select(e => e.GetInt32()).ToList()
            : [];

    /// <summary>Odoo-Zeitstempel ("2024-05-03 10:12:33", UTC) → lokale Zeit.</summary>
    public static DateTime? Zeitpunkt(JsonElement element, string feld)
    {
        var text = Text(element, feld);
        if (text is null)
            return null;
        return DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc)
            ? utc.ToLocalTime()
            : null;
    }

    /// <summary>
    /// Alter oder neuer Wert einer Feldänderung (mail.tracking.value). Odoo füllt je
    /// nach Feldtyp eine andere Spalte und lässt die übrigen auf 0/false — Text und
    /// Datum haben Vorrang, Zahlen 0 nur, wenn sonst nichts gesetzt ist.
    /// </summary>
    public static string TrackingWert(JsonElement w, string seite)
    {
        foreach (var f in new[] { "char", "text", "datetime" })
            if (Text(w, $"{seite}_value_{f}") is { } s)
                return s;
        if (w.TryGetProperty($"{seite}_value_float", out var fl) && fl.ValueKind == JsonValueKind.Number && fl.GetDouble() != 0)
            return fl.GetDouble().ToString(CultureInfo.GetCultureInfo("de-DE"));
        if (w.TryGetProperty($"{seite}_value_integer", out var i) && i.ValueKind == JsonValueKind.Number && i.GetInt64() != 0)
            return i.GetInt64().ToString(CultureInfo.InvariantCulture);
        return "—";
    }

    /// <summary>Feldbezeichnung ohne angehängten Modellnamen: „KundenNr (Contact)" → „KundenNr".</summary>
    public static string FeldName(string label)
    {
        var klammer = label.LastIndexOf(" (", StringComparison.Ordinal);
        return klammer > 0 && label.EndsWith(')') ? label[..klammer] : label;
    }

    /// <summary>Gruppenwert aus formatted_read_group lesbar machen (many2one = [id, name]).</summary>
    public static string GruppenWert(JsonElement gruppe, string feld)
    {
        if (!gruppe.TryGetProperty(feld, out var wert))
            return "(leer)";
        return wert.ValueKind switch
        {
            JsonValueKind.Array when wert.GetArrayLength() > 1 => wert[1].GetString() ?? "(leer)",
            JsonValueKind.String => wert.GetString()!,
            JsonValueKind.Number => wert.ToString(),
            _ => "(leer)",
        };
    }
}
