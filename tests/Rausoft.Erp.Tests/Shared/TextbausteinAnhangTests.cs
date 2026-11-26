using System.Text.Json;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.OdooImport;

namespace Rausoft.Erp.Tests.Shared;

/// <summary>
/// Anhänge der Odoo-Mailvorlagen: was übernommen, was neu geladen und was ausgelassen wird —
/// und dass die Vorlagen auch dann ankommen, wenn Odoo die Anhänge nicht herausgibt.
/// Erfundene Daten.
/// </summary>
public class TextbausteinAnhangTests
{
    private static OdooAnhangInfo Datei(int id, string name, long groesse = 1000, string? pruefsumme = "abc", string? typ = "application/pdf", bool hatDatei = true) =>
        new(id, name, typ, groesse, pruefsumme, hatDatei);

    [Fact]
    public void NeueDateiWirdGeladenUnveraenderteBleibt()
    {
        var plan = AnhangAbgleich.Planen(
            [(Id: 1, OdooId: (int?)50, OdooStand: (string?)"abc")],
            [Datei(50, "Infomappe.pdf"), Datei(51, "Preisblatt.pdf", pruefsumme: "def")]);

        Assert.Equal(51, Assert.Single(plan.Laden).OdooId);
        Assert.Empty(plan.EntfernenIds);
        Assert.Empty(plan.Hinweise);
    }

    [Fact]
    public void GeaenderteDateiWirdErsetztUndNichtMehrAngehaengteEntfernt()
    {
        var plan = AnhangAbgleich.Planen(
            [(Id: 1, OdooId: (int?)50, OdooStand: (string?)"alt"), (Id: 2, OdooId: (int?)60, OdooStand: (string?)"x")],
            [Datei(50, "Infomappe.pdf", pruefsumme: "neu")]);

        Assert.Equal(50, Assert.Single(plan.Laden).OdooId);     // neue Fassung laden
        Assert.Equal([1, 2], plan.EntfernenIds);                // alte Fassung und die abgehängte Datei gehen
    }

    [Fact]
    public void DoppelteUebernahmeWirdBereinigt()
    {
        var plan = AnhangAbgleich.Planen(
            [(Id: 1, OdooId: (int?)50, OdooStand: (string?)"abc"), (Id: 2, OdooId: (int?)50, OdooStand: (string?)"abc")],
            [Datei(50, "Infomappe.pdf")]);

        Assert.Empty(plan.Laden);
        Assert.Equal([2], plan.EntfernenIds);
    }

    [Theory]
    [InlineData("Infomappe.pdf", 1000, true, null)]
    [InlineData("Rechner.xlsm", 1000, true, "Dateiart")]            // Makros
    [InlineData("setup.EXE", 1000, true, "Dateiart")]
    [InlineData("Seite.html", 1000, true, "Dateiart")]
    [InlineData("Film.mp4", 16 * 1024 * 1024, true, "zu groß")]
    [InlineData("Link zur Website", 0, false, "Verweis")]
    public void AusschlussMitGrund(string name, long groesse, bool hatDatei, string? erwartet)
    {
        var grund = AnhangAbgleich.Ausschluss(Datei(1, name, groesse, typ: null, hatDatei: hatDatei));

        if (erwartet is null)
            Assert.Null(grund);
        else
            Assert.Contains(erwartet, grund);
    }

    [Fact]
    public void AusgeschlosseneDateiWirdNichtGeladenAberGemeldet()
    {
        var plan = AnhangAbgleich.Planen([], [Datei(1, "makro.docm"), Datei(2, "Infomappe.pdf")]);

        Assert.Equal(2, Assert.Single(plan.Laden).OdooId);
        Assert.Contains("makro.docm", Assert.Single(plan.Hinweise));
    }

    [Theory]
    [InlineData("Infomappe 3.0 Erstkontakt", "application/pdf", "Infomappe 3.0 Erstkontakt.pdf")]  // „.0 Erstkontakt“ ist keine Endung
    [InlineData("Infomappe 3.0", "application/pdf", "Infomappe 3.0.pdf")]                          // „.0“ auch nicht
    [InlineData("Infomappe.pdf", "application/pdf", "Infomappe.pdf")]
    [InlineData("Bedingungen", null, "Bedingungen")]
    [InlineData("a/b:c.pdf", "application/pdf", "a_b_c.pdf")]
    public void DateinameBekommtDieEndungAusDemInhaltstyp(string name, string? typ, string erwartet) =>
        Assert.Equal(erwartet, AnhangAbgleich.Dateiname(Datei(1, name, typ: typ)));

    [Fact]
    public void HtmlOhneEndungWirdUeberDenInhaltstypErkanntUndAusgeschlossen() =>
        Assert.Contains("Dateiart", AnhangAbgleich.Ausschluss(Datei(1, "Seite", typ: "text/html")));

