using Rausoft.Erp.Domain.Customers;

namespace Rausoft.Erp.Tests.Customers;

public class WebsiteAnfrageTests
{
    /// <summary>Aufbau der Infomappen-Mail von idprove.de (Elementor-Formular), mit erfundenen Angaben.</summary>
    private const string InfomappenMail = """
        Von: ID.prove Website DE <vertrieb@idprove.example>
        Gesendet: Donnerstag, 12. November 2026 15:44
        An: Zentrale <info@rausoft.example>
        Betreff: Bitte INFOMAPPE versenden!

        Es wurde eine INFOMAPPE auf idprove.de angefordert. Bitte nach Prüfung (Freemailer/Wettbewerb) versenden!
        Name: Erika Maria Beispiel
        Firmen E-Mail: erika.beispiel@beispiel-consult.example
        Firma: BEISPIEL Consult GmbH
        Telefon: 0151 0000000
        Datenschutzerklärung: on
        gclid: Abc-123_xyz
        utm_source: google
        utm_medium: cpc
        utm_campaign: google_ads_1
        doc_referrer: https://www.google.com/
        gad_source: 1
        userId:
        wbraid:
        first_touch_at: 2026-11-12T14:42:03.937Z
        last_page_before_form: https://www.idprove.de/service/infomappe/?_gl=1*6b0azq&gclid=Abc-123_xyz

        ---

        Datum: 12. November 2026
        Zeit: 15:43
        Seiten URL: https://www.idprove.de/service/infomappe/
        Benutzer Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64)
        Remote IP: 192.0.2.1
        Unterstützt von: Elementor
        """;

    [Fact]
    public void Lesen_Infomappe_ErkenntKontaktFormularUndHerkunft()
    {
        var (a, fehler) = WebsiteAnfrageLeser.Lesen(InfomappenMail);

        Assert.Null(fehler);
        Assert.NotNull(a);
        Assert.Equal("Infomappe", a.Formular);
        Assert.Equal("idprove.de", a.Website);
        Assert.Equal("BEISPIEL Consult GmbH", a.Firma);
        Assert.Equal("Erika Maria Beispiel", a.Name);
        Assert.Equal("Erika Maria", a.Vorname);
        Assert.Equal("Beispiel", a.Nachname);
        Assert.Equal("erika.beispiel@beispiel-consult.example", a.EMail);
        Assert.Equal("beispiel-consult.example", a.EMailDomaene);
        Assert.Equal("0151 0000000", a.Telefon);
        Assert.True(a.DatenschutzBestaetigt);
        Assert.Equal(new DateTime(2026, 11, 12, 15, 43, 0), a.Eingang);
        Assert.Empty(a.Hinweise);
        // Nur Herkunftsfelder mit Inhalt, in der Reihenfolge der Mail
        Assert.Equal(
            ["gclid", "utm_source", "utm_medium", "utm_campaign", "doc_referrer", "gad_source",
                "first_touch_at", "last_page_before_form"],
            a.Herkunft.Select(h => h.Name));
        Assert.Equal("https://www.idprove.de/service/infomappe/?_gl=1*6b0azq&gclid=Abc-123_xyz",
            a.Herkunft.Single(h => h.Name == "last_page_before_form").Wert);
    }

    [Fact]
    public void Lesen_UebernimmtKeineTechnischenAbsenderdaten()
    {
        var (a, _) = WebsiteAnfrageLeser.Lesen(InfomappenMail);
        Assert.NotNull(a);
        Assert.DoesNotContain(a.Herkunft, h => h.Wert.Contains("192.0.2.1") || h.Wert.Contains("Mozilla"));
        Assert.DoesNotContain(a.Herkunft, h => h.Name is "Remote IP" or "Benutzer Agent" or "Seiten URL");
    }

    [Fact]
    public void Lesen_OhneMailkopf_NurFormularzeilen_Reicht()
    {
        var (a, fehler) = WebsiteAnfrageLeser.Lesen("Name: Beispiel\r\nFirma: Muster AG\r\nE-Mail: b@muster.example\r\nDatenschutzerklärung: on");
        Assert.Null(fehler);
        Assert.NotNull(a);
        Assert.Equal(WebsiteAnfrageLeser.FormularUnbekannt, a.Formular);
        Assert.Null(a.Vorname);
        Assert.Equal("Beispiel", a.Nachname);
        Assert.Null(a.Eingang);
    }

