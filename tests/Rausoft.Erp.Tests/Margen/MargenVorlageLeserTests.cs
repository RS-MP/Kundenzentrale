using Rausoft.Erp.Domain.Margen;

namespace Rausoft.Erp.Tests.Margen;

/// <summary>
/// Einlesen der HTML-Kalkulationsvorlage. Die Testdatei ahmt nur den Aufbau
/// nach — alle Zahlen sind erfunden.
/// </summary>
public class MargenVorlageLeserTests
{
    private const string Vorlage = """
        <!DOCTYPE html><html><body>
        <ul><li><b>Stand der Regel:</b> Variante&nbsp;X laut Abstimmung vom 1.&nbsp;Januar 2030. <i>Maßgeblich</i> ist der Vertrag.</li></ul>
        <footer><span>Interne Kalkulation · VK-Preise und Staffel Stand 1. Januar 2030 · ohne Umsatzsteuer</span></footer>
        <script>
        (function(){
          "use strict";
          /* [bis Cases pro Jahr, Full File, Sanctions Only] */
          var TIERS=[[100000,2000,1000],[200000,3000,1500],[500000,5000,5000]];
          var NT=TIERS.length;
          var ANCH=[[500000,5000],[1000000,8000.5],[2000000,10000]]; /* Anker */
          var LIN_FROM=ANCH[0][0],LIN_STEP=100000,RATE_INF=1;
          var B=[1000,10000,100000];
          var PS=[50,300,1000];      /* VK Sanctions Only */
          var PF=[100,600,2000]; /* VK Full File */
          var EX_S=[1,2,3];
        })();
        </script></body></html>
        """;

    [Fact]
    public void Lesen_UebernimmtBasketsStaffelUndLineareRegel()
    {
        var d = MargenVorlageLeser.Lesen(Vorlage, out var fehler);

        Assert.Null(fehler);
        Assert.NotNull(d);
        Assert.Equal([new(1_000, 50, 100), new(10_000, 300, 600), new(100_000, 1000, 2000)], d.Baskets);
        Assert.Equal([new(100_000, 2000, 1000), new(200_000, 3000, 1500), new(500_000, 5000, 5000)], d.Staffel);
        Assert.Equal(100_000, d.LinearSchritt);
        Assert.Equal([new(500_000, 5000), new(1_000_000, 8000.5), new(2_000_000, 10_000)], d.LinearAnker);
        Assert.Equal("1. Januar 2030", d.Stand);
        Assert.Equal("Variante X laut Abstimmung vom 1. Januar 2030. Maßgeblich ist der Vertrag.", d.Hinweis);
        Assert.Empty(d.Pruefen());
    }

    [Fact]
    public void Lesen_OhneStandUndHinweisTrotzdemGueltig()
    {
        var ohne = Vorlage.Replace("Stand der Regel", "Regel").Replace("Staffel Stand 1.", "Staffel vom 1.");
        var d = MargenVorlageLeser.Lesen(ohne, out var fehler);
        Assert.Null(fehler);
        Assert.Null(d!.Stand);
        Assert.Null(d.Hinweis);
    }

    [Theory]
    [InlineData("var TIERS=", "var STUFEN=", "Staffeltabelle")]
    [InlineData("var ANCH=", "var ANKER=", "Anker")]
    [InlineData("LIN_STEP=", "SCHRITT=", "Schrittweite")]
    [InlineData("var B=", "var KOERBE=", "Baskets")]
    [InlineData("var PS=", "var VKS=", "Sanctions Only")]
    [InlineData("var PF=", "var VKF=", "Full File")]
    public void Lesen_FehlendeTeileWerdenBenannt(string alt, string neu, string erwartet)
    {
        var d = MargenVorlageLeser.Lesen(Vorlage.Replace(alt, neu), out var fehler);
        Assert.Null(d);
        Assert.Contains(erwartet, fehler);
    }

    [Fact]
    public void Lesen_UnpassendeZahlenWerdenAbgelehnt()
    {
        // Verkaufspreise mit anderer Anzahl als Baskets
        Assert.Null(MargenVorlageLeser.Lesen(Vorlage.Replace("var PS=[50,300,1000]", "var PS=[50,300]"), out var fehler));
        Assert.Contains("unterschiedlich", fehler);

        // Anker beginnt nicht am Ende der Tabelle
        Assert.Null(MargenVorlageLeser.Lesen(Vorlage.Replace("ANCH=[[500000,5000]", "ANCH=[[400000,5000]"), out fehler));
        Assert.Contains("Anker", fehler);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html><body>Keine Vorlage</body></html>")]
    [InlineData("var TIERS=[[alert(1)]];")]
    public void Lesen_FremdeDateienGebenEinenFehlerStattEinerAusnahme(string? html)
    {
        Assert.Null(MargenVorlageLeser.Lesen(html, out var fehler));
        Assert.False(string.IsNullOrWhiteSpace(fehler));
    }
}
