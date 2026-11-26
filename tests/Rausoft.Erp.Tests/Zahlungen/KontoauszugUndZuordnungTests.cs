using System.Text;
using Rausoft.Erp.Domain.Zahlungen;

namespace Rausoft.Erp.Tests.Zahlungen;

/// <summary>
/// Kontoauszug im Format CSV-MT940 lesen und Zahlungseingänge den offenen Rechnungen zuordnen.
/// Alle Namen, Konten und Beträge sind erfunden.
/// </summary>
public class KontoauszugUndZuordnungTests
{
    private const string Kopf =
        "\"Auftragskonto\";\"Buchungstag\";\"Valutadatum\";\"Buchungstext\";\"Verwendungszweck\";\"Beguenstigter/Zahlungspflichtiger\";\"Kontonummer\";\"BLZ\";\"Betrag\";\"Waehrung\";\"Info\"\n";

    private static string Zeile(string tag, string zweck, string name, string betrag, string info = "Umsatz gebucht") =>
        $"\"DE00123456780000000001\";\"{tag}\";\"{tag}\";\"GUTSCHR. UEBERWEISUNG\";\"{zweck}\";\"{name}\";\"DE00999999990000000002\";\"TESTDEFFXXX\";\"{betrag}\";\"EUR\";\"{info}\"\n";

    // --- Lesen ---------------------------------------------------------------------------

    [Fact]
    public void NurGebuchteEingaengeWerdenUebernommen()
    {
        var text = Kopf
                   + Zeile("24.11.26", "RE/2026/00123 Kd 4711", "Muster GmbH", "1.190,00")
                   + Zeile("24.11.26", "Gehalt November", "Erika Beispiel", "-3.200,00")
                   + Zeile("25.11.26", "RE/2026/00124", "Beispiel AG", "59,50", "Umsatz vorgemerkt")
                   + Zeile("25.11.26", "Miete", "Vermieter KG", "-800,00");

        var inhalt = KontoauszugLeser.Lesen(text);

        Assert.Null(inhalt.Fehler);
        var eingang = Assert.Single(inhalt.Eingaenge);
        Assert.Equal(new DateOnly(2026, 11, 24), eingang.Buchungstag);
        Assert.Equal(1190.00m, eingang.Betrag);
        Assert.Equal("Muster GmbH", eingang.Zahler);
        Assert.Equal("EUR", eingang.Waehrung);
        Assert.Equal(2, inhalt.Abgaenge);
        Assert.Equal(1, inhalt.Vorgemerkt);
        // Abgänge tauchen nirgends auf — auch nicht als Text
        Assert.DoesNotContain(inhalt.Eingaenge, e => e.Verwendungszweck.Contains("Gehalt"));
    }

    [Fact]
    public void DieselbeBuchungHatInJederDateiDenselbenFingerabdruck()
    {
        var a = KontoauszugLeser.Lesen(Kopf + Zeile("24.11.26", "RE/2026/00123", "Muster GmbH", "100,00"));
        var b = KontoauszugLeser.Lesen(Kopf + Zeile("23.11.26", "Anderes", "Jemand", "5,00") + Zeile("24.11.26", "RE/2026/00123", "Muster GmbH", "100,00"));

        Assert.Equal(a.Eingaenge[0].Fingerabdruck, b.Eingaenge[1].Fingerabdruck);
        Assert.Equal(64, a.Eingaenge[0].Fingerabdruck.Length);
    }

    [Fact]
    public void ZweiWirklichGleicheBuchungenBleibenZwei()
    {
        var zeile = Zeile("24.11.26", "Abschlag", "Muster GmbH", "100,00");

        var inhalt = KontoauszugLeser.Lesen(Kopf + zeile + zeile);

        Assert.Equal(2, inhalt.Eingaenge.Count);
        Assert.NotEqual(inhalt.Eingaenge[0].Fingerabdruck, inhalt.Eingaenge[1].Fingerabdruck);
    }

