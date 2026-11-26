using System.Xml.Linq;
using Rausoft.Erp.Domain.Customers;

namespace Rausoft.Erp.Tests.Customers;

/// <summary>
/// Zuordnung einer Outlook-Mail zum Kunden und das Manifest des Add-ins. Alle Adressen sind erfunden.
/// </summary>
public class MailZuordnungTests
{
    private static readonly string[] Eigene = ["firma.example"];

    [Theory]
    [InlineData(" Erika.Beispiel@Kunde.Example ", "erika.beispiel@kunde.example")]
    [InlineData("<erika@kunde.example>", "erika@kunde.example")]
    [InlineData("kein-at", null)]
    [InlineData("zwei@at@kunde.example", null)]
    [InlineData("erika@ohnepunkt", null)]
    [InlineData("mit leer@kunde.example", null)]
    [InlineData("", null)]
    public void AdressenWerdenNormalisiertOderVerworfen(string text, string? erwartet) =>
        Assert.Equal(erwartet, MailZuordnung.Normalisieren(text));

    [Fact]
    public void EigeneAdressenZaehlenNicht()
    {
        // Gesendete Mail: Absender ist ein Kollege, es zählen die Empfänger
        var fremde = MailZuordnung.Fremde(["kollege@firma.example", "Erika@Kunde.Example", "erika@kunde.example", null, "unsinn"], Eigene);

        Assert.Equal(["erika@kunde.example"], fremde);
    }

    [Fact]
    public void UeberFreemailWirdKeinKundeGesucht()
    {
        var domaenen = MailZuordnung.SuchDomaenen(["erika@kunde.example", "privat@gmail.com", "max@gmx.de", "b@kunde.example"]);

        Assert.Equal(["kunde.example"], domaenen);
    }

    [Fact]
    public void AdresseSchlaegtDomaeneUndMehrereKundenSindNieEindeutig()
    {
        Assert.Equal("adresse: 7", Ergebnis(true, [7, 7], [8, 9]));
        Assert.Equal("mehrdeutig: 7, 8", Ergebnis(true, [7, 8], []));
        Assert.Equal("domaene: 8", Ergebnis(true, [], [8, 8]));
        Assert.Equal("mehrdeutig: 8, 9", Ergebnis(true, [], [8, 9]));
        Assert.Equal("keine: ", Ergebnis(true, [], []));
        Assert.Equal("intern: ", Ergebnis(false, [7], [8]));
    }

    private static string Ergebnis(bool hatFremde, int[] adresse, int[] domaene)
    {
        var (art, kunden) = MailZuordnung.Entscheiden(hatFremde, adresse, domaene);
        return $"{art}: {string.Join(", ", kunden)}";
    }

    [Theory]
    [InlineData(false, OutlookManifest.KennungProduktiv, "Kundenzentrale")]
    [InlineData(true, OutlookManifest.KennungTest, "Kundenzentrale (Test)")]
    public void ManifestZeigtAufDieEigeneInstanz(bool test, string kennung, string name)
    {
        var xml = XDocument.Parse(OutlookManifest.Erzeugen("https://server.example:8443/Kundenzentrale/", test));
        XNamespace ns = "http://schemas.microsoft.com/office/appforoffice/1.1";
        XNamespace vo = "http://schemas.microsoft.com/office/mailappversionoverrides";
        XNamespace bt = "http://schemas.microsoft.com/office/officeappbasictypes/1.0";
        var wurzel = xml.Root!;

        Assert.Equal(kennung, wurzel.Element(ns + "Id")!.Value);
        Assert.Equal(name, wurzel.Element(ns + "DisplayName")!.Attribute("DefaultValue")!.Value);
        Assert.Equal("https://server.example:8443", wurzel.Element(ns + "AppDomains")!.Element(ns + "AppDomain")!.Value);
        // Nur die geöffnete Mail lesen — kein Schreiben, kein Postfachzugriff
        Assert.Equal("ReadItem", wurzel.Element(ns + "Permissions")!.Value);
        Assert.Equal("MessageReadCommandSurface",
            wurzel.Descendants(vo + "ExtensionPoint").Single().Attributes().Single(a => a.Name.LocalName == "type").Value);
        // Alle festen Adressen (Verweise im Menüband tragen nur eine Kennung, keinen Wert)
        var adressen = wurzel.Descendants(bt + "Url").Concat(wurzel.Descendants(bt + "Image")).Concat(wurzel.Descendants(ns + "SourceLocation"))
            .Select(e => e.Attribute("DefaultValue")?.Value).OfType<string>().ToList();
        Assert.Equal(5, adressen.Count);
        Assert.NotEmpty(adressen);
        Assert.All(adressen, a => Assert.StartsWith("https://server.example:8443/Kundenzentrale/", a));
        Assert.Contains("https://server.example:8443/Kundenzentrale/outlook-addin/aufgabenbereich.html", adressen);
    }

    [Theory]
    [InlineData("http://server.example/Kundenzentrale/")]      // Outlook lädt Add-ins nur über https
    [InlineData("https://server.example/Kundenzentrale")]      // ohne Schrägstrich entstünden falsche Adressen
    [InlineData("unsinn")]
    public void ManifestNurFuerHttpsAdressen(string basis) =>
        Assert.Throws<ArgumentException>(() => OutlookManifest.Erzeugen(basis, false));
}
