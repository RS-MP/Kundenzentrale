using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Domain.Wiki;

namespace Rausoft.Erp.Tests.Wiki;

/// <summary>
/// Berechtigungen der Kommunikationsleitlinien (Entscheidung MP 10.11.2026):
/// vorschlagen jeder, eigene Entwürfe der Ersteller; freigeben, zurückziehen,
/// fremde Entwürfe ändern und löschen ausschließlich Admin oder Compliance —
/// die Wiki-Redaktion zählt hier nicht. Freigegebenes wird nie überschrieben.
/// </summary>
public class SprachregelRegelnTests
{
    [Theory]
    [InlineData(MitarbeiterRolle.Admin)]
    [InlineData(MitarbeiterRolle.Compliance)]
    [InlineData(MitarbeiterRolle.Compliance | MitarbeiterRolle.Vertrieb)]
    public void Freigeben_NurAdminUndCompliance(MitarbeiterRolle rollen) =>
        Assert.True(SprachregelRegeln.DarfFreigeben(rollen));

    [Theory]
    [InlineData(MitarbeiterRolle.Keine)]
    [InlineData(MitarbeiterRolle.Vertrieb)]
    [InlineData(MitarbeiterRolle.Support)]
    [InlineData(MitarbeiterRolle.Buchhaltung)]
    [InlineData(MitarbeiterRolle.VertraulicheVertraege)]
    [InlineData(MitarbeiterRolle.WikiRedaktion)]
    [InlineData(MitarbeiterRolle.WikiRedaktion | MitarbeiterRolle.Vertrieb | MitarbeiterRolle.Support)]
    public void Freigeben_KeineAndereRolle_AuchNichtWikiRedaktion(MitarbeiterRolle rollen) =>
        Assert.False(SprachregelRegeln.DarfFreigeben(rollen));

    [Fact]
    public void Bearbeiten_NurEntwurf_ErstellerOderFreigeber()
    {
        // eigener Entwurf
        Assert.True(SprachregelRegeln.DarfBearbeiten(MitarbeiterRolle.Vertrieb, 7, 7, SprachregelStatus.Entwurf));
        // fremder Entwurf: nur Admin/Compliance
        Assert.False(SprachregelRegeln.DarfBearbeiten(MitarbeiterRolle.Vertrieb, 7, 8, SprachregelStatus.Entwurf));
        Assert.False(SprachregelRegeln.DarfBearbeiten(MitarbeiterRolle.WikiRedaktion, 7, 8, SprachregelStatus.Entwurf));
        Assert.False(SprachregelRegeln.DarfBearbeiten(MitarbeiterRolle.Vertrieb, 7, null, SprachregelStatus.Entwurf));
        Assert.True(SprachregelRegeln.DarfBearbeiten(MitarbeiterRolle.Compliance, 7, 8, SprachregelStatus.Entwurf));
        Assert.True(SprachregelRegeln.DarfBearbeiten(MitarbeiterRolle.Admin, 7, null, SprachregelStatus.Entwurf));
    }

    [Theory]
    [InlineData(SprachregelStatus.Freigegeben)]
    [InlineData(SprachregelStatus.Zurueckgezogen)]
    public void Bearbeiten_FreigegebenesWirdNieUeberschrieben(SprachregelStatus status)
    {
        Assert.False(SprachregelRegeln.DarfBearbeiten(MitarbeiterRolle.Admin, 7, 7, status));
        Assert.False(SprachregelRegeln.DarfBearbeiten(MitarbeiterRolle.Compliance, 7, 8, status));
        Assert.False(SprachregelRegeln.DarfBearbeiten(MitarbeiterRolle.Vertrieb, 7, 7, status));
    }

