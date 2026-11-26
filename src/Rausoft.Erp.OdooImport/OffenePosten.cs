using System.Globalization;
using System.Text.Json;
using Rausoft.Erp.Domain.Zahlungen;

namespace Rausoft.Erp.OdooImport;

/// <summary>
/// Offene Ausgangsrechnungen aus Odoo (gebucht, nicht oder nur teilweise bezahlt) — für die
/// Zuordnung der Zahlungseingänge und die Liste der ausstehenden Zahlungen. Nur lesend.
/// </summary>
public static class OffenePosten
{
    public const int MaxRechnungen = 2000;

    public static object[] Filter() =>
    [
        new object[] { "move_type", "=", "out_invoice" },
        new object[] { "state", "=", "posted" },
        new object[] { "payment_state", "in", new[] { "not_paid", "partial" } },
    ];

    public static async Task<List<OffeneRechnung>> LadenAsync(IOdooAufruf odoo)
    {
        var rechnungen = await odoo.AusfuehrenAsync("account.move", "search_read", [Filter()],
            new Dictionary<string, object>
            {
                ["fields"] = new[]
                {
                    "name", "partner_id", "commercial_partner_id", "invoice_date", "invoice_date_due",
                    "amount_total", "amount_residual", "currency_id", "payment_reference",
                },
                ["order"] = "invoice_date_due, name",
                ["limit"] = MaxRechnungen,
            });
        if (rechnungen.ValueKind != JsonValueKind.Array)
            return [];
        return rechnungen.EnumerateArray()
            .Where(r => OdooJson.Text(r, "name") is not null)
            .Select(r => new OffeneRechnung(
                OdooJson.Text(r, "name")!,
                OdooJson.Many2OneLabel(r, "commercial_partner_id") ?? OdooJson.Many2OneLabel(r, "partner_id"),
                OdooJson.Many2OneId(r, "commercial_partner_id") ?? OdooJson.Many2OneId(r, "partner_id"),
                OffeneVerkaeufe.Tag(OdooJson.Text(r, "invoice_date")),
                OffeneVerkaeufe.Tag(OdooJson.Text(r, "invoice_date_due")),
                OdooJson.Dezimal(r, "amount_total"),
                OdooJson.Dezimal(r, "amount_residual"),
                Waehrung(OdooJson.Many2OneLabel(r, "currency_id")),
                OdooJson.Text(r, "payment_reference")))
            .ToList();
    }

    /// <summary>Odoo nennt die Währung „EUR"; fehlt sie, gilt die Firmenwährung Euro.</summary>
    internal static string Waehrung(string? label) =>
        label?.Trim().ToUpperInvariant() is { Length: 3 } w ? w : "EUR";
}

/// <param name="Gebucht">Die Zahlung steht in Odoo, die Rechnung ist ausgeglichen.</param>
/// <param name="Unklar">Odoo wurde zum Buchen aufgerufen, das Ergebnis ist nicht gesichert — nie automatisch wiederholen.</param>
/// <param name="Meldung">Für die Oberfläche; enthält nie Odoos Rohantwort.</param>
public sealed record BuchungsErgebnis(bool Gebucht, bool Unklar, int? ZahlungId, string Meldung);

