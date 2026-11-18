using Rausoft.Erp.OdooImport;

namespace Rausoft.Erp.Tests.OdooImport;

public class AnfragenAuswertungTests
{
    private static readonly DateTime Heute = new(2026, 11, 18, 10, 0, 0);

    private static AnfrageLeadZeile Lead(DateTime angelegt, string? formular = null, string[]? felder = null,
        bool aktiv = true, bool kontakt = true, bool nameWieFormular = false,
        string? utmSource = null, string? utmMedium = null, string? odooQuelle = null) =>
        new(angelegt, aktiv, formular, felder ?? [], kontakt, HatEmail: true, nameWieFormular, utmSource, utmMedium, odooQuelle);

    private static readonly Dictionary<string, string> Vorhanden = new()
    {
        ["x_studio_form_source"] = "Formular",
        ["x_studio_gclid"] = "GCLID",
        ["x_studio_utm_source"] = "UTM Source",
        ["source_id"] = "Quelle",
    };

    [Fact]
    public void Bericht_ZaehltJeJahr_UndTrenntSicherVonNurAmNamenErkennbar()
    {
        AnfrageLeadZeile[] zeilen =
        [
            // 2024: nichts gepflegt, eine nur am Namen erkennbar
            Lead(new DateTime(2024, 3, 1)),
            Lead(new DateTime(2024, 5, 1), nameWieFormular: true),
            // 2026: Formular + Herkunft, eine davon mit frischer Klick-Kennung, eine verloren (archiviert)
            Lead(new DateTime(2026, 2, 1), "Infomappe", ["x_studio_utm_source", "x_studio_gclid"],
                utmSource: "google", utmMedium: "cpc", odooQuelle: "Suchmaschine"),
            Lead(new DateTime(2026, 11, 1), "infomappe", ["x_studio_gclid", "source_id"], aktiv: false, kontakt: false),
            Lead(new DateTime(2026, 6, 1), "Testversion"),
        ];

        var bericht = AnfragenAuswertung.Bericht(zeilen, Vorhanden, ["x_studio_wbraid"],
            new Dictionary<string, string> { ["x_studio_sonstiges"] = "Sonstiges" }, Heute);

        Assert.Contains("Verkaufschancen gesamt (auch archivierte/verlorene): 5", bericht);
        // Spalten: Jahr, gesamt, Formular, Herkunft, Klick-K., Odoo-Kanal, nur Name, m. Kontakt
        Assert.Matches(@"2024\s+2\s+0\s+0\s+0\s+0\s+1\s+2\b", bericht);
        Assert.Matches(@"2026\s+3\s+3\s+2\s+2\s+1\s+0\s+2\b", bericht);
        Assert.Contains("Formularfeld gefüllt:   3 · von 01.02.2026 bis 01.11.2026", bericht);
        Assert.Contains("Herkunft gefüllt:       2 · von 01.02.2026 bis 01.11.2026", bericht);
        // Nur die Anfrage vom 01.11. ist jünger als 90 Tage
        Assert.Contains("(nur diese würden übernommen): 1", bericht);
        Assert.Contains("sicher als Website-Anfrage erkennbar (Formular oder Herkunft): 3", bericht);
        Assert.Contains("davon mit Kontakt verknüpft: 2", bericht);
        Assert.Contains("davon archiviert/verloren:   1", bericht);
        Assert.Matches(@"nur am Namen erkennbar \(ohne Herkunft, unsicher\):\s+1", bericht);
        // Formularwerte unabhängig von der Schreibweise zusammengezählt
        Assert.Matches(@"\s2\s+Infomappe", bericht);
        Assert.Matches(@"\s1\s+Testversion", bericht);
        Assert.Contains("google / cpc", bericht);
        Assert.Contains("Suchmaschine", bericht);
        // Fehlende und unbekannte eigene Felder werden genannt
        Assert.Contains("NICHT vorhanden: 1", bericht);
        Assert.Contains("x_studio_wbraid", bericht);
        Assert.Contains("x_studio_sonstiges", bericht);
    }

    [Fact]
    public void Bericht_OhneVerkaufschancen_BleibtLesbar()
    {
        var bericht = AnfragenAuswertung.Bericht([], Vorhanden, [], new Dictionary<string, string>(), Heute);
        Assert.Contains("Keine Verkaufschancen gefunden.", bericht);
    }

    [Theory]
    [InlineData("Infomappe - Muster AG", true)]
    [InlineData("TESTVERSION Beispiel GmbH", true)]
    [InlineData("Angebot Lizenzerweiterung", false)]
    [InlineData(null, false)]
    public void NameNenntFormular(string? name, bool erwartet) =>
        Assert.Equal(erwartet, AnfragenAuswertung.NameNenntFormular(name));
}
