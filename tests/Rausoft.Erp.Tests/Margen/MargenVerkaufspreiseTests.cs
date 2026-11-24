using Rausoft.Erp.Domain.Margen;

namespace Rausoft.Erp.Tests.Margen;

/// <summary>
/// Die gültigen Verkaufspreise stehen in der Wiki-Preisliste: Hat sie für Basket
/// und Bereich einen Preis, rechnet der Margenrechner damit — der von Hand
/// eingetragene Preis gilt nur ersatzweise. Erfundene Zahlen.
/// </summary>
public class MargenVerkaufspreiseTests
{
    private static MargenDaten Gepflegt() => new(
        Baskets: [new(1_000, 50, 100), new(10_000, 300, 600), new(100_000, 1000, 2000)],
        Staffel: [new(100_000, 2000, 1000), new(200_000, 3000, 1500), new(500_000, 5000, 5000)],
        LinearSchritt: 100_000,
        LinearAnker: [new(500_000, 5000), new(1_000_000, 8000)],
        Stand: "Test");

    [Fact]
    public void WikiPreisHatVorrang_GepflegterGiltNurWoDasWikiKeinenHat()
    {
        var wiki = new MargenWikiPreise(7, "2030-01",
        [
            new(1_000, 55, null),          // nur Sanctions Only im Wiki
            new(10_000, 320, 640),
            new(250_000, 1500, 3000),      // Basket, den der Rechner nicht führt
        ]);

        var (daten, herkunft) = MargenVerkaufspreise.Anwenden(Gepflegt(), wiki);

        Assert.Equal([new(1_000, 55, 100), new(10_000, 320, 640), new(100_000, 1000, 2000)], daten.Baskets);
        Assert.Equal(7, herkunft.WikiVersion);
        Assert.Equal("2030-01", herkunft.WikiStand);
        Assert.Equal(3, herkunft.AusWiki);
        Assert.Equal(
        [
            new(1_000, MargenBereich.FullFile),
            new(100_000, MargenBereich.SanctionsOnly),
            new(100_000, MargenBereich.FullFile),
        ], herkunft.VonHand);
    }

    [Fact]
    public void EinkaufsstaffelUndBasketsBleibenWieGepflegt()
    {
        var gepflegt = Gepflegt();
        var (daten, _) = MargenVerkaufspreise.Anwenden(gepflegt, new MargenWikiPreise(1, "2030-01", [new(10_000, 1, 2)]));

        Assert.Equal(gepflegt.Staffel, daten.Staffel);
        Assert.Equal(gepflegt.LinearAnker, daten.LinearAnker);
        Assert.Equal(gepflegt.LinearSchritt, daten.LinearSchritt);
        Assert.Equal(gepflegt.Baskets.Select(b => b.Cases), daten.Baskets.Select(b => b.Cases));
        Assert.Equal("Test", daten.Stand);
        Assert.Empty(daten.Pruefen());
    }

    [Fact]
    public void OhneWikiPreisliste_GeltenDieGepflegtenPreise()
    {
        var (daten, herkunft) = MargenVerkaufspreise.Anwenden(Gepflegt(), null);

        Assert.Equal(Gepflegt().Baskets, daten.Baskets);
        Assert.Null(herkunft.WikiVersion);
        Assert.Equal(0, herkunft.AusWiki);
        Assert.Equal(6, herkunft.VonHand.Count);
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void UnbrauchbarerWikiPreis_WirdNichtUebernommen(double preis)
    {
        var (daten, herkunft) = MargenVerkaufspreise.Anwenden(Gepflegt(), new MargenWikiPreise(1, "2030-01", [new(1_000, preis, null)]));

        Assert.Equal(50, daten.Baskets[0].VkSanctionsOnly);
        Assert.Contains(new MargenBasketBereich(1_000, MargenBereich.SanctionsOnly), herkunft.VonHand);
    }

    [Fact]
    public void RechnungNutztDenWikiPreis()
    {
        var (daten, _) = MargenVerkaufspreise.Anwenden(Gepflegt(), new MargenWikiPreise(1, "2030-01", [new(10_000, 320, null)]));
        var e = new Margenrechnung(daten).Berechnen(new MargenEingabe([0, 10, 0], [0, 0, 0]));

        Assert.Equal(320, e.Zeilen[1].SanctionsOnly.VkJeKunde);
        Assert.Equal(3200, e.Kennzahlen.Verkaufspreis);        // 10 Kunden × 320 statt × 300
        Assert.Equal(600, e.Zeilen[1].FullFile.VkJeKunde);     // Full File nicht im Wiki → gepflegter Preis
    }
}
