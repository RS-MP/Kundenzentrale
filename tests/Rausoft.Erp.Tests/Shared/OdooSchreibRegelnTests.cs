using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Tests.Shared;

public class OdooSchreibRegelnTests
{
    private const string ProdUrl = "https://firma.odoo.example";
    private const string ProdDb = "firma";
    private static readonly DateOnly Heute = new(2026, 11, 17);

    [Theory]
    [InlineData("Test", OdooSchreibZiel.Test)]
    [InlineData(" produktiv ", OdooSchreibZiel.Produktiv)]
    [InlineData("Aus", OdooSchreibZiel.Aus)]
    [InlineData("", OdooSchreibZiel.Aus)]
    [InlineData(null, OdooSchreibZiel.Aus)]
    [InlineData("irgendwas", OdooSchreibZiel.Aus)]
    [InlineData("7", OdooSchreibZiel.Aus)] // Zahl außerhalb der Aufzählung zählt nicht
    public void Ziel_UnbekanntesIstAus(string? wert, OdooSchreibZiel erwartet) =>
        Assert.Equal(erwartet, OdooSchreibRegeln.Ziel(wert));

    [Fact]
    public void GueltigBis_LiestBeideSchreibweisen_UndRechnetResttage()
    {
        Assert.Equal(new DateOnly(2026, 12, 1), OdooSchreibRegeln.GueltigBis("2026-12-01"));
        Assert.Equal(new DateOnly(2026, 12, 1), OdooSchreibRegeln.GueltigBis("01.12.2026"));
        Assert.Null(OdooSchreibRegeln.GueltigBis("bald"));
        Assert.Equal("2026-12-01", OdooSchreibRegeln.GueltigBisText(new DateOnly(2026, 12, 1)));

        Assert.Equal(14, OdooSchreibRegeln.TageRest(new DateOnly(2026, 12, 1), Heute));
        Assert.Equal(-1, OdooSchreibRegeln.TageRest(Heute.AddDays(-1), Heute));
        Assert.Null(OdooSchreibRegeln.TageRest(null, Heute));
        // Am letzten Tag gilt sie noch, ab dem Folgetag nicht mehr; ohne Datum nie abgelaufen
        Assert.False(OdooSchreibRegeln.Abgelaufen(Heute, Heute));
        Assert.True(OdooSchreibRegeln.Abgelaufen(Heute.AddDays(-1), Heute));
        Assert.False(OdooSchreibRegeln.Abgelaufen(null, Heute));
    }

    [Theory]
    [InlineData("https://firma-test1.odoo.example", "firma-test1", "api@firma.example", null)]
    [InlineData("https://firma.odoo.example", "firma-kopie", "api@firma.example", null)] // gleicher Server, andere Datenbank
    [InlineData("", "db", "nutzer", "ausgefüllt")]
    [InlineData("https://firma-test1.odoo.example", " ", "nutzer", "ausgefüllt")]
    [InlineData("http://firma-test1.odoo.example", "db", "nutzer", "https")]
    [InlineData("firma-test1.odoo.example", "db", "nutzer", "https")]
    [InlineData("https://nutzer:geheim@firma-test1.odoo.example", "db", "nutzer", "https")]
    [InlineData("https://FIRMA.odoo.example/odoo", "Firma", "nutzer", "Produktiv-Datenbank")]
    public void TestZugangFehler(string url, string datenbank, string benutzer, string? fehlerEnthaelt)
    {
        var fehler = OdooSchreibRegeln.TestZugangFehler(url, datenbank, benutzer, ProdUrl, ProdDb);
        if (fehlerEnthaelt is null)
            Assert.Null(fehler);
        else
            Assert.Contains(fehlerEnthaelt, fehler);
    }

    [Fact]
    public void SchreibSperre_NurMitGueltigemZiel()
    {
        string? Sperre(OdooSchreibZiel ziel, bool testsystem = false, bool vollstaendig = true, DateOnly? bis = null) =>
            OdooSchreibRegeln.SchreibSperre(ziel, testsystem, vollstaendig, bis, Heute);

        Assert.Contains("ausgeschaltet", Sperre(OdooSchreibZiel.Aus));
        Assert.Null(Sperre(OdooSchreibZiel.Test, bis: Heute));
        Assert.Null(Sperre(OdooSchreibZiel.Test)); // ohne Ablaufdatum
        Assert.Contains("nicht vollständig", Sperre(OdooSchreibZiel.Test, vollstaendig: false));
        Assert.Contains("16.11.2026 abgelaufen", Sperre(OdooSchreibZiel.Test, bis: Heute.AddDays(-1)));
        // Die Produktiv-Datenbank hängt nicht an der Testdatenbank …
        Assert.Null(Sperre(OdooSchreibZiel.Produktiv, vollstaendig: false, bis: Heute.AddDays(-30)));
        // … aber ein Testsystem der Kundenzentrale schreibt nie dorthin
        Assert.Contains("Testsystem", Sperre(OdooSchreibZiel.Produktiv, testsystem: true));
        Assert.Null(Sperre(OdooSchreibZiel.Test, testsystem: true, bis: Heute));
    }
}
