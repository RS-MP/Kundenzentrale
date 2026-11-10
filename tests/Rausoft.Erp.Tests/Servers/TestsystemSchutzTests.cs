using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.OdooImport;

namespace Rausoft.Erp.Tests.Servers;

public class TestsystemSchutzTests
{
    private const string Wurzel = @"D:\Kundenzentrale-Test\Ablage";

    [Theory]
    [InlineData(@"D:\Kundenzentrale-Test\Ablage", true)]
    [InlineData(@"D:\Kundenzentrale-Test\Ablage\", true)]
    [InlineData(@"d:\kundenzentrale-test\ablage\Vorlagen", true)]
    [InlineData(@"D:\Kundenzentrale-Test\Ablage\..\Ablage\Erzeugt", true)]
    [InlineData(@"D:\Kundenzentrale-Test\Ablage2", false)]
    [InlineData(@"D:\Kundenzentrale-Test\Ablage\..\..\Kunden Archiv", false)]
    [InlineData(@"D:\Kunden Archiv\2_Wartungsverträge", false)]
    [InlineData(@"\\RS-W2019-01\Kunden Archiv", false)]
    public void LiegtUnter_erkennt_nur_Pfade_in_der_Wurzel(string pfad, bool erwartet) =>
        Assert.Equal(erwartet, TestsystemSchutz.LiegtUnter(pfad, Wurzel));

    [Fact]
    public void Ersatz_stellt_fremdes_Verzeichnis_auf_den_Unterordner_der_Wurzel_um() =>
        Assert.Equal(
            Path.Combine(Wurzel, "Erzeugt"),
            TestsystemSchutz.Ersatz(EinstellungsSchluessel.VertragserzeugungZielverzeichnis,
                @"\\RS-W2019-01\Kunden Archiv\2_Wartungsverträge", Wurzel));

    [Fact]
    public void Ersatz_leert_ein_Signatur_Zertifikat_ausserhalb_der_Wurzel() =>
        Assert.Equal("", TestsystemSchutz.Ersatz(EinstellungsSchluessel.VertragserzeugungSignaturZertifikat,
            @"C:\Kundenzentrale-Zertifikat\signatur.pfx", Wurzel));

    [Theory]
    [InlineData(EinstellungsSchluessel.VorlagenVerzeichnis, @"D:\Kundenzentrale-Test\Ablage\Vorlagen")] // schon innerhalb
    [InlineData(EinstellungsSchluessel.DokumentablageVerzeichnis, "")]                                   // nicht eingerichtet
    [InlineData(EinstellungsSchluessel.OdooUrl, "https://odoo.example")]                                 // kein Pfad
    public void Ersatz_laesst_alles_andere_unveraendert(string schluessel, string wert) =>
        Assert.Null(TestsystemSchutz.Ersatz(schluessel, wert, Wurzel));

    [Fact]
    public void Jede_Verzeichnis_Einstellung_ist_abgedeckt()
    {
        // Neue *.Verzeichnis-Einstellungen müssen in den Schutz aufgenommen werden
        var verzeichnisSchluessel = typeof(EinstellungsSchluessel).GetFields()
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(s => s.EndsWith("verzeichnis", StringComparison.OrdinalIgnoreCase));
        Assert.All(verzeichnisSchluessel, s => Assert.True(TestsystemSchutz.IstPfadEinstellung(s), s));
    }

    [Fact]
    public void Schluesselbund_und_Datenbank_folgen_der_Aufruf_Option()
    {
        try
        {
            ImportUmgebung.DbOption = "Server=RS-W2019-01;Database=Kundenzentrale_Test;Trusted_Connection=True";
            ImportUmgebung.SchluesselOption = @"C:\ProgramData\Rausoft\Kundenzentrale-Test\Schluessel";
            Assert.Equal("Kundenzentrale_Test auf RS-W2019-01", ImportUmgebung.ZielText());
            Assert.Equal(@"C:\ProgramData\Rausoft\Kundenzentrale-Test\Schluessel", ImportUmgebung.SchluesselOrdner);
            // Aufruf aus der Web-App: deren Connection String gewinnt immer
            Assert.Equal("Server=x;Database=y", ImportUmgebung.Verbindung("Server=x;Database=y"));
        }
        finally
        {
            ImportUmgebung.DbOption = null;
            ImportUmgebung.SchluesselOption = null;
        }
    }
}
