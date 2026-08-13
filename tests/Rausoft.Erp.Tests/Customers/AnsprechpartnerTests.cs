using Rausoft.Erp.Domain.Customers;

namespace Rausoft.Erp.Tests.Customers;

public class AnsprechpartnerTests
{
    [Fact]
    public void Briefanrede_Frau()
    {
        var ap = new Ansprechpartner { Anrede = Anrede.Frau, Nachname = "Berghoff" };
        Assert.Equal("Sehr geehrte Frau Berghoff", ap.Briefanrede());
    }

    [Fact]
    public void Briefanrede_Herr_MitTitel()
    {
        var ap = new Ansprechpartner { Anrede = Anrede.Herr, Titel = "Dr.", Nachname = "Stauch" };
        Assert.Equal("Sehr geehrter Herr Dr. Stauch", ap.Briefanrede());
    }

    [Fact]
    public void Briefanrede_OhneAnrede_DamenUndHerren()
    {
        var ap = new Ansprechpartner { Anrede = Anrede.Keine, Nachname = "Sammelpostfach" };
        Assert.Equal("Sehr geehrte Damen und Herren", ap.Briefanrede());
    }

    [Fact]
    public void Anzeigename_SetztTeileZusammen()
    {
        var ap = new Ansprechpartner { Titel = "Dr.", Vorname = "Eva", Nachname = "Berghoff" };
        Assert.Equal("Dr. Eva Berghoff", ap.Anzeigename());
    }
}
