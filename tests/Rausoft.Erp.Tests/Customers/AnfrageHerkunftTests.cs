using Rausoft.Erp.Domain.Customers;

namespace Rausoft.Erp.Tests.Customers;

public class AnfrageHerkunftTests
{
    private static readonly DateTime Jetzt = new(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc);

    private static Anfrage Aus(string felder) =>
        AnfrageHerkunft.Uebernehmen(
            WebsiteAnfrageLeser.Lesen("Name: Erika Beispiel\nFirma: Muster AG\nFirmen E-Mail: e@muster.example\n" + felder).Anfrage!,
            Jetzt);

    [Fact]
    public void Uebernehmen_GoogleAnzeige_KanalKampagneUndKlickKennung()
    {
        var a = Aus("""
            gclid: Abc-123
            gbraid: 0AAA
            utm_source: google
            utm_medium: cpc
            utm_campaign: google_ads_24257565344
            gad_source: 1
            gad_campaignid: 24257565344
            doc_referrer: https://www.google.com/
            first_touch_at: 2026-11-12T14:42:03.937Z
            last_page_before_form: https://www.idprove.de/service/infomappe/?_gl=1*6b0azq&gclid=Abc-123&gbraid=0AAA
            Datum: 12. November 2026
            Zeit: 15:43
            """);

        Assert.True(a.UeberAnzeige);
        Assert.Equal("Abc-123", a.Gclid);
        Assert.Equal("0AAA", a.Gbraid);
        Assert.Null(a.Wbraid);
        Assert.Equal("google", a.Quelle);
        Assert.Equal("cpc", a.Medium);
        // „google_ads_<Nummer>" ist der Ersatzname der Website, kein Kampagnenname
        Assert.Null(a.Kampagne);
        Assert.Equal("24257565344", a.KampagnenNr);
        // Eingang 15:43 deutscher Zeit (Winterzeit) = 14:43 UTC; erster Besuch eine Minute davor
        Assert.Equal(new DateTime(2026, 11, 12, 14, 43, 0), a.EingegangenAm);
        Assert.Equal(new DateTime(2026, 11, 12, 14, 42, 3, 937), a.ErsterBesuch);
        Assert.Equal("Google Ads", AnfrageHerkunft.Kanal(a));
        Assert.Equal("Google Ads · Kampagne Nr. 24257565344", AnfrageHerkunft.Text(a, new Dictionary<string, string>()));
        Assert.Equal("Google Ads · Kampagne Sanktionslisten Suche",
            AnfrageHerkunft.Text(a, AnfrageHerkunft.KampagnenNamen("1 = Alt; 24257565344 = Sanktionslisten Suche")));
    }

    /// <summary>
    /// Die Adressen der Website tragen die Klick-Kennung im Abfrageteil. Bliebe er stehen,
    /// würde das Löschen nach 90 Tagen nichts löschen.
    /// </summary>
    [Fact]
    public void Uebernehmen_Adressen_VerlierenAbfrageteilMitKlickKennung()
    {
        var a = Aus("""
            gclid: GEHEIM-KLICK
            landing_page: https://www.idprove.de/produkt/?gclid=GEHEIM-KLICK#oben
            last_page_before_form: https://www.idprove.de/service/infomappe/?_gl=1*x&gclid=GEHEIM-KLICK&gbraid=AUCH
            doc_referrer: https://www.google.com/search?q=sanktionslisten
            """);

        Assert.Equal("https://www.idprove.de/produkt/", a.Einstiegsseite);
        Assert.Equal("https://www.idprove.de/service/infomappe/", a.LetzteSeite);
        Assert.Equal("https://www.google.com/search", a.Verweis);
        foreach (var wert in new[] { a.Einstiegsseite, a.LetzteSeite, a.Verweis, a.Quelle, a.Kampagne, a.Nachricht })
            Assert.DoesNotContain("GEHEIM-KLICK", wert ?? "");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("nur text")]
    [InlineData("ftp://server/datei")]
    public void UrlOhneAbfrage_NurHttpAdressen(string wert) =>
        Assert.Null(AnfrageHerkunft.UrlOhneAbfrage(wert));

    [Theory]
    [InlineData(null, null, true, null, "Google Ads")]
    [InlineData("google", "cpc", true, null, "Google Ads")]
    [InlineData("linkedin", "paid_social", true, null, "Linkedin (bezahlt)")]
    [InlineData("google", "organic", false, null, "Google organisch")]
    [InlineData("bing", null, false, null, "Bing organisch")]
    [InlineData("rapidmail", "email", false, null, "Newsletter")]
    [InlineData("partner", "referral", false, null, "Partner")]
    [InlineData(null, null, false, "https://www.google.de/", "Google organisch")]
    [InlineData(null, null, false, "https://www.fachportal.example/artikel", "Verweis von fachportal.example")]
    [InlineData(null, null, false, null, "Direkt")]
    public void Kanal_IstLesbar(string? quelle, string? medium, bool anzeige, string? verweis, string erwartet) =>
        Assert.Equal(erwartet, AnfrageHerkunft.Kanal(quelle, medium, anzeige, verweis));

    [Fact]
    public void Uebernehmen_OhneHerkunft_IstDirekt_OhneEingangGiltJetzt()
    {
        var a = Aus("");
        Assert.False(a.UeberAnzeige);
        Assert.Equal(Jetzt, a.EingegangenAm);
        Assert.Null(a.ErsterBesuch);
        Assert.Equal("Direkt", AnfrageHerkunft.Text(a, new Dictionary<string, string>()));
    }

    [Fact]
    public void Uebernehmen_EigenerKampagnenname_BleibtErhalten()
    {
        var a = Aus("utm_source: google\nutm_medium: cpc\nutm_campaign: Herbstaktion\ngad_campaignid: 77");
        Assert.Equal("Herbstaktion", a.Kampagne);
        Assert.Equal("77", a.KampagnenNr);
        Assert.Equal("Google Ads · Kampagne Herbstaktion",
            AnfrageHerkunft.Text(a, AnfrageHerkunft.KampagnenNamen("77 = Anderer Name")));
    }

    [Fact]
    public void KampagnenNamen_LiestListe_UndIgnoriertUnvollstaendiges()
    {
        var namen = AnfrageHerkunft.KampagnenNamen(" 1 = Suche DE ;2=Display\n3 = ; = ohne ; nur text; 1 = doppelt");
        Assert.Equal(2, namen.Count);
        Assert.Equal("Suche DE", namen["1"]);
        Assert.Equal("Display", namen["2"]);
        Assert.Empty(AnfrageHerkunft.KampagnenNamen(null));
    }

    [Fact]
    public void Ortszeit_SommerUndWinterzeit()
    {
        Assert.Equal(new DateTime(2026, 7, 1, 10, 0, 0), Ortszeit.NachUtc(new DateTime(2026, 7, 1, 12, 0, 0)));
        Assert.Equal(new DateTime(2026, 12, 1, 11, 0, 0), Ortszeit.NachUtc(new DateTime(2026, 12, 1, 12, 0, 0)));
        Assert.Equal(new DateTime(2026, 7, 1, 12, 0, 0), Ortszeit.VonUtc(new DateTime(2026, 7, 1, 10, 0, 0)));
    }
}
