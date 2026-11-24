using Rausoft.Erp.Domain.Margen;

namespace Rausoft.Erp.Tests.Margen;

/// <summary>
/// Verkaufte World-Check-Lizenzen aus den Abos als Bestand des Margenrechners:
/// Bereich und Basket werden aus dem Produktnamen gelesen. Kunden und Mengen
/// hier sind erfunden.
/// </summary>
public class MargenLizenzenTests
{
    [Theory]
    [InlineData("ID.prove SaaS World-Check Sanctions Only (5.000)", MargenBereich.SanctionsOnly, 5_000)]
    [InlineData("ID.prove SaaS World-Check Full File (100.000)", MargenBereich.FullFile, 100_000)]
    [InlineData("[WC-SO] ID.prove SaaS WorldCheck Sanctions-Only (1.000)", MargenBereich.SanctionsOnly, 1_000)]
    [InlineData("ID.prove SaaS World Check full file (2.500 Stammdaten)", MargenBereich.FullFile, 2_500)]
    [InlineData("World-Check Sanctions Only (50k)", MargenBereich.SanctionsOnly, 50_000)]
    [InlineData("World-Check Full File (1 Mio.)", MargenBereich.FullFile, 1_000_000)]
    [InlineData("World-Check Sanctions Only (500 000)", MargenBereich.SanctionsOnly, 500_000)]
    [InlineData("World-Check Sanctions Only (10000)", MargenBereich.SanctionsOnly, 10_000)]
    [InlineData("World-Check Sanctions Only 25.000 Cases", MargenBereich.SanctionsOnly, 25_000)]
    [InlineData("ID.prove SaaS (PROFESSIONAL) World-Check Full File (15.000)", MargenBereich.FullFile, 15_000)]
    public void Erkennen_BereichUndBasketAusDemProduktnamen(string produkt, MargenBereich bereich, long cases)
    {
        Assert.True(MargenLizenzErkennung.IstWorldCheck(produkt));
        Assert.Equal(new MargenLizenzProdukt(bereich, cases), MargenLizenzErkennung.Erkennen(produkt));
    }

    [Theory]
    [InlineData("ID.prove SaaS World-Check Sanctions Only")]           // kein Basket
    [InlineData("ID.prove SaaS World-Check (5.000)")]                  // kein Bereich
    [InlineData("World-Check Sanctions Only und Full File (5.000)")]   // beide Bereiche
    [InlineData("World-Check Sanctions Only (auf Anfrage)")]
    public void Erkennen_UnklareWorldCheckProdukteBleibenUnerkannt_AberSichtbar(string produkt)
    {
        Assert.True(MargenLizenzErkennung.IstWorldCheck(produkt));
        Assert.Null(MargenLizenzErkennung.Erkennen(produkt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ID.prove SaaS PROFESSIONAL (25.000)")]
    [InlineData("Nationale Listen Europa")]
    [InlineData("Dow Jones Modul")]
    public void AndereProdukte_GehoerenNichtZumMargenrechner(string? produkt)
    {
        Assert.False(MargenLizenzErkennung.IstWorldCheck(produkt));
        Assert.Null(MargenLizenzErkennung.Erkennen(produkt));
    }

    private static readonly long[] Baskets = [1_000, 5_000, 100_000];

    private static MargenLizenzbestandErmittlung.Position Pos(int kunde, string produkt, decimal menge = 1) =>
        new(kunde, $"K{kunde:D5}", $"Kunde {kunde}", $"S{kunde:D5}", produkt, menge);

    [Fact]
    public void Bestand_ZaehltLizenzenJeBasketUndBereich()
    {
        var b = MargenLizenzbestandErmittlung.Ermitteln(Baskets,
        [
            Pos(1, "ID.prove SaaS World-Check Sanctions Only (5.000)"),
            Pos(2, "ID.prove SaaS World-Check Sanctions Only (5.000)"),
            Pos(3, "ID.prove SaaS World-Check Full File (100.000)"),
            Pos(4, "ID.prove SaaS World-Check Sanctions Only (1.000)", menge: 3),   // drei Lizenzen in einer Position
            Pos(5, "ID.prove SaaS PROFESSIONAL (25.000)"),                          // kein World-Check
        ], "24.11.2030 03:00");

        Assert.Equal([3, 2, 0], b.KundenSanctionsOnly);
        Assert.Equal([0, 0, 1], b.KundenFullFile);
        Assert.Equal(6, b.Zugeordnet);
        Assert.Equal(0, b.NichtZugeordnet);
        Assert.Equal(4, b.Lizenzen.Count);              // die Position ohne World-Check fehlt
        Assert.All(b.Lizenzen, l => Assert.Null(l.Hinweis));
        Assert.Equal("24.11.2030 03:00", b.StandImport);
    }

    [Fact]
    public void Bestand_NichtZuordenbaresStehtMitBegruendungInDerListe()
    {
        var b = MargenLizenzbestandErmittlung.Ermitteln(Baskets,
        [
            Pos(1, "ID.prove SaaS World-Check Sanctions Only (250.000)"),   // Basket fehlt im Rechner
            Pos(2, "ID.prove SaaS World-Check Sanctions Only"),             // Basket nicht erkennbar
            Pos(3, "ID.prove SaaS World-Check Full File (5.000)"),
        ], null);

        Assert.Equal([0, 0, 0], b.KundenSanctionsOnly);
        Assert.Equal([0, 1, 0], b.KundenFullFile);
        Assert.Equal(1, b.Zugeordnet);
        Assert.Equal(2, b.NichtZugeordnet);

        // Zugeordnete zuerst, dann die offenen
        Assert.Equal(1, b.Lizenzen[0].Anzahl);
        var fehlt = Assert.Single(b.Lizenzen, l => l.Basket == 250_000);
        Assert.Equal(0, fehlt.Anzahl);
        Assert.Contains("Basket", fehlt.Hinweis);
        var unklar = Assert.Single(b.Lizenzen, l => l.Basket is null);
        Assert.Null(unklar.Bereich);
        Assert.Contains("nicht aus dem Produktnamen", unklar.Hinweis);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(5000)]
    [InlineData(1.5)]
    public void Bestand_UngewoehnlicheMengeZaehltAlsEineLizenzUndWirdMarkiert(double menge)
    {
        var b = MargenLizenzbestandErmittlung.Ermitteln(Baskets,
            [Pos(1, "ID.prove SaaS World-Check Sanctions Only (5.000)", (decimal)menge)], null);

        Assert.Equal([0, 1, 0], b.KundenSanctionsOnly);
        Assert.Contains("Menge", Assert.Single(b.Lizenzen).Hinweis);
    }

    [Fact]
    public void Bestand_OhnePositionenIstLeer()
    {
        var b = MargenLizenzbestandErmittlung.Ermitteln(Baskets, [], null);
        Assert.Equal([0, 0, 0], b.KundenSanctionsOnly);
        Assert.Empty(b.Lizenzen);
        Assert.Equal(0, b.Zugeordnet);
    }
}
