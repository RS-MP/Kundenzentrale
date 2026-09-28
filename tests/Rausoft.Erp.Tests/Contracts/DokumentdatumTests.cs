using Rausoft.Erp.Domain.Contracts;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Tests.Contracts;

public class DokumentdatumTests
{
    [Theory]
    [InlineData("Bestellung 2026-09-08.pdf", 2026, 9, 8)]
    [InlineData("4228_08.09.2026.pdf", 2026, 9, 8)]
    [InlineData("4228 Nachtrag 8.9.26.docx", 2026, 9, 8)]
    [InlineData("BE_20250131.pdf", 2025, 1, 31)]
    [InlineData("Bestellung_2024_11_05_Motorex AG.pdf", 2024, 11, 5)]
    public void AusDateiname_ErkenntGaengigeFormate(string datei, int jahr, int monat, int tag) =>
        Assert.Equal(new DateOnly(jahr, monat, tag), DokumentdatumErkennung.AusDateiname(datei));

    [Theory]
    [InlineData("4228.pdf")]           // Kundennummer, kein Datum
    [InlineData("4228-1.pdf")]
    [InlineData("123456.pdf")]
    [InlineData("Bestellung 31.02.2026.pdf")] // ungültiges Datum
    [InlineData("S012345678.pdf")]      // Ziffernfolge, kein Datum
    public void AusDateiname_OhneDatum_Null(string datei) =>
        Assert.Null(DokumentdatumErkennung.AusDateiname(datei));

    [Theory]
    [InlineData("4228.pdf", 0)]
    [InlineData("4228-1.pdf", 1)]
    [InlineData("4228-12.docx", 12)]
    [InlineData("Bestellung 2026-09-08.pdf", 0)] // Datumsteil ist keine Folgenummer
    public void Folge_AusSuffix(string datei, int folge) =>
        Assert.Equal(folge, DokumentdatumErkennung.Folge(datei));

    [Fact]
    public void Stand_UnterzeichnetVorDokumentdatum()
    {
        var bestandteil = new Vertragsbestandteil
        {
            Titel = "Bestellung",
            Dokumentdatum = new DateOnly(2026, 1, 1),
            UnterzeichnetAm = new DateOnly(2026, 2, 1),
        };
        Assert.Equal(new DateOnly(2026, 2, 1), bestandteil.Stand);
    }

    [Theory]
    [InlineData(MitarbeiterRolle.Keine, MitarbeiterRolle.Support, true)]      // keine Einschränkung = alle
    [InlineData(MitarbeiterRolle.Vertrieb, MitarbeiterRolle.Vertrieb, true)]
    [InlineData(MitarbeiterRolle.Vertrieb, MitarbeiterRolle.Support, false)]
    [InlineData(MitarbeiterRolle.Vertrieb, MitarbeiterRolle.Keine, false)]
    [InlineData(MitarbeiterRolle.Vertrieb, MitarbeiterRolle.Compliance, true)] // Compliance/Admin immer
    [InlineData(MitarbeiterRolle.Vertrieb, MitarbeiterRolle.Admin, true)]
    public void Vorlage_NutzungsfreigabeJeRolle(MitarbeiterRolle erlaubt, MitarbeiterRolle benutzer, bool ergebnis) =>
        Assert.Equal(ergebnis, new Vorlage { Name = "NDA", ErlaubteRollen = erlaubt }.ErlaubtFuer(benutzer));
}