    [Theory]
    [InlineData("anna@gmail.com", true)]
    [InlineData("anna@GMX.de", true)]
    [InlineData("anna@muster.example", false)]
    public void Lesen_Freemailer_WirdAlsHinweisGemeldet(string email, bool freemailer)
    {
        var (a, _) = WebsiteAnfrageLeser.Lesen($"Name: Anna Test\nFirma: Muster AG\nFirmen E-Mail: {email}\nDatenschutzerklärung: on");
        Assert.NotNull(a);
        Assert.Equal(freemailer, a.Hinweise.Any(h => h.StartsWith("Freemailer")));
    }

    [Fact]
    public void Lesen_FehlendeUndUngueltigeAngaben_WerdenGemeldet()
    {
        var (a, fehler) = WebsiteAnfrageLeser.Lesen("Name: Anna Test\nFirmen E-Mail: keine-adresse");
        Assert.Null(fehler);
        Assert.NotNull(a);
        Assert.Null(a.EMailDomaene);
        Assert.Contains(a.Hinweise, h => h.Contains("sieht nicht gültig aus"));
        Assert.Contains(a.Hinweise, h => h.Contains("keine Firma"));
        Assert.Contains(a.Hinweise, h => h.Contains("Datenschutzerklärung"));
    }

    [Fact]
    public void Lesen_KeineFormularMail_OderZuGross_LiefertFehler()
    {
        Assert.NotNull(WebsiteAnfrageLeser.Lesen("").Fehler);
        Assert.NotNull(WebsiteAnfrageLeser.Lesen("Hallo, bitte rufen Sie mich zurück.\nViele Grüße").Fehler);
        Assert.NotNull(WebsiteAnfrageLeser.Lesen("Name: A\n" + new string('x', WebsiteAnfrageLeser.MaxTextLaenge)).Fehler);
        Assert.NotNull(WebsiteAnfrageLeser.Lesen("Name: A" + string.Concat(Enumerable.Repeat("\nx", WebsiteAnfrageLeser.MaxZeilen + 1))).Fehler);
    }

    /// <summary>Das Formular ist öffentlich: überlange Werte werden gekürzt, Steuerzeichen entfernt.</summary>
    [Fact]
    public void Lesen_UnvertrauterText_WirdBegrenztUndBereinigt()
    {
        var lang = new string('a', WebsiteAnfrageLeser.MaxFeldLaenge + 50);
        var (a, _) = WebsiteAnfrageLeser.Lesen($"Name: Anna\u0000 Te​st\u0007\nFirma: {lang}\nutm_source: {lang}\nE-Mail: a@muster.example");
        Assert.NotNull(a);
        Assert.Equal("Anna Test", a.Name);
        Assert.Equal(WebsiteAnfrageLeser.MaxFeldLaenge, a.Firma!.Length);
        Assert.Equal(WebsiteAnfrageLeser.MaxFeldLaenge, a.Herkunft.Single().Wert.Length);
        Assert.Contains(a.Hinweise, h => h.Contains("gekürzt"));
    }

    [Theory]
    [InlineData("Bitte TESTVERSION freischalten!", "Testversion")]
    [InlineData("Bitte CHECKLISTE versenden!", "Checkliste")]
    [InlineData("Neue Kontaktanfrage", "Kontakt")]
    public void Lesen_ErkenntFormularAmBetreff(string betreff, string erwartet)
    {
        var (a, _) = WebsiteAnfrageLeser.Lesen($"Betreff: {betreff}\nName: Anna Test\nFirma: Muster AG\nFirmen E-Mail: anna@muster.example");
        Assert.NotNull(a);
        Assert.Equal(erwartet, a.Formular);
    }

    [Theory]
    [InlineData("PROMOS Consult GmbH", "Promos-Consult", 100)]
    [InlineData("Müller & Söhne GmbH & Co. KG", "Mueller Soehne", 100)]
    [InlineData("Beispiel Consult GmbH", "Beispiel Consult Deutschland AG", 80)]
    [InlineData("Beispiel Consult GmbH", "Beispiel Logistik GmbH", 60)]
    [InlineData("Beispiel GmbH", "Muster GmbH", 0)]
    [InlineData("GmbH", "AG", 0)]
    [InlineData("ABC GmbH", "ABC Logistik", 0)] // zu kurz für eine belastbare Ähnlichkeit
    public void Firmenname_Aehnlichkeit(string a, string b, int erwartet)
    {
        Assert.Equal(erwartet, Firmenname.Aehnlichkeit(a, b));
        Assert.Equal(erwartet, Firmenname.Aehnlichkeit(b, a));
    }
}