/// <summary>
/// Registriert einen Zahlungseingang an einer Rechnung in Odoo — derselbe Weg wie der Knopf
/// „Zahlung registrieren" an der Rechnung (Assistent account.payment.register). Die Rechnung
/// wird in der Zieldatenbank am Namen gesucht, nie über eine mitgebrachte Nummer: Test- und
/// Produktiv-Datenbank haben verschiedene Nummern. Vor dem Schreiben wird der offene Betrag
/// geprüft — ist die Rechnung inzwischen bezahlt oder der Betrag ein anderer, wird nicht gebucht.
/// </summary>
public static class ZahlungsBucher
{
    public static async Task<BuchungsErgebnis> BuchenAsync(
        IOdooAufruf odoo, string rechnung, decimal betrag, DateOnly zahltag, string? journalName, string verwendungszweck)
    {
        // 1. Rechnung in der Zieldatenbank suchen und den offenen Betrag prüfen
        var treffer = await odoo.AusfuehrenAsync("account.move", "search_read",
            [new object[]
            {
                new object[] { "name", "=", rechnung },
                new object[] { "move_type", "=", "out_invoice" },
                new object[] { "state", "=", "posted" },
            }],
            new Dictionary<string, object> { ["fields"] = new[] { "id", "amount_residual", "payment_state", "currency_id" }, ["limit"] = 2 });
        if (treffer.ValueKind != JsonValueKind.Array || treffer.GetArrayLength() == 0)
            return Abgelehnt($"Die Rechnung {rechnung} gibt es in dieser Odoo-Datenbank nicht (oder sie ist nicht gebucht).");
        if (treffer.GetArrayLength() > 1)
            return Abgelehnt($"Die Rechnungsnummer {rechnung} ist in Odoo nicht eindeutig.");
        var move = treffer[0];
        var moveId = move.GetProperty("id").GetInt32();
        var offen = OdooJson.Dezimal(move, "amount_residual");
        if (OffenePosten.Waehrung(OdooJson.Many2OneLabel(move, "currency_id")) != "EUR")
            return Abgelehnt($"Die Rechnung {rechnung} lautet nicht auf Euro.");
        if (offen == 0)
            return Abgelehnt($"Die Rechnung {rechnung} ist in Odoo bereits bezahlt.");
        if (offen < betrag)
            return Abgelehnt($"An der Rechnung {rechnung} sind in Odoo nur noch {offen.ToString("N2", Deutsch)} € offen — der Eingang ist höher.");

        // 2. Bankjournal am Namen bestimmen (keine festen Odoo-Nummern in der Kundenzentrale)
        var journale = await odoo.AusfuehrenAsync("account.journal", "search_read",
            [new object[] { new object[] { "type", "=", "bank" } }],
            new Dictionary<string, object> { ["fields"] = new[] { "id", "name" }, ["order"] = "sequence, id" });
        var alle = journale.ValueKind == JsonValueKind.Array
            ? journale.EnumerateArray().Select(j => (Id: j.GetProperty("id").GetInt32(), Name: OdooJson.Text(j, "name") ?? "")).ToList()
            : [];
        var gewuenscht = journalName?.Trim();
        var passend = string.IsNullOrEmpty(gewuenscht)
            ? alle
            : alle.Where(j => j.Name.Equals(gewuenscht, StringComparison.OrdinalIgnoreCase)).ToList();
        if (passend.Count != 1)
            return Abgelehnt(string.IsNullOrEmpty(gewuenscht)
                ? alle.Count == 0
                    ? "In Odoo gibt es kein Bankjournal."
                    : $"In Odoo gibt es mehrere Bankjournale ({string.Join(", ", alle.Select(j => j.Name))}) — bitte den Namen unter Administration → Kontoauszüge eintragen."
                : $"Das Bankjournal „{gewuenscht}“ gibt es in Odoo nicht (vorhanden: {string.Join(", ", alle.Select(j => j.Name))}).");

        // 3. Assistent „Zahlung registrieren" vorbereiten — nur Felder setzen, die diese Odoo-Version kennt
        var kontext = new Dictionary<string, object>
        {
            ["active_model"] = "account.move",
            ["active_ids"] = new[] { moveId },
            ["active_id"] = moveId,
        };
        var felder = await odoo.AusfuehrenAsync("account.payment.register", "fields_get", [],
            new Dictionary<string, object> { ["attributes"] = new[] { "type" } });
        bool Kennt(string feld) => felder.ValueKind == JsonValueKind.Object && felder.TryGetProperty(feld, out _);
        if (!Kennt("amount") || !Kennt("payment_date") || !Kennt("journal_id"))
            return Abgelehnt("Diese Odoo-Version kennt den Assistenten „Zahlung registrieren“ nicht in der erwarteten Form — es wurde nichts gebucht.");
        var werte = new Dictionary<string, object>
        {
            ["journal_id"] = passend[0].Id,
            ["payment_date"] = zahltag.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["amount"] = betrag,
        };
        if (Kennt("communication"))
            werte["communication"] = verwendungszweck.Length <= 140 ? verwendungszweck : verwendungszweck[..140];
        var assistent = await odoo.AusfuehrenAsync("account.payment.register", "create", [werte],
            new Dictionary<string, object> { ["context"] = kontext });
        if (assistent.ValueKind != JsonValueKind.Number)
            return Abgelehnt("Odoo hat den Assistenten „Zahlung registrieren“ nicht angelegt — es wurde nichts gebucht.");

        // 4. Buchen. Ab hier ist bei einem Fehler unklar, ob Odoo die Zahlung angelegt hat.
        int? zahlungId = null;
        try
        {
            var aktion = await odoo.AusfuehrenAsync("account.payment.register", "action_create_payments",
                [new[] { assistent.GetInt32() }], new Dictionary<string, object> { ["context"] = kontext });
            if (aktion.ValueKind == JsonValueKind.Object)
                zahlungId = OdooJson.Zahl(aktion, "res_id");

            // 5. Gegenprobe: Der offene Betrag muss um den Eingang gesunken sein
            var danach = await odoo.AusfuehrenAsync("account.move", "read", [new[] { moveId }],
                new Dictionary<string, object> { ["fields"] = new[] { "amount_residual" } });
            var rest = danach.ValueKind == JsonValueKind.Array && danach.GetArrayLength() == 1
                ? OdooJson.Dezimal(danach[0], "amount_residual")
                : offen;
            if (rest != offen - betrag)
                return new BuchungsErgebnis(false, true, zahlungId,
                    $"Odoo hat die Zahlung angenommen, an der Rechnung {rechnung} sind aber {rest.ToString("N2", Deutsch)} € offen statt {(offen - betrag).ToString("N2", Deutsch)} € — bitte in Odoo prüfen.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or OdooFehlerException)
        {
            return new BuchungsErgebnis(false, true, zahlungId, ex is OdooFehlerException o
                ? $"Odoo hat beim Buchen einen Fehler gemeldet ({o.Art ?? "unbekannt"}) — bitte an der Rechnung {rechnung} in Odoo prüfen, ob die Zahlung angelegt wurde."
                : $"Die Verbindung zu Odoo brach beim Buchen ab — bitte an der Rechnung {rechnung} in Odoo prüfen, ob die Zahlung angelegt wurde.");
        }
        return new BuchungsErgebnis(true, false, zahlungId,
            $"Zahlung über {betrag.ToString("N2", Deutsch)} € an Rechnung {rechnung} in Odoo registriert (Journal {passend[0].Name}).");
    }

    private static readonly CultureInfo Deutsch = CultureInfo.GetCultureInfo("de-DE");

    private static BuchungsErgebnis Abgelehnt(string meldung) => new(false, false, null, meldung);
}
