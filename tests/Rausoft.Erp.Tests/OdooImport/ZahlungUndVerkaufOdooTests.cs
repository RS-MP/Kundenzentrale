using System.Text.Json;
using Rausoft.Erp.OdooImport;

namespace Rausoft.Erp.Tests.OdooImport;

/// <summary>
/// Zahlung in Odoo registrieren, offene Posten und offene Angebote lesen — gegen ein
/// nachgestelltes Odoo mit erfundenen Daten. Entscheidend sind die Schutzregeln: nie
/// doppelt buchen, nie über den offenen Betrag hinaus, kein Raten beim Journal.
/// </summary>
public class ZahlungUndVerkaufOdooTests
{
    /// <summary>Odoo-Attrappe: beantwortet Aufrufe über eine Funktion und merkt sich, was gerufen wurde.</summary>
    private sealed class Attrappe(Func<string, string, object[], Dictionary<string, object>?, object?> antwort) : IOdooAufruf
    {
        public List<(string Modell, string Methode, object[] Args, Dictionary<string, object>? Kwargs)> Aufrufe { get; } = [];

        public Task<JsonElement> AusfuehrenAsync(string modell, string methode, object[] args, Dictionary<string, object>? kwargs = null)
        {
            Aufrufe.Add((modell, methode, args, kwargs));
            return Task.FromResult(JsonSerializer.SerializeToElement(antwort(modell, methode, args, kwargs)));
        }

        public bool Gerufen(string modell, string methode) => Aufrufe.Any(a => a.Modell == modell && a.Methode == methode);
    }

    private static readonly DateOnly Zahltag = new(2026, 11, 24);

    /// <summary>Odoo mit einer Rechnung RE/2026/00123; das Buchen senkt den offenen Betrag.</summary>
    private static Attrappe OdooMitRechnung(decimal offen, string[] journale, Action? beimBuchen = null, bool buchtWirklich = true)
    {
        var rest = offen;
        return new Attrappe((modell, methode, args, _) => (modell, methode) switch
        {
            ("account.move", "search_read") => new[] { new Dictionary<string, object> { ["id"] = 77, ["amount_residual"] = rest, ["payment_state"] = "not_paid", ["currency_id"] = new object[] { 1, "EUR" } } },
            ("account.move", "read") => new[] { new Dictionary<string, object> { ["id"] = 77, ["amount_residual"] = rest } },
            ("account.journal", "search_read") => journale.Select((name, i) => new Dictionary<string, object> { ["id"] = 10 + i, ["name"] = name }).ToArray(),
            ("account.payment.register", "fields_get") => new Dictionary<string, object>
            {
                ["amount"] = new { type = "monetary" }, ["payment_date"] = new { type = "date" },
                ["journal_id"] = new { type = "many2one" }, ["communication"] = new { type = "char" },
            },
            ("account.payment.register", "create") => 501,
            ("account.payment.register", "action_create_payments") => Buchen(),
            _ => throw new InvalidOperationException($"Unerwarteter Aufruf {modell}.{methode}"),
        });

        object Buchen()
        {
            beimBuchen?.Invoke();
            if (buchtWirklich)
                rest = 0;
            return new Dictionary<string, object> { ["type"] = "ir.actions.act_window", ["res_id"] = 9001 };
        }
    }

    [Fact]
    public async Task EindeutigeZahlungWirdRegistriertUndGegengeprueft()
    {
        var odoo = OdooMitRechnung(1190.00m, ["Bank"]);

        var e = await ZahlungsBucher.BuchenAsync(odoo, "RE/2026/00123", 1190.00m, Zahltag, null, "Muster GmbH: RE/2026/00123");

        Assert.True(e.Gebucht);
        Assert.False(e.Unklar);
        Assert.Equal(9001, e.ZahlungId);
        var anlegen = odoo.Aufrufe.Single(a => a.Methode == "create");
        var werte = (Dictionary<string, object>)anlegen.Args[0];
        Assert.Equal(10, werte["journal_id"]);
        Assert.Equal("2026-11-24", werte["payment_date"]);
        Assert.Equal(1190.00m, werte["amount"]);
        // Der Assistent hängt über den Kontext an genau dieser Rechnung
        var kontext = (Dictionary<string, object>)anlegen.Kwargs!["context"];
        Assert.Equal("account.move", kontext["active_model"]);
        Assert.Equal(new[] { 77 }, (int[])kontext["active_ids"]);
    }

