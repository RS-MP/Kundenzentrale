using System.Text.Json;

namespace Rausoft.Erp.OdooImport;

/// <summary>Mailvorlage aus Odoo (mail.template) im Rohzustand — Umsetzung: <c>OdooMailvorlage</c>.</summary>
public sealed record OdooMailVorlage(int Id, string Name, string? Betreff, string? BodyHtml, string? Modell, DateTime? Stand);

/// <summary>
/// Liest alle Mailvorlagen aus Odoo, in der deutschen Fassung. Nur lesend. Der Text kommt als
/// HTML (body_html) und behält damit seine Formatierung.
/// </summary>
public static class MailVorlagenLeser
{
    public const int MaxVorlagen = 1000;

    public static async Task<List<OdooMailVorlage>> LadenAsync(IOdooAufruf odoo)
    {
        var vorlagen = await odoo.AusfuehrenAsync("mail.template", "search_read", [Array.Empty<object>()],
            new Dictionary<string, object>
            {
                ["fields"] = new[] { "id", "name", "subject", "body_html", "model", "write_date" },
                ["order"] = "model, name, id",
                ["limit"] = MaxVorlagen,
                ["context"] = new Dictionary<string, object> { ["lang"] = "de_DE" },
            });
        if (vorlagen.ValueKind != JsonValueKind.Array)
            return [];
        return vorlagen.EnumerateArray()
            .Select(v => new OdooMailVorlage(
                v.GetProperty("id").GetInt32(),
                OdooJson.Text(v, "name") ?? "(ohne Namen)",
                OdooJson.Text(v, "subject"),
                OdooJson.Text(v, "body_html"),
                OdooJson.Text(v, "model"),
                OdooJson.ZeitpunktUtc(v, "write_date")))
            .ToList();
    }
}
