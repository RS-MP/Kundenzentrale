using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Domain.Wiki;

namespace Rausoft.Erp.Tests.Wiki;

/// <summary>
/// Berechtigungen der FAQ: anlegen jeder, eigenes bearbeiten der Ersteller;
/// freigeben/veraltet/löschen/fremdes bearbeiten nur Support, Admin,
/// Compliance oder Wiki-Redaktion mit Freigabe für das Thema „faq".
/// </summary>
public class FaqRegelnTests
{
    private static readonly string[] Keine = [];
    private static readonly string[] NurFaq = [WikiThema.Faq];
    private static readonly string[] NurSanktionslisten = [WikiThema.Sanktionslisten];

    [Theory]
    [InlineData(MitarbeiterRolle.Support)]
    [InlineData(MitarbeiterRolle.Admin)]
    [InlineData(MitarbeiterRolle.Compliance)]
    [InlineData(MitarbeiterRolle.Support | MitarbeiterRolle.Vertrieb)]
    public void Freigeben_SupportAdminCompliance(MitarbeiterRolle rollen) =>
        Assert.True(FaqRegeln.DarfFreigeben(rollen, Keine));

    [Theory]
    [InlineData(MitarbeiterRolle.Keine)]
    [InlineData(MitarbeiterRolle.Vertrieb)]
    [InlineData(MitarbeiterRolle.Buchhaltung)]
    [InlineData(MitarbeiterRolle.VertraulicheVertraege)]
    public void Freigeben_NichtOhneRolle(MitarbeiterRolle rollen) =>
        Assert.False(FaqRegeln.DarfFreigeben(rollen, NurFaq));   // Thema allein reicht nicht ohne Redaktionsrolle

    [Fact]
    public void Freigeben_RedaktionNurMitThemaFaq()
    {
        Assert.True(FaqRegeln.DarfFreigeben(MitarbeiterRolle.WikiRedaktion, NurFaq));
        Assert.False(FaqRegeln.DarfFreigeben(MitarbeiterRolle.WikiRedaktion, NurSanktionslisten));
        Assert.False(FaqRegeln.DarfFreigeben(MitarbeiterRolle.WikiRedaktion, Keine));
    }

    [Fact]
    public void Bearbeiten_ErstellerOderFreigeber()
    {
        Assert.True(FaqRegeln.DarfBearbeiten(MitarbeiterRolle.Vertrieb, Keine, eigeneId: 7, erstelltVonId: 7));
        Assert.False(FaqRegeln.DarfBearbeiten(MitarbeiterRolle.Vertrieb, Keine, eigeneId: 7, erstelltVonId: 8));
        Assert.False(FaqRegeln.DarfBearbeiten(MitarbeiterRolle.Vertrieb, Keine, eigeneId: 7, erstelltVonId: null));
        Assert.True(FaqRegeln.DarfBearbeiten(MitarbeiterRolle.Support, Keine, eigeneId: 7, erstelltVonId: 8));
    }

    [Fact]
    public void Loeschen_ErstellerNurBeiEigenemEntwurf()
    {
        Assert.True(FaqRegeln.DarfLoeschen(MitarbeiterRolle.Vertrieb, Keine, 7, 7, FaqStatus.Entwurf));
        Assert.False(FaqRegeln.DarfLoeschen(MitarbeiterRolle.Vertrieb, Keine, 7, 7, FaqStatus.Freigegeben));
        Assert.False(FaqRegeln.DarfLoeschen(MitarbeiterRolle.Vertrieb, Keine, 7, 8, FaqStatus.Entwurf));
        Assert.True(FaqRegeln.DarfLoeschen(MitarbeiterRolle.Admin, Keine, 7, 8, FaqStatus.Freigegeben));
    }

    [Fact]
    public void Katalog_SchluesselSindBekannt()
    {
        Assert.True(FaqKatalog.IstProdukt("idprove-saas"));
        Assert.True(FaqKatalog.IstKategorie("anmeldung-2fa"));
        Assert.False(FaqKatalog.IstProdukt("freitext"));
        Assert.False(FaqKatalog.IstKategorie(null));
        Assert.Equal("ID.prove 3.0 SaaS", FaqKatalog.ProduktName("idprove-saas"));
        Assert.True(WikiThema.IstBekannt(WikiThema.Faq));
    }
}