    [Fact]
    public void StandOhnePruefsummeAusGroesseUndName()
    {
        Assert.Equal("abc", AnhangAbgleich.Stand(Datei(1, "x.pdf")));
        Assert.Equal("1000:x.pdf", AnhangAbgleich.Stand(Datei(1, "x.pdf", pruefsumme: null)));
        Assert.Equal(300, AnhangAbgleich.Stand(Datei(1, new string('n', 400), pruefsumme: null)).Length);
    }

    // --- Odoo-Leser -------------------------------------------------------------------

    private sealed class Attrappe(Func<string, string, object?> antwort) : IOdooAufruf
    {
        public List<(string Modell, string Methode)> Aufrufe { get; } = [];

        public Task<JsonElement> AusfuehrenAsync(string modell, string methode, object[] args, Dictionary<string, object>? kwargs = null)
        {
            Aufrufe.Add((modell, methode));
            return Task.FromResult(JsonSerializer.SerializeToElement(antwort(modell, methode)));
        }
    }

    private static object[] Vorlagen(params int[] anhangIds) =>
    [
        new Dictionary<string, object>
        {
            ["id"] = 7, ["name"] = "Erstkontakt", ["subject"] = "Ihre Anfrage", ["body_html"] = "<p>Guten Tag</p>",
            ["model"] = "crm.lead", ["write_date"] = "2026-11-20 10:00:00", ["attachment_ids"] = anhangIds,
        },
    ];

    [Fact]
    public async Task VorlageMitAnhaengenUndLinkWirdGelesen()
    {
        var odoo = new Attrappe((modell, methode) => (modell, methode) switch
        {
            ("mail.template", "search_read") => Vorlagen(50, 51, 52),
            ("ir.attachment", "search_read") => new object[]
            {
                new Dictionary<string, object> { ["id"] = 50, ["name"] = "Infomappe 3.0 Erstkontakt", ["mimetype"] = "application/pdf", ["file_size"] = 2_500_000, ["checksum"] = "c0ffee", ["type"] = "binary" },
                new Dictionary<string, object> { ["id"] = 51, ["name"] = "Website", ["mimetype"] = false, ["file_size"] = 0, ["checksum"] = false, ["type"] = "url" },
                // 52 liefert Odoo nicht (kein Zugriff) — die Vorlage kommt trotzdem
            },
            _ => throw new InvalidOperationException($"Unerwarteter Aufruf {modell}.{methode}"),
        });

        var gelesen = await MailVorlagenLeser.LadenAsync(odoo);

        Assert.True(gelesen.AnhaengeLesbar);
        var v = Assert.Single(gelesen.Vorlagen);
        Assert.Equal(2, v.Anhaenge.Count);
        Assert.Equal(new OdooAnhangInfo(50, "Infomappe 3.0 Erstkontakt", "application/pdf", 2_500_000, "c0ffee", true), v.Anhaenge[0]);
        Assert.False(v.Anhaenge[1].HatDatei);
    }

    [Fact]
    public async Task OhneLeserechtAufAnhaengeKommenDieTexteTrotzdem()
    {
        var odoo = new Attrappe((modell, methode) => (modell, methode) switch
        {
            ("mail.template", "search_read") => Vorlagen(50),
            _ => throw new OdooFehlerException("odoo.exceptions.AccessError", "geheim"),
        });

        var gelesen = await MailVorlagenLeser.LadenAsync(odoo);

        Assert.False(gelesen.AnhaengeLesbar);
        Assert.Empty(Assert.Single(gelesen.Vorlagen).Anhaenge);
    }

    [Fact]
    public async Task VorlagenOhneAnhaengeFragenNichtNachDateien()
    {
        var odoo = new Attrappe((_, _) => Vorlagen());

        Assert.Empty(Assert.Single((await MailVorlagenLeser.LadenAsync(odoo)).Vorlagen).Anhaenge);
        Assert.Equal([("mail.template", "search_read")], odoo.Aufrufe);
    }

    [Fact]
    public async Task InhaltWirdAusBase64GelesenUndKaputtesIstLeer()
    {
        var gut = new Attrappe((_, _) => new object[] { new Dictionary<string, object> { ["id"] = 50, ["datas"] = Convert.ToBase64String([1, 2, 3]) } });
        var leer = new Attrappe((_, _) => new object[] { new Dictionary<string, object> { ["id"] = 50, ["datas"] = false } });
        var kaputt = new Attrappe((_, _) => new object[] { new Dictionary<string, object> { ["id"] = 50, ["datas"] = "kein base64 !" } });

        Assert.Equal([1, 2, 3], await MailVorlagenLeser.InhaltAsync(gut, 50));
        Assert.Empty(await MailVorlagenLeser.InhaltAsync(leer, 50));
        Assert.Empty(await MailVorlagenLeser.InhaltAsync(kaputt, 50));
    }
}
