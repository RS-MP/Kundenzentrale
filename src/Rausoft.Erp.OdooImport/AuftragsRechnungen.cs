using System.Globalization;
using System.Text.Json;

namespace Rausoft.Erp.OdooImport;

/// <summary>Gebuchte Ausgangsrechnung aus Odoo mit ihrer Herkunft (invoice_origin, z. B. „S01194, S01195").</summary>
public sealed record OdooRechnung(string Name, string? Herkunft, DateOnly? Datum);

/// <summary>
/// Gebuchte Rechnungen zu Odoo-Aufträgen — für den Vorgangsschritt „Rechnung in Odoo stellen"
/// (Übergangszeit, ANFORDERUNGEN.md „Vorgänge"). Nur lesend. Zugeordnet wird exakt über die
/// Auftragsnummer in invoice_origin, nicht über den Verlaufsstrang: ein Strang kann mehrere
/// Aufträge bündeln (z. B. Abo-Verlängerungen derselben Verkaufschance).
/// </summary>
public static class AuftragsRechnungen
{
    /// <summary>Odoo-Domain: gebuchte Ausgangsrechnungen, deren Herkunft eine der Nummern enthält.</summary>
    public static object[] Filter(IReadOnlyCollection<string> auftraege)
    {
        if (auftraege.Count == 0)
            throw new ArgumentException("Mindestens ein Auftrag.", nameof(auftraege));
        var domain = new List<object>
        {
            new object[] { "move_type", "=", "out_invoice" },
            new object[] { "state", "=", "posted" },
        };
        // Präfix-Notation: n Bedingungen brauchen n-1 vorangestellte ODER
        domain.AddRange(Enumerable.Repeat<object>("|", auftraege.Count - 1));
        domain.AddRange(auftraege.Select(a => (object)new object[] { "invoice_origin", "ilike", a }));
        return domain.ToArray();
    }

    public static async Task<List<OdooRechnung>> LadenAsync(OdooClient odoo, IReadOnlyCollection<string> auftraege)
    {
        if (auftraege.Count == 0)
            return [];
        var ergebnis = await odoo.AusfuehrenAsync("account.move", "search_read", [Filter(auftraege)],
            new Dictionary<string, object>
            {
                ["fields"] = new[] { "name", "invoice_origin", "invoice_date" },
                ["limit"] = 2000,
            });
        if (ergebnis.ValueKind != JsonValueKind.Array)
            return [];
        return ergebnis.EnumerateArray()
            .Select(r => new OdooRechnung(
                OdooJson.Text(r, "name") ?? "Rechnung",
                OdooJson.Text(r, "invoice_origin"),
                DateOnly.TryParse(OdooJson.Text(r, "invoice_date"), CultureInfo.InvariantCulture, out var d) ? d : null))
            .ToList();
    }

    /// <summary>
    /// Je Auftrag die erste gebuchte Rechnung, deren Herkunft die Nummer exakt enthält
    /// („S0119" trifft nicht „S01194").
    /// </summary>
    public static Dictionary<string, OdooRechnung> Zuordnen(IEnumerable<OdooRechnung> rechnungen, IEnumerable<string> auftraege)
    {
        var gesucht = new HashSet<string>(auftraege, StringComparer.OrdinalIgnoreCase);
        var treffer = new Dictionary<string, OdooRechnung>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rechnungen.OrderBy(r => r.Datum ?? DateOnly.MaxValue).ThenBy(r => r.Name, StringComparer.Ordinal))
            foreach (var nummer in VorgangsName.Herkunft(r.Herkunft))
                if (gesucht.Contains(nummer))
                    treffer.TryAdd(nummer, r);
        return treffer;
    }
}
