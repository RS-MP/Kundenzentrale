using System.Text.Json;

namespace Rausoft.Erp.OdooImport;

/// <summary>Bestätigter Odoo-Auftrag eines Kunden — Auswahl beim Anlegen eines Vorgangs (ohne Preise).</summary>
public sealed record OdooKundenAuftrag(string Nummer, DateOnly? Datum, bool Abo, string? Referenz, IReadOnlyList<string> Produkte);

/// <summary>
/// Bestätigte Aufträge eines Kunden live aus Odoo, damit beim Anlegen eines Vorgangs
/// niemand die Auftragsnummer heraussuchen muss. Nur lesend. Gesucht wird über den
/// Firmenpartner und alle seine Ansprechpartner (child_of), neueste zuerst.
/// </summary>
public static class KundenAuftraege
{
    public const int MaxAuftraege = 20;
    private const int MaxProdukteImText = 3;

    /// <summary>Odoo-Domain: bestätigte Aufträge (sale/done) des Partners und seiner Kontakte.</summary>
    public static object[] Filter(int partnerId) =>
    [
        new object[] { "partner_id", "child_of", partnerId },
        new object[] { "state", "in", new[] { "sale", "done" } },
    ];

    public static async Task<List<OdooKundenAuftrag>> LadenAsync(OdooClient odoo, int partnerId)
    {
        var auftraege = await odoo.AusfuehrenAsync("sale.order", "search_read", [Filter(partnerId)],
            new Dictionary<string, object>
            {
                ["fields"] = new[] { "id", "name", "date_order", "is_subscription", "client_order_ref" },
                ["order"] = "date_order desc, id desc",
                ["limit"] = MaxAuftraege,
            });
        if (auftraege.ValueKind != JsonValueKind.Array || auftraege.GetArrayLength() == 0)
            return [];

        var ids = auftraege.EnumerateArray().Select(a => a.GetProperty("id").GetInt32()).ToArray();
        var produkte = await ProdukteAsync(odoo, ids);

        return auftraege.EnumerateArray()
            .Select(a => new OdooKundenAuftrag(
                OdooJson.Text(a, "name") ?? "?",
                OdooJson.Zeitpunkt(a, "date_order") is { } z ? DateOnly.FromDateTime(z) : null,
                OdooJson.Wahr(a, "is_subscription"),
                OdooJson.Text(a, "client_order_ref"),
                produkte.GetValueOrDefault(a.GetProperty("id").GetInt32()) ?? []))
            .ToList();
    }

    /// <summary>Produkte je Auftrag (ohne Abschnitts- und Notizzeilen), in der Reihenfolge des Auftrags.</summary>
    public static async Task<Dictionary<int, List<string>>> ProdukteAsync(IOdooAufruf odoo, int[] auftragIds)
    {
        var produkte = new Dictionary<int, List<string>>();
        if (auftragIds.Length == 0)
            return produkte;
        var zeilen = await odoo.AusfuehrenAsync("sale.order.line", "search_read",
            [new object[]
            {
                new object[] { "order_id", "in", auftragIds },
                new object[] { "display_type", "=", false },
            }],
            new Dictionary<string, object>
            {
                ["fields"] = new[] { "order_id", "product_id", "name" },
                ["order"] = "order_id, sequence, id",
            });
        if (zeilen.ValueKind == JsonValueKind.Array)
            foreach (var z in zeilen.EnumerateArray())
                if (OdooJson.Many2OneId(z, "order_id") is { } auftrag
                    && (OdooJson.Many2OneLabel(z, "product_id") ?? ErsteZeile(OdooJson.Text(z, "name"))) is { } produkt)
                    (produkte.TryGetValue(auftrag, out var liste) ? liste : produkte[auftrag] = []).Add(produkt);
        return produkte;
    }

    /// <summary>Kurzer Inhalt zum Wiedererkennen, z. B. „Shared Cloud Basis, Backup, Exchange +2".</summary>
    public static string? Inhalt(IEnumerable<string> produkte)
    {
        var verschieden = produkte.Select(p => p.Trim()).Where(p => p != "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (verschieden.Count == 0)
            return null;
        var text = string.Join(", ", verschieden.Take(MaxProdukteImText));
        return verschieden.Count > MaxProdukteImText ? $"{text} +{verschieden.Count - MaxProdukteImText}" : text;
    }

    private static string? ErsteZeile(string? text) => text?.Split('\n')[0].Trim() is { Length: > 0 } z ? z : null;
}
