using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Tests.Wiki;

/// <summary>
/// Suchlogik der FAQ: Begriffe zerlegen, LIKE-Muster entschärfen, Treffer über
/// mehrere Felder (UND), Schlagwörter bereinigen.
/// </summary>
public class FaqSucheTests
{
    [Fact]
    public void Begriffe_KleinOhneDublettenUndKurzwoerter()
    {
        var begriffe = FaqSuche.Begriffe("  2FA, Authenticator  2fa  a „Code ungültig“ ");
        Assert.Equal(["2fa", "authenticator", "code", "ungültig"], begriffe);
    }

    [Fact]
    public void Begriffe_LeerBeiNullOderLeer()
    {
        Assert.Empty(FaqSuche.Begriffe(null));
        Assert.Empty(FaqSuche.Begriffe("   "));
        Assert.Empty(FaqSuche.Begriffe("a , b"));
    }

    [Fact]
    public void LikeMuster_EntschaerftPlatzhalter()
    {
        Assert.Equal("%100\\%%", FaqSuche.LikeMuster("100%"));
        Assert.Equal("%a\\_b%", FaqSuche.LikeMuster("a_b"));
        Assert.Equal("%\\[x]%", FaqSuche.LikeMuster("[x]"));
        Assert.Equal("%c:\\\\pfad%", FaqSuche.LikeMuster("c:\\pfad"));
        Assert.Equal("%2fa%", FaqSuche.LikeMuster("2fa"));
    }

    [Fact]
    public void Trifft_AlleBegriffeUeberFelderHinweg()
    {
        string?[] felder = ["2FA lässt sich nicht einrichten", null, "alter Eintrag in der Authenticator-App", "Code wird abgelehnt"];
        Assert.True(FaqSuche.Trifft(felder, FaqSuche.Begriffe("2fa authenticator")));
        Assert.True(FaqSuche.Trifft(felder, FaqSuche.Begriffe("AUTHENTICATOR")));
        Assert.False(FaqSuche.Trifft(felder, FaqSuche.Begriffe("2fa lizenz")));
        Assert.True(FaqSuche.Trifft(felder, []));
    }

    [Fact]
    public void Schlagwoerter_BereinigtUndEntdoppelt()
    {
        var liste = FaqSuche.Schlagwoerter(" 2FA, Authenticator ;authenticator,, QR-Code \n TOTP ");
        Assert.Equal(["2FA", "Authenticator", "QR-Code", "TOTP"], liste);
        Assert.Equal("2FA, Authenticator, QR-Code, TOTP", FaqSuche.SchlagwoerterSpeichern(" 2FA, Authenticator ;authenticator,, QR-Code \n TOTP "));
        Assert.Null(FaqSuche.SchlagwoerterSpeichern("  , ;  "));
        Assert.Null(FaqSuche.SchlagwoerterSpeichern(null));
    }

    [Fact]
    public void Schlagwoerter_HoechstensZwanzig()
    {
        var viele = string.Join(",", Enumerable.Range(1, 30).Select(i => $"wort{i}"));
        Assert.Equal(20, FaqSuche.Schlagwoerter(viele).Count);
    }
}
