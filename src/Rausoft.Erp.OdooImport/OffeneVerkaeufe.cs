using System.Globalization;
using System.Text.Json;

namespace Rausoft.Erp.OdooImport;

/// <summary>
/// Angebot oder Auftrag aus Odoo für die Übersicht „Offene Angebote und Aufträge" — ohne Preise.
/// <see cref="FirmaId"/> ist der Firmenpartner (commercial_partner_id), über den der Kunde der
/// Kundenzentrale gefunden wird; Angebote gehen oft an einen Ansprechpartner.
/// </summary>
public sealed record OdooVerkauf(
    int Id, string Nummer, bool Bestaetigt, int? FirmaId, string? PartnerName,
    DateOnly? Datum, DateOnly? GueltigBis, string? Verkaeufer, string? VerkaeuferLogin,
    bool Abo, bool HatRechnung, string? Referenz, IReadOnlyList<string> Produkte);

/// <summary>
/// Gesendete Angebote (state = sent) und bestätigte Aufträge (sale/done) der letzten Zeit,
/// live aus Odoo. Nur lesend. Grundlage für zwei Listen (ANFORDERUNGEN.md, „Offene Angebote
/// und Aufträge"): Angebote, auf die der Kunde nicht reagiert hat, und bestätigte Aufträge,
/// zu denen noch niemand einen Vorgang angelegt hat.
/// </summary>
public static class OffeneVerkaeufe
{
    public const int MaxZeilen = 500;

    /// <summary>Odoo-Domain: gesendete Angebote und bestätigte Aufträge ab dem Stichtag.</summary>
    public static object[] Filter(DateOnly ab) =>
    [
        new object[] { "state", "in", new[] { "sent", "sale", "done" } },
        new object[] { "date_order", ">=", ab.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " 00:00:00" },
    ];

    public static async Task<List<OdooVerkauf>> LadenAsync(IOdooAufruf odoo, DateOnly ab)
    {
        var auftraege = await odoo.AusfuehrenAsync("sale.order", "search_read", [Filter(ab)],
            new Dictionary<string, object>
            {
                ["fields"] = new[]
                {
                    "id", "name", "state", "partner_id", "date_order", "validity_date", "user_id",
                    "is_subscription", "invoice_ids", "client_order_ref",
                },
                ["order"] = "date_order, id",
                ["limit"] = MaxZeilen,
            });
        if (auftraege.ValueKind != JsonValueKind.Array || auftraege.GetArrayLength() == 0)
            return [];
        var zeilen = auftraege.EnumerateArray().ToList();

        var firmen = await FirmenAsync(odoo, zeilen.Select(a => OdooJson.Many2OneId(a, "partner_id")).OfType<int>().Distinct().ToArray());
        var logins = await LoginsAsync(odoo, zeilen.Select(a => OdooJson.Many2OneId(a, "user_id")).OfType<int>().Distinct().ToArray());
        var produkte = await KundenAuftraege.ProdukteAsync(odoo, zeilen.Select(a => a.GetProperty("id").GetInt32()).ToArray());

        return zeilen.Select(a =>
        {
            var id = a.GetProperty("id").GetInt32();
            var partner = OdooJson.Many2OneId(a, "partner_id");
            var verkaeufer = OdooJson.Many2OneId(a, "user_id");
            return new OdooVerkauf(
                id,
                (OdooJson.Text(a, "name") ?? "?").ToUpperInvariant(),
                OdooJson.Text(a, "state") is "sale" or "done",
                partner is { } p ? firmen.GetValueOrDefault(p, p) : null,
                OdooJson.Many2OneLabel(a, "partner_id"),
                OdooJson.Zeitpunkt(a, "date_order") is { } z ? DateOnly.FromDateTime(z) : null,
                Tag(OdooJson.Text(a, "validity_date")),
                OdooJson.Many2OneLabel(a, "user_id"),
                verkaeufer is { } v ? logins.GetValueOrDefault(v) : null,
                OdooJson.Wahr(a, "is_subscription"),
                OdooJson.AnzahlIds(a, "invoice_ids") > 0,
                OdooJson.Text(a, "client_order_ref"),
                produkte.GetValueOrDefault(id) ?? []);
        }).ToList();
    }

    /// <summary>Odoo-Datum („2026-11-30") als Tag; alles andere (false, leer) ist kein Datum.</summary>
    public static DateOnly? Tag(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var tag) ? tag : null;

    private static async Task<Dictionary<int, int>> FirmenAsync(IOdooAufruf odoo, int[] partnerIds)
    {
        var firmen = new Dictionary<int, int>();
        if (partnerIds.Length == 0)
            return firmen;
        var partner = await odoo.AusfuehrenAsync("res.partner", "read", [partnerIds],
            new Dictionary<string, object> { ["fields"] = new[] { "commercial_partner_id" } });
        if (partner.ValueKind == JsonValueKind.Array)
            foreach (var p in partner.EnumerateArray())
                if (OdooJson.Many2OneId(p, "commercial_partner_id") is { } firma)
                    firmen[p.GetProperty("id").GetInt32()] = firma;
        return firmen;
    }

    private static async Task<Dictionary<int, string>> LoginsAsync(IOdooAufruf odoo, int[] benutzerIds)
    {
        var logins = new Dictionary<int, string>();
        if (benutzerIds.Length == 0)
            return logins;
        var benutzer = await odoo.AusfuehrenAsync("res.users", "read", [benutzerIds],
            new Dictionary<string, object> { ["fields"] = new[] { "login" } });
        if (benutzer.ValueKind == JsonValueKind.Array)
            foreach (var b in benutzer.EnumerateArray())
                if (OdooJson.Text(b, "login") is { } login)
                    logins[b.GetProperty("id").GetInt32()] = login;
        return logins;
    }
}