    [Fact]
    public void ZeilenumbruchUndAnfuehrungszeichenImFeld()
    {
        var text = Kopf + "\"DE00\";\"24.11.2026\";\"24.11.2026\";\"GUTSCHRIFT\";\"Rechnung \"\"RE/2026/00123\"\"\nvielen Dank\";\"Muster; GmbH\";\"\";\"\";\"12,50\";\"EUR\";\"Umsatz gebucht\"\n";

        var eingang = Assert.Single(KontoauszugLeser.Lesen(text).Eingaenge);

        Assert.Equal("Rechnung \"RE/2026/00123\" vielen Dank", eingang.Verwendungszweck);
        Assert.Equal("Muster; GmbH", eingang.Zahler);
        Assert.Equal(12.50m, eingang.Betrag);
    }

    [Fact]
    public void CsvCamtWirdAnDenUeberschriftenErkannt()
    {
        var text = "\"Auftragskonto\";\"Buchungstag\";\"Valutadatum\";\"Buchungstext\";\"Verwendungszweck\";\"Glaeubiger ID\";\"Mandatsreferenz\";\"Kundenreferenz (End-to-End)\";\"Sammlerreferenz\";\"Lastschrift Ursprungsbetrag\";\"Auslagenersatz Ruecklastschrift\";\"Beguenstigter/Zahlungspflichtiger\";\"Kontonummer/IBAN\";\"BIC (SWIFT-Code)\";\"Betrag\";\"Waehrung\";\"Info\"\n"
                   + "\"DE00\";\"24.11.26\";\"24.11.26\";\"GUTSCHRIFT\";\"RE/2026/00123\";\"\";\"\";\"\";\"\";\"\";\"\";\"Muster GmbH\";\"DE00999\";\"TESTDEFF\";\"77,00\";\"EUR\";\"Umsatz gebucht\"\n";

        var eingang = Assert.Single(KontoauszugLeser.Lesen(text).Eingaenge);

        Assert.Equal("Muster GmbH", eingang.Zahler);
        Assert.Equal(77m, eingang.Betrag);
    }

    [Theory]
    [InlineData(":20:STARTUMS\n:25:12345678/0000000001\n")]
    [InlineData("<?xml version=\"1.0\"?><Document></Document>")]
    [InlineData("Datum;Text\n24.11.2026;irgendwas\n")]
    public void FremdeFormateWerdenAbgelehntStattFalschGelesen(string text)
    {
        var inhalt = KontoauszugLeser.Lesen(text);

        Assert.NotNull(inhalt.Fehler);
        Assert.Empty(inhalt.Eingaenge);
    }

    [Fact]
    public void UmlauteAusLatin1UndUtf8()
    {
        var text = Kopf + Zeile("24.11.26", "Rechnung für Möbel", "Müller & Söhne", "10,00");

        var latin1 = KontoauszugLeser.Lesen(KontoauszugLeser.Text(Encoding.Latin1.GetBytes(text)));
        var utf8 = KontoauszugLeser.Lesen(KontoauszugLeser.Text([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(text)]));

        Assert.Equal("Müller & Söhne", latin1.Eingaenge[0].Zahler);
        Assert.Equal("Müller & Söhne", utf8.Eingaenge[0].Zahler);
        Assert.Equal(latin1.Eingaenge[0].Fingerabdruck, utf8.Eingaenge[0].Fingerabdruck);
    }

    // --- Zuordnen ------------------------------------------------------------------------