    [Fact]
    public void Loeschen_EntwurfErstellerOderFreigeber_GeltendeRegelNiemand()
    {
        Assert.True(SprachregelRegeln.DarfLoeschen(MitarbeiterRolle.Vertrieb, 7, 7, SprachregelStatus.Entwurf));
        Assert.False(SprachregelRegeln.DarfLoeschen(MitarbeiterRolle.Vertrieb, 7, 8, SprachregelStatus.Entwurf));
        Assert.True(SprachregelRegeln.DarfLoeschen(MitarbeiterRolle.Compliance, 7, 8, SprachregelStatus.Entwurf));

        // Eine geltende Regel wird zurückgezogen, nicht gelöscht — auch nicht von Admin
        Assert.False(SprachregelRegeln.DarfLoeschen(MitarbeiterRolle.Admin, 7, 7, SprachregelStatus.Freigegeben));
        Assert.False(SprachregelRegeln.DarfLoeschen(MitarbeiterRolle.Compliance, 7, 8, SprachregelStatus.Freigegeben));

        // Zurückgezogene: nur Admin/Compliance, nicht der Ersteller
        Assert.True(SprachregelRegeln.DarfLoeschen(MitarbeiterRolle.Admin, 7, 8, SprachregelStatus.Zurueckgezogen));
        Assert.False(SprachregelRegeln.DarfLoeschen(MitarbeiterRolle.Vertrieb, 7, 7, SprachregelStatus.Zurueckgezogen));
        Assert.False(SprachregelRegeln.DarfLoeschen(MitarbeiterRolle.WikiRedaktion, 7, 7, SprachregelStatus.Zurueckgezogen));
    }

    [Fact]
    public void Statuswechsel_NurVorwaerts()
    {
        Assert.True(SprachregelRegeln.IstErlaubterWechsel(SprachregelStatus.Entwurf, SprachregelStatus.Freigegeben));
        Assert.True(SprachregelRegeln.IstErlaubterWechsel(SprachregelStatus.Freigegeben, SprachregelStatus.Zurueckgezogen));

        Assert.False(SprachregelRegeln.IstErlaubterWechsel(SprachregelStatus.Freigegeben, SprachregelStatus.Entwurf));
        Assert.False(SprachregelRegeln.IstErlaubterWechsel(SprachregelStatus.Zurueckgezogen, SprachregelStatus.Freigegeben));
        Assert.False(SprachregelRegeln.IstErlaubterWechsel(SprachregelStatus.Zurueckgezogen, SprachregelStatus.Entwurf));
        Assert.False(SprachregelRegeln.IstErlaubterWechsel(SprachregelStatus.Entwurf, SprachregelStatus.Zurueckgezogen));
        Assert.False(SprachregelRegeln.IstErlaubterWechsel(SprachregelStatus.Entwurf, SprachregelStatus.Entwurf));
    }

    [Fact]
    public void Export_KiNurAdminUndCompliance_SonstDieEigeneRolle()
    {
        foreach (var zielgruppe in new[] { SprachregelZielgruppe.KI, SprachregelZielgruppe.Vertrieb, SprachregelZielgruppe.Support })
        {
            Assert.True(SprachregelRegeln.DarfExportieren(MitarbeiterRolle.Admin, zielgruppe));
            Assert.True(SprachregelRegeln.DarfExportieren(MitarbeiterRolle.Compliance, zielgruppe));
            Assert.False(SprachregelRegeln.DarfExportieren(MitarbeiterRolle.Buchhaltung, zielgruppe));
            Assert.False(SprachregelRegeln.DarfExportieren(MitarbeiterRolle.WikiRedaktion, zielgruppe));
        }

        Assert.True(SprachregelRegeln.DarfExportieren(MitarbeiterRolle.Vertrieb, SprachregelZielgruppe.Vertrieb));
        Assert.False(SprachregelRegeln.DarfExportieren(MitarbeiterRolle.Vertrieb, SprachregelZielgruppe.Support));
        Assert.False(SprachregelRegeln.DarfExportieren(MitarbeiterRolle.Vertrieb, SprachregelZielgruppe.KI));

        Assert.True(SprachregelRegeln.DarfExportieren(MitarbeiterRolle.Support, SprachregelZielgruppe.Support));
        Assert.False(SprachregelRegeln.DarfExportieren(MitarbeiterRolle.Support, SprachregelZielgruppe.Vertrieb));
        Assert.False(SprachregelRegeln.DarfExportieren(MitarbeiterRolle.Support | MitarbeiterRolle.Vertrieb, SprachregelZielgruppe.KI));
    }

