using System.Text.Json;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.OdooImport;

namespace Rausoft.Erp.Tests.Shared;

/// <summary>
/// Kundenmail aus der Akte: Platzhalter, Zusammensetzen, Empfänger. Entscheidend ist, was
/// NICHT passieren darf — ein offener Platzhalter geht nicht unbemerkt raus, fremder Text
/// landet nicht als HTML in der Mail, und das Testsystem schreibt nie an einen Kunden.
/// Erfundene Daten.
/// </summary>
public class MailRegelnTests
{
    private static Dictionary<string, string?> Werte(string? ansprechpartner = "Erika Beispiel", string? signatur = "<p>Max Vertrieb</p>") =>
        MailRegeln.Werte("Muster GmbH", "10001", ansprechpartner, "erika@muster.example", "Max Vertrieb", "max@firma.example", signatur);

    [Fact]
    public void BekanntePlatzhalterWerdenGefuelltUnbekannteBleibenOffen()
    {
        var html = MailRegeln.Fuellen("<p>Guten Tag {{Kunde.Name}} ({{ Kunde.Firma }}), Ihr Beleg {{Beleg.Nummer}} und {{Odoo: object.x}}.</p>", Werte(), html: true);

        Assert.Equal("<p>Guten Tag Erika Beispiel (Muster GmbH), Ihr Beleg {{Beleg.Nummer}} und {{Odoo: object.x}}.</p>", html);
        Assert.Equal(["Beleg.Nummer", "Odoo: object.x"], MailRegeln.Offene(html));
    }

    [Fact]
    public void WerteWerdenImHtmlKodiertImBetreffNicht()
    {
        var werte = MailRegeln.Werte("Müller & Söhne <b>GmbH</b>", null, null, null, "Max", null, null);

        Assert.Equal("<p>Müller &amp; Söhne &lt;b&gt;GmbH&lt;/b&gt;</p>", MailRegeln.Fuellen("<p>{{Kunde.Firma}}</p>", werte, html: true));
        Assert.Equal("Angebot für Müller & Söhne <b>GmbH</b>", MailRegeln.Fuellen("Angebot für {{Kunde.Firma}}", werte, html: false));
    }

    [Fact]
    public void OhneAnsprechpartnerStehtDieFirmaUndFehlendesBleibtOffen()
    {
        var werte = Werte(ansprechpartner: null);
        werte["Kunde.Email"] = null;

        Assert.Equal("Muster GmbH · {{Kunde.Email}}", MailRegeln.Fuellen("{{Kunde.Name}} · {{Kunde.Email}}", werte, html: false));
    }

    [Fact]
    public void SignaturIstHtmlUndIhrPlatzhalterVerschwindetOhneSignatur()
    {
        Assert.Equal("<div><p>Max Vertrieb</p></div>", MailRegeln.Fuellen("<div>{{Mitarbeiter.Signatur}}</div>", Werte(), html: true));
        Assert.Equal("<div></div>", MailRegeln.Fuellen("<div>{{Mitarbeiter.Signatur}}</div>", Werte(signatur: null), html: true));
        Assert.True(MailRegeln.EnthaeltSignatur("<p>{{ mitarbeiter.signatur }}</p>"));
        Assert.False(MailRegeln.EnthaeltSignatur("<p>{{Mitarbeiter.Name}}</p>"));
    }

    [Fact]
    public void EigenerTextStehtObenUndIstNieHtml()
    {
        var html = MailRegeln.Zusammensetzen("Guten Tag Frau Beispiel,\nanbei wie besprochen.\n\n<script>x</script>", "<p>Baustein</p>", "<p>Gruß</p>");

        Assert.Equal(
            "<p>Guten Tag Frau Beispiel,<br>anbei wie besprochen.</p>\n<p>&lt;script&gt;x&lt;/script&gt;</p>\n<p>Baustein</p>\n<div><p>Gruß</p></div>",
            html);
    }

    [Fact]
    public void LeereTeileFallenWeg()
    {
        Assert.Equal("<p>Baustein</p>", MailRegeln.Zusammensetzen("  ", "<p>Baustein</p>", null));
        Assert.Equal("", MailRegeln.Zusammensetzen(null, null, " "));
    }

    [Fact]
    public void AdressenWerdenGetrenntBereinigtUndGeprueft()
    {
        var (gueltig, ungueltig) = MailRegeln.Adressen("Erika@Muster.example; <max@firma.example>, erika@muster.example\nkeine-adresse @ohne.name a@b");

        Assert.Equal(["erika@muster.example", "max@firma.example"], gueltig);
        Assert.Equal(["keine-adresse", "@ohne.name", "a@b"], ungueltig);
        Assert.Empty(MailRegeln.Adressen(null).Gueltig);
    }

    [Fact]
    public void TestsystemSchreibtNieAnKunden()
    {
        string[] kunde = ["erika@muster.example", "chef@muster.example"];

        Assert.Equal(kunde, MailRegeln.Empfaenger(kunde, umleiten: false, umleitung: "test@firma.example"));
        Assert.Equal(["test@firma.example"], MailRegeln.Empfaenger(kunde, umleiten: true, umleitung: "test@firma.example"));
        Assert.Empty(MailRegeln.Empfaenger(kunde, umleiten: true, umleitung: ""));       // ohne Umleitung an niemanden
        Assert.Empty(MailRegeln.Empfaenger([], umleiten: true, umleitung: "test@firma.example"));
    }

    [Theory]
    [InlineData("Ja", true)]
    [InlineData(" ja ", true)]
    [InlineData("Nein", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("true", false)]      // nur das ausdrückliche Ja gibt frei
    public void KundenmailsNurMitAusdruecklichemJa(string? einstellung, bool erwartet) =>
        Assert.Equal(erwartet, MailRegeln.Freigegeben(einstellung));

    // --- Signatur aus Odoo --------------------------------------------------------------

    private sealed class Attrappe(object antwort) : IOdooAufruf
    {
        public object[]? Args { get; private set; }

        public Task<JsonElement> AusfuehrenAsync(string modell, string methode, object[] args, Dictionary<string, object>? kwargs = null)
        {
            Args = args;
            return Task.FromResult(JsonSerializer.SerializeToElement(antwort));
        }
    }

    [Fact]
    public async Task SignaturWirdUeberDieAnmeldungGesucht()
    {
        var odoo = new Attrappe(new object[] { new Dictionary<string, object> { ["id"] = 3, ["signature"] = "<p>Max Vertrieb</p>" } });

        Assert.Equal("<p>Max Vertrieb</p>", await BenutzerSignatur.LadenAsync(odoo, " max@firma.example "));
        Assert.Contains("\"login\",\"=ilike\",\"max@firma.example\"", JsonSerializer.Serialize(odoo.Args));
    }

    [Fact]
    public async Task OhneBenutzerOderOhneSignaturKommtNichts()
    {
        Assert.Null(await BenutzerSignatur.LadenAsync(new Attrappe(Array.Empty<object>()), "max@firma.example"));
        Assert.Null(await BenutzerSignatur.LadenAsync(new Attrappe(new object[] { new Dictionary<string, object> { ["id"] = 3, ["signature"] = false } }), "max@firma.example"));
        Assert.Null(await BenutzerSignatur.LadenAsync(new Attrappe(Array.Empty<object>()), "  "));
    }
}
