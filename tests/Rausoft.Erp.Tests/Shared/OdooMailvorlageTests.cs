using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Domain.Vorgaenge;

namespace Rausoft.Erp.Tests.Shared;

/// <summary>
/// Odoo-Mailvorlagen als Textbausteine (Platzhalter, Bedingungen, Säubern), Nachfass-Regeln
/// für offene Angebote und die Regeln der Wochenübersichten.
/// </summary>
public class OdooMailvorlageTests
{
    [Fact]
    public void FormatierungBleibtUndBekanntePlatzhalterWerdenUebersetzt()
    {
        var html = """
            <div style="margin:0;padding:0">
              <p>Guten Tag <t t-out="object.partner_id.name or ''">Brandon Freeman</t>,</p>
              <p>anbei Ihr Angebot <strong t-out="object.name">S00052</strong> über
                 <t t-out="format_amount(object.amount_total, object.currency_id) or ''">$ 10.00</t>.</p>
              <table border="1"><tr><td>Gültig bis</td><td><t t-out="format_date(object.validity_date)"/></td></tr></table>
            </div>
            """;

        var v = OdooMailvorlage.Umsetzen("Angebot {{ object.name }} von {{ object.company_id.name }}", html);

        Assert.Equal("Angebot {{Beleg.Nummer}} von {{Firma.Name}}", v.Betreff);
        Assert.Contains("Guten Tag {{Kunde.Name}},", v.Html);
        Assert.Contains("<strong>{{Beleg.Nummer}}</strong>", v.Html);
        Assert.Contains("{{Beleg.Betrag}}", v.Html);
        Assert.Contains("<td>{{Angebot.GueltigBis}}</td>", v.Html);
        Assert.Contains("<div style=\"margin:0;padding:0\">", v.Html);
        Assert.Contains("<table border=\"1\">", v.Html);
        // Odoos Beispielwerte verschwinden, es bleibt nichts Unübersetztes
        Assert.DoesNotContain("Brandon", v.Html);
        Assert.DoesNotContain("t-out", v.Html);
        Assert.Empty(v.Hinweise);
    }

    [Fact]
    public void UnbekanntePlatzhalterBleibenSichtbarUndWerdenGemeldet()
    {
        var v = OdooMailvorlage.Umsetzen("${object.x_studio_sonderfeld}",
            "<p><t t-out=\"object.get_portal_url()\"/> und ${object.name}</p>");

        Assert.Equal("{{Odoo: object.x_studio_sonderfeld}}", v.Betreff);
        Assert.Contains("{{Odoo: object.get_portal_url()}}", v.Html);
        Assert.Contains("{{Beleg.Nummer}}", v.Html);
        Assert.Equal(2, v.Hinweise.Count);
        Assert.All(v.Hinweise, h => Assert.StartsWith("Unbekannter Platzhalter", h));
    }

    [Fact]
    public void BedingungenUndSchleifenWerdenAufgeloestUndAlsHinweisGenannt()
    {
        var html = """
            <t t-set="doc_name" t-value="'Angebot'"/>
            <t t-if="object.state == 'sale'"><p>Ihr Auftrag ist bestätigt.</p></t>
            <t t-else=""><p>Ihr Angebot liegt bei.</p></t>
            <ul><li t-foreach="object.order_line" t-as="line">Position</li></ul>
            <a t-att-href="object.get_portal_url()" style="color:red">Online ansehen</a>
            """;

        var v = OdooMailvorlage.Umsetzen("Betreff", html);

        Assert.Contains("<p>Ihr Auftrag ist bestätigt.</p>", v.Html);
        Assert.Contains("<p>Ihr Angebot liegt bei.</p>", v.Html);
        Assert.Contains("<li>Position</li>", v.Html);
        Assert.Contains("<a style=\"color:red\">Online ansehen</a>", v.Html);
        Assert.DoesNotContain("<t", v.Html);
        Assert.DoesNotContain("</t>", v.Html);
        Assert.DoesNotContain("t-", v.Html);
        Assert.Contains(v.Hinweise, h => h.Contains("object.state == 'sale'"));
        Assert.Contains(v.Hinweise, h => h.StartsWith("Wiederholung entfernt"));
        Assert.Contains(v.Hinweise, h => h.Contains("href"));
    }