    [Fact]
    public void Pflichtfelder_JeArt()
    {
        const SprachregelZielgruppe alle = SprachregelZielgruppe.Vertrieb | SprachregelZielgruppe.KI;

        Assert.Null(SprachregelRegeln.Pruefen(SprachregelArt.Verwenden, "Sanktionslistenprüfung", null, null, null, alle));
        Assert.NotNull(SprachregelRegeln.Pruefen(SprachregelArt.Verwenden, "  ", null, null, null, alle));
        Assert.NotNull(SprachregelRegeln.Pruefen(SprachregelArt.Verwenden, "Begriff", null, null, null, SprachregelZielgruppe.Keine));

        // Vermeiden braucht die Alternative
        Assert.NotNull(SprachregelRegeln.Pruefen(SprachregelArt.Vermeiden, "garantiert", null, null, "Grund", alle));
        Assert.Null(SprachregelRegeln.Pruefen(SprachregelArt.Vermeiden, "garantiert", "beschreiben, was die Software tut", null, null, alle));

        // Verboten braucht die Begründung
        Assert.NotNull(SprachregelRegeln.Pruefen(SprachregelArt.Verboten, "Zusage", null, null, " ", alle));
        Assert.Null(SprachregelRegeln.Pruefen(SprachregelArt.Verboten, "Zusage", null, null, "Können wir nicht einhalten.", alle));

        // Pflichthinweis braucht den Anlass
        Assert.NotNull(SprachregelRegeln.Pruefen(SprachregelArt.Pflichthinweis, "Hinweis", null, null, "Grund", alle));
        Assert.Null(SprachregelRegeln.Pruefen(SprachregelArt.Pflichthinweis, "Hinweis", null, "bei jedem Angebot", null, alle));

        Assert.Null(SprachregelRegeln.Pruefen(SprachregelArt.Grundsatz, "Wir siezen.", null, null, null, alle));
    }

    [Fact]
    public void Katalog_SchluesselUndReihenfolge()
    {
        Assert.True(SprachregelKatalog.IstThema("recht"));
        Assert.False(SprachregelKatalog.IstThema("freitext"));
        Assert.False(SprachregelKatalog.IstThema(null));
        Assert.Equal("Preise und Rabatte", SprachregelKatalog.ThemaName("preise"));
        Assert.Equal(8, SprachregelKatalog.Themen.Length);
        Assert.True(WikiThema.IstBekannt(WikiThema.Leitlinien));
        Assert.Equal("Vertrieb", WikiThema.Alle.Single(t => t.Schluessel == WikiThema.Leitlinien).Bereich);

        Assert.Equal(SprachregelZielgruppe.KI, SprachregelKatalog.Zielgruppe("KI"));
        Assert.Null(SprachregelKatalog.Zielgruppe("alle"));
        Assert.Equal(SprachregelZielgruppe.Vertrieb | SprachregelZielgruppe.KI, SprachregelKatalog.ZielgruppenAus(["vertrieb", "ki"]));
        Assert.Null(SprachregelKatalog.ZielgruppenAus(["vertrieb", "kunden"]));
        Assert.Equal(SprachregelZielgruppe.Keine, SprachregelKatalog.ZielgruppenAus(null));
        Assert.Equal(SprachregelKanal.EMail | SprachregelKanal.Chat, SprachregelKatalog.KanaeleAus(["email", "chat"]));
        Assert.Null(SprachregelKatalog.KanaeleAus(["fax"]));
        Assert.Equal(["email", "chat"], SprachregelKatalog.KanalSchluessel(SprachregelKanal.Chat | SprachregelKanal.EMail));

        // Jede Art hat einen Bereich — sonst fiele sie aus Seite und Export
        foreach (var art in Enum.GetValues<SprachregelArt>())
            Assert.NotEqual(int.MaxValue, SprachregelKatalog.ArtRang(art));
    }
}