    [Theory]
    [InlineData(0, "bereits bezahlt")]
    [InlineData(500, "nur noch")]
    public async Task SchonBezahltOderZuWenigOffenWirdNichtGebucht(int offen, string erwartet)
    {
        var odoo = OdooMitRechnung(offen, ["Bank"]);

        var e = await ZahlungsBucher.BuchenAsync(odoo, "RE/2026/00123", 1190.00m, Zahltag, null, "x");

        Assert.False(e.Gebucht);
        Assert.False(e.Unklar);
        Assert.Contains(erwartet, e.Meldung);
        Assert.False(odoo.Gerufen("account.payment.register", "create"));
        Assert.False(odoo.Gerufen("account.payment.register", "action_create_payments"));
    }

    [Fact]
    public async Task BeiMehrerenBankjournalenWirdNichtGeraten()
    {
        var ohneName = await ZahlungsBucher.BuchenAsync(OdooMitRechnung(100m, ["Bank", "Sparkasse"]), "RE/2026/00123", 100m, Zahltag, "", "x");
        var falscherName = await ZahlungsBucher.BuchenAsync(OdooMitRechnung(100m, ["Bank", "Sparkasse"]), "RE/2026/00123", 100m, Zahltag, "Volksbank", "x");
        var odoo = OdooMitRechnung(100m, ["Bank", "Sparkasse"]);
        var mitName = await ZahlungsBucher.BuchenAsync(odoo, "RE/2026/00123", 100m, Zahltag, "sparkasse", "x");

        Assert.False(ohneName.Gebucht);
        Assert.Contains("mehrere Bankjournale", ohneName.Meldung);
        Assert.False(falscherName.Gebucht);
        Assert.True(mitName.Gebucht);
        Assert.Equal(11, ((Dictionary<string, object>)odoo.Aufrufe.Single(a => a.Methode == "create").Args[0])["journal_id"]);
    }

    [Fact]
    public async Task UnbekannteRechnungWirdAbgelehnt()
    {
        var odoo = new Attrappe((_, _, _, _) => Array.Empty<object>());

        var e = await ZahlungsBucher.BuchenAsync(odoo, "RE/2026/09999", 10m, Zahltag, null, "x");

        Assert.False(e.Gebucht);
        Assert.False(e.Unklar);
        Assert.Single(odoo.Aufrufe);
    }

    [Fact]
    public async Task AbbruchWaehrendDesBuchensIstUnklarUndNichtAbgelehnt()
    {
        var odoo = OdooMitRechnung(100m, ["Bank"], beimBuchen: () => throw new HttpRequestException("Verbindung getrennt"));

        var e = await ZahlungsBucher.BuchenAsync(odoo, "RE/2026/00123", 100m, Zahltag, null, "x");

        Assert.False(e.Gebucht);
        Assert.True(e.Unklar);
        Assert.Contains("prüfen", e.Meldung);
    }

    [Fact]
    public async Task OdooFehlerBeimBuchenVerraetNurDieArt()
    {
        var odoo = OdooMitRechnung(100m, ["Bank"],
            beimBuchen: () => throw new OdooFehlerException("odoo.exceptions.UserError", "Odoo-Fehler: {geheime Rohantwort}"));

        var e = await ZahlungsBucher.BuchenAsync(odoo, "RE/2026/00123", 100m, Zahltag, null, "x");

        Assert.True(e.Unklar);
        Assert.Contains("odoo.exceptions.UserError", e.Meldung);
        Assert.DoesNotContain("Rohantwort", e.Meldung);
    }

    [Fact]
    public async Task BleibtDieRechnungOffenGiltDieBuchungAlsUnklar()
    {
        var odoo = OdooMitRechnung(100m, ["Bank"], buchtWirklich: false);

        var e = await ZahlungsBucher.BuchenAsync(odoo, "RE/2026/00123", 100m, Zahltag, null, "x");

        Assert.False(e.Gebucht);
        Assert.True(e.Unklar);
    }