    [Theory]
    [InlineData("<p>Hallo</p><script>alert(1)</script>", "<p>Hallo</p>")]
    [InlineData("<p onclick=\"x()\" style=\"color:red\">Hallo</p>", "<p style=\"color:red\">Hallo</p>")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>", "<a href=\"#\">x</a>")]
    [InlineData("<scr<script>ipt>alert(1)</scr</script>ipt><b>x</b>", "<b>x</b>")]
    [InlineData("<iframe src=\"https://example.org\"></iframe><i>x</i>", "<i>x</i>")]
    [InlineData("<form action=\"https://example.org\"><input name=\"a\"><b>x</b></form>", "<b>x</b>")]
    [InlineData("<img src=\"logo.png\" onerror=\"x()\">", "<img src=\"logo.png\">")]
    public void AusfuehrbaresWirdEntfernt(string html, string erwartet) =>
        Assert.Equal(erwartet, OdooMailvorlage.Saeubern(html));

    [Theory]
    [InlineData("sale.order", "Angebot / Auftrag")]
    [InlineData("account.move", "Rechnung")]
    [InlineData("x_eigenes.modell", "x_eigenes.modell")]
    [InlineData("", null)]
    public void VerwendungAusDemOdooModell(string modell, string? erwartet) =>
        Assert.Equal(erwartet, OdooMailvorlage.Verwendung(modell));

    // --- Nachfassen ----------------------------------------------------------------------

    private static readonly DateOnly Heute = new(2026, 11, 26);

    [Theory]
    [InlineData(6, false)]
    [InlineData(7, true)]
    [InlineData(30, true)]
    public void ErsteAufgabeNachDerFrist(int tageAlt, bool erwartet) =>
        Assert.Equal(erwartet, NachfassRegeln.AufgabeFaellig(Heute.AddDays(-tageAlt), Heute, 7, aufgabeOffen: false, null, 14));

    [Fact]
    public void SolangeEineAufgabeOffenIstEntstehtKeineZweite() =>
        Assert.False(NachfassRegeln.AufgabeFaellig(Heute.AddDays(-60), Heute, 7, aufgabeOffen: true, Heute.AddDays(-50), 14));

    [Theory]
    [InlineData(13, 14, false)]
    [InlineData(14, 14, true)]
    [InlineData(100, 0, false)]     // Wiederholung abgeschaltet: nur einmal nachfassen
    public void NaechsteAufgabeErstNachDerWiederholungsfrist(int tageSeitErledigt, int wiederholung, bool erwartet) =>
        Assert.Equal(erwartet, NachfassRegeln.AufgabeFaellig(Heute.AddDays(-90), Heute, 7, false, Heute.AddDays(-tageSeitErledigt), wiederholung));

    [Fact]
    public void OhneFristOderOhneDatumEntstehtNichts()
    {
        Assert.False(NachfassRegeln.AufgabeFaellig(Heute.AddDays(-90), Heute, 0, false, null, 14));
        Assert.False(NachfassRegeln.AufgabeFaellig(null, Heute, 7, false, null, 14));
    }

    [Theory]
    [InlineData("12", 7, 12)]
    [InlineData("", 7, 7)]
    [InlineData("abc", 7, 7)]
    [InlineData("-3", 7, 0)]
    [InlineData("99999", 7, 3650)]
    public void ZahlAusEinstellung(string wert, int standard, int erwartet) =>
        Assert.Equal(erwartet, NachfassRegeln.Zahl(wert, standard));

    // --- Wochenübersicht -------------------------------------------------------------------

    [Fact]
    public void WochenuebersichtNurAmEingestelltenTagUndNurEinmal()
    {
        var donnerstag = new DateOnly(2026, 11, 26);

        Assert.True(BerichtsRegeln.Faellig("Donnerstag", null, donnerstag));
        Assert.True(BerichtsRegeln.Faellig("donnerstag", "2026-11-19", donnerstag));
        Assert.False(BerichtsRegeln.Faellig("Donnerstag", "2026-11-26", donnerstag));
        Assert.False(BerichtsRegeln.Faellig("Montag", null, donnerstag));
        Assert.False(BerichtsRegeln.Faellig("aus", null, donnerstag));
        Assert.True(BerichtsRegeln.Faellig("täglich", "2026-11-25", donnerstag));
        Assert.False(BerichtsRegeln.Faellig("täglich", "2026-11-26", donnerstag));
    }

    [Fact]
    public void BerichteGehenNurAnErlaubteDomaenen()
    {
        var empfaenger = BerichtsRegeln.Empfaenger("a@firma.example; b@fremd.example, kein-at", "firma.example");

        Assert.Equal(["a@firma.example"], empfaenger);
    }
}