    private static OffeneRechnung Rechnung(string nummer, decimal offen, string kunde = "Muster GmbH", string waehrung = "EUR") =>
        new(nummer, kunde, 1, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 15), offen, offen, waehrung, null);

    private static readonly OffeneRechnung[] Offen =
    [
        Rechnung("RE/2026/00123", 1190.00m),
        Rechnung("RE/2026/0012", 50.00m, "Beispiel AG"),
        Rechnung("RE/2026/00124", 59.50m, "Beispiel AG"),
    ];

    [Theory]
    [InlineData("RE/2026/00123 Kd 4711")]
    [InlineData("Rechnung RE 2026 00123")]            // Bank hat die Schrägstriche durch Leerzeichen ersetzt
    [InlineData("SVWZ+RE/2026/0 0123 DANKE")]         // Umbruch mitten in der Nummer
    [InlineData("re-2026-00123")]
    public void NummerImVerwendungszweckUndBetragExaktIstEindeutig(string zweck)
    {
        var z = ZahlungsZuordnung.Pruefen(zweck, "Muster GmbH", 1190.00m, "EUR", Offen);

        Assert.Equal(ZuordnungsArt.Eindeutig, z.Art);
        Assert.Equal("RE/2026/00123", Assert.Single(z.Rechnungen).Nummer);
    }

    [Fact]
    public void KuerzereNummerTrifftNichtDieLaengere()
    {
        // „RE/2026/0012" steckt als Anfang in „RE/2026/00123" — gemeint ist nur die lange
        var z = ZahlungsZuordnung.Pruefen("RE/2026/00123", null, 50.00m, "EUR", Offen);

        Assert.Equal(ZuordnungsArt.BetragWeichtAb, z.Art);
        Assert.Equal("RE/2026/00123", Assert.Single(z.Rechnungen).Nummer);
    }

    [Fact]
    public void AbweichenderBetragWirdNieAutomatischGebucht()
    {
        var z = ZahlungsZuordnung.Pruefen("RE/2026/00123 abzgl. 2% Skonto", "Muster GmbH", 1166.20m, "EUR", Offen);

        Assert.Equal(ZuordnungsArt.BetragWeichtAb, z.Art);
        Assert.Contains("Skonto", z.Grund);
    }

    [Fact]
    public void SammelueberweisungGehtInDieKlaerung()
    {
        var z = ZahlungsZuordnung.Pruefen("RE/2026/00123 und RE/2026/00124", "Muster GmbH", 1249.50m, "EUR", Offen);

        Assert.Equal(ZuordnungsArt.Mehrere, z.Art);
        Assert.Equal(2, z.Rechnungen.Count);
    }

    [Fact]
    public void OhneNummerIstEinPassenderBetragNurEinVorschlag()
    {
        var z = ZahlungsZuordnung.Pruefen("Vielen Dank fuer die gute Zusammenarbeit", "BEISPIEL AG", 59.50m, "EUR", Offen);

        Assert.Equal(ZuordnungsArt.NurBetrag, z.Art);
        Assert.Contains("Zahler passen", z.Grund);
    }

    [Fact]
    public void NichtsPasstOderFremdwaehrung()
    {
        Assert.Equal(ZuordnungsArt.Keine, ZahlungsZuordnung.Pruefen("Erstattung", "Finanzamt", 12.34m, "EUR", Offen).Art);
        Assert.Equal(ZuordnungsArt.Keine, ZahlungsZuordnung.Pruefen("RE/2026/00123", "Muster GmbH", 1190.00m, "USD", Offen).Art);
    }

    [Theory]
    [InlineData("12", false)]          // zu kurz — stünde zufällig in jedem Verwendungszweck
    [InlineData("ABCDEF", false)]      // ohne Ziffer keine Rechnungsnummer
    [InlineData("2026-0012", true)]
    public void NurBelastbareNummernWerdenGesucht(string nummer, bool erwartet) =>
        Assert.Equal(erwartet, ZahlungsZuordnung.Enthaelt(ZahlungsZuordnung.Kern("Zahlung 2026-0012 und 12 ABCDEF"), nummer));

    [Fact]
    public void ZiffernDavorOderDahinterGehoerenZuEinerAnderenNummer()
    {
        Assert.False(ZahlungsZuordnung.Enthaelt(ZahlungsZuordnung.Kern("Kd 9202600125"), "2026/0012"));
        Assert.False(ZahlungsZuordnung.Enthaelt(ZahlungsZuordnung.Kern("12026/0012"), "2026/0012"));
        Assert.True(ZahlungsZuordnung.Enthaelt(ZahlungsZuordnung.Kern("Nr. 2026/0012, danke"), "2026/0012"));
    }
}
