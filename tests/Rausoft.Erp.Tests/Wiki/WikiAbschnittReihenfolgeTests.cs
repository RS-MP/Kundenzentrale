using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Tests.Wiki;

/// <summary>
/// Die Redaktion ordnet die Abschnitte einer Wiki-Seite an; die Seite muss mit
/// alten (unbekannten) und neuen (noch nicht angeordneten) Abschnitten umgehen.
/// </summary>
public class WikiAbschnittReihenfolgeTests
{
    private static readonly List<string> Standard = ["stufen", "addons", "begriffe", "worldcheck"];

    [Fact]
    public void OhneGespeicherteReihenfolgeGiltDerStandard()
    {
        Assert.Equal(Standard, WikiAbschnittReihenfolge.Anwenden(null, Standard));
        Assert.Equal(Standard, WikiAbschnittReihenfolge.Anwenden([], Standard));
    }

    [Fact]
    public void GespeicherteReihenfolgeHatVorrangFehlendeKommenHinten()
    {
        var ergebnis = WikiAbschnittReihenfolge.Anwenden(["worldcheck", "stufen"], Standard);
        Assert.Equal(["worldcheck", "stufen", "addons", "begriffe"], ergebnis);
    }

    [Fact]
    public void UnbekannteUndDoppelteSchluesselWerdenIgnoriert()
    {
        var ergebnis = WikiAbschnittReihenfolge.Anwenden(["alt", "begriffe", "begriffe"], Standard);
        Assert.Equal(["begriffe", "stufen", "addons", "worldcheck"], ergebnis);
    }

    [Fact]
    public void LesenUndSchreibenSindUmkehrbar()
    {
        var wert = WikiAbschnittReihenfolge.Schreiben([" worldcheck ", "stufen"], out var fehler);
        Assert.Null(fehler);
        Assert.Equal("worldcheck,stufen", wert);
        Assert.Equal(["worldcheck", "stufen"], WikiAbschnittReihenfolge.Lesen(wert));
        Assert.Empty(WikiAbschnittReihenfolge.Lesen(null));
    }

    [Fact]
    public void LeereListeLoeschtDieReihenfolge()
    {
        Assert.Null(WikiAbschnittReihenfolge.Schreiben([], out var fehler));
        Assert.Null(fehler);
    }

    [Theory]
    [InlineData("Stufen")]
    [InlineData("stufen,addons;x")]
    [InlineData("a b")]
    public void UngueltigeSchluesselWerdenAbgelehnt(string eingabe)
    {
        Assert.Null(WikiAbschnittReihenfolge.Schreiben(eingabe.Split(','), out var fehler));
        Assert.NotNull(fehler);
    }

    [Fact]
    public void DoppelteSchluesselWerdenAbgelehnt()
    {
        Assert.Null(WikiAbschnittReihenfolge.Schreiben(["stufen", "stufen"], out var fehler));
        Assert.NotNull(fehler);
    }
}
