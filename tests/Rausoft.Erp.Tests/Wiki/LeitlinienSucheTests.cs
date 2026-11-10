using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Tests.Wiki;

/// <summary>
/// Suche der Kommunikationsleitlinien: alle Begriffe müssen vorkommen — über
/// Aussage, Alternative, Anlass, Begründung, Beispiele, Varianten und den
/// Hinweis für Claude hinweg; LIKE-Platzhalter gelten wörtlich; Varianten
/// werden bereinigt gespeichert.
/// </summary>
public class LeitlinienSucheTests
{
    private static LeitlinieDto Regel(string aussage, string? stattdessen = null, string? anlass = null, string? begruendung = null,
        string? beispielGut = null, string? beispielSchlecht = null, string? varianten = null, string? kiHinweis = null) =>
        new(1, "Vermeiden", aussage, stattdessen, anlass, begruendung, false, beispielGut, beispielSchlecht,
            LeitlinienSuche.Varianten(varianten), ["vertrieb"], "produktleistung", "Produktleistung", null, null, [],
            kiHinweis, "Freigegeben", null, null, DateTime.UtcNow, null, null, null, null, null, null, null, "", false, false, null, null);

    [Fact]
    public void Trifft_AlleBegriffeUeberAlleFelder()
    {
        var r = Regel("garantiert", stattdessen: "beschreiben, was die Software tut",
            begruendung: "Klingt nach einer **bindenden Zusage**.", varianten: "garantieren, Garantie",
            kiHinweis: "An den Vertrieb verweisen");

        Assert.True(LeitlinienSuche.Trifft(r, "garantiert"));
        Assert.True(LeitlinienSuche.Trifft(r, "GARANTIE zusage"));          // Variante + Begründung
        Assert.True(LeitlinienSuche.Trifft(r, "software vertrieb"));        // Alternative + Hinweis für Claude
        Assert.False(LeitlinienSuche.Trifft(r, "garantiert rabatt"));       // ein Begriff fehlt
        Assert.True(LeitlinienSuche.Trifft(r, ""));
        Assert.True(LeitlinienSuche.Trifft(r, null));
    }

    [Fact]
    public void Trifft_AnlassUndBeispiele()
    {
        var r = Regel("Die Prüfung ersetzt keine Rechtsberatung.", anlass: "wenn ein Kunde nach der Bewertung eines Treffers fragt",
            beispielGut: "Bitte klären Sie das mit Ihrer Rechtsabteilung.", beispielSchlecht: "Das ist unbedenklich.");

        Assert.True(LeitlinienSuche.Trifft(r, "treffers"));
        Assert.True(LeitlinienSuche.Trifft(r, "rechtsabteilung"));
        Assert.True(LeitlinienSuche.Trifft(r, "unbedenklich rechtsberatung"));
        Assert.False(LeitlinienSuche.Trifft(r, "preisliste"));
    }

    [Fact]
    public void Suchfelder_SindDieAchtTextfelderDerDatenbankabfrage()
    {
        var felder = LeitlinienSuche.Suchfelder(Regel("a", "b", "c", "d", "e", "f", "g1, g2", "h"));
        Assert.Equal(new string?[] { "a", "b", "c", "d", "e", "f", "g1, g2", "h" }, felder);
    }

    [Fact]
    public void LikeMuster_PlatzhalterInRegelbegriffenGeltenWoertlich()
    {
        // „100 %“ in einer Aussage wie „100 % sicher“ darf nicht als Platzhalter wirken
        Assert.Equal(["100", "sicher"], FaqSuche.Begriffe("100 % sicher"));
        Assert.Equal("%100\\%%", FaqSuche.LikeMuster("100%"));
        Assert.Equal("%rundum\\_sorglos%", FaqSuche.LikeMuster("rundum_sorglos"));
        Assert.Equal("%\\[intern]%", FaqSuche.LikeMuster("[intern]"));
    }

    [Fact]
    public void Varianten_BereinigtUndEntdoppelt()
    {
        Assert.Equal(["garantieren", "Garantie", "garantierte"],
            LeitlinienSuche.Varianten(" garantieren, Garantie ;GARANTIE,, garantierte \n garantieren "));
        Assert.Equal("garantieren, Garantie", LeitlinienSuche.VariantenSpeichern("garantieren ; Garantie"));
        Assert.Null(LeitlinienSuche.VariantenSpeichern(" , ; "));
        Assert.Null(LeitlinienSuche.VariantenSpeichern(null));
        Assert.Empty(LeitlinienSuche.Varianten(null));
    }

    [Fact]
    public void Varianten_HoechstensDreissig()
    {
        var viele = string.Join(",", Enumerable.Range(1, 45).Select(i => $"form{i}"));
        Assert.Equal(30, LeitlinienSuche.Varianten(viele).Count);
    }
}