    [Fact]
    public async Task OffenePostenWerdenMitFirmaUndRestbetragGelesen()
    {
        var odoo = new Attrappe((_, _, _, _) => new object[]
        {
            new Dictionary<string, object>
            {
                ["id"] = 1, ["name"] = "RE/2026/00123", ["partner_id"] = new object[] { 8, "Muster GmbH, Erika Beispiel" },
                ["commercial_partner_id"] = new object[] { 5, "Muster GmbH" }, ["invoice_date"] = "2026-11-01",
                ["invoice_date_due"] = "2026-11-15", ["amount_total"] = 1190.0, ["amount_residual"] = 595.005,
                ["currency_id"] = new object[] { 1, "EUR" }, ["payment_reference"] = false,
            },
        });

        var r = Assert.Single(await OffenePosten.LadenAsync(odoo));

        Assert.Equal("Muster GmbH", r.Kunde);
        Assert.Equal(5, r.FirmaId);
        Assert.Equal(new DateOnly(2026, 11, 15), r.Faellig);
        Assert.Equal(595.01m, r.Offen);
        Assert.Null(r.Zahlungsreferenz);
        Assert.Equal(new object[] { "payment_state", "in", new[] { "not_paid", "partial" } }, (object[])OffenePosten.Filter()[2]);
    }

    [Fact]
    public async Task OffeneAngeboteUndAuftraegeMitFirmaVerkaeuferUndRechnung()
    {
        var odoo = new Attrappe((modell, methode, _, _) => (modell, methode) switch
        {
            ("sale.order", "search_read") => new object[]
            {
                new Dictionary<string, object>
                {
                    ["id"] = 1, ["name"] = "s01300", ["state"] = "sent", ["partner_id"] = new object[] { 8, "Muster GmbH, Erika Beispiel" },
                    ["date_order"] = "2026-11-10 09:00:00", ["validity_date"] = "2026-12-10", ["user_id"] = new object[] { 3, "Max Vertrieb" },
                    ["is_subscription"] = false, ["invoice_ids"] = Array.Empty<int>(), ["client_order_ref"] = false,
                },
                new Dictionary<string, object>
                {
                    ["id"] = 2, ["name"] = "S01301", ["state"] = "sale", ["partner_id"] = new object[] { 9, "Beispiel AG" },
                    ["date_order"] = "2026-11-12 09:00:00", ["validity_date"] = false, ["user_id"] = false,
                    ["is_subscription"] = true, ["invoice_ids"] = new[] { 40 }, ["client_order_ref"] = "Bestellung 4711",
                },
            },
            ("res.partner", "read") => new object[]
            {
                new Dictionary<string, object> { ["id"] = 8, ["commercial_partner_id"] = new object[] { 5, "Muster GmbH" } },
                new Dictionary<string, object> { ["id"] = 9, ["commercial_partner_id"] = new object[] { 9, "Beispiel AG" } },
            },
            ("res.users", "read") => new object[] { new Dictionary<string, object> { ["id"] = 3, ["login"] = "max@firma.example" } },
            ("sale.order.line", "search_read") => new object[]
            {
                new Dictionary<string, object> { ["order_id"] = new object[] { 1, "S01300" }, ["product_id"] = new object[] { 20, "Beispielprodukt" }, ["name"] = "Beispielprodukt" },
            },
            _ => throw new InvalidOperationException($"Unerwarteter Aufruf {modell}.{methode}"),
        });

        var liste = await OffeneVerkaeufe.LadenAsync(odoo, new DateOnly(2026, 6, 1));

        var angebot = liste[0];
        Assert.Equal("S01300", angebot.Nummer);
        Assert.False(angebot.Bestaetigt);
        Assert.Equal(5, angebot.FirmaId);                       // Firma, nicht der Ansprechpartner
        Assert.Equal("max@firma.example", angebot.VerkaeuferLogin);
        Assert.Equal(new DateOnly(2026, 12, 10), angebot.GueltigBis);
        Assert.Equal(["Beispielprodukt"], angebot.Produkte);
        var auftrag = liste[1];
        Assert.True(auftrag.Bestaetigt);
        Assert.True(auftrag.HatRechnung);
        Assert.True(auftrag.Abo);
        Assert.Null(auftrag.VerkaeuferLogin);
        Assert.Equal("2026-06-01 00:00:00", ((object[])OffeneVerkaeufe.Filter(new DateOnly(2026, 6, 1))[1])[2]);
    }
}
