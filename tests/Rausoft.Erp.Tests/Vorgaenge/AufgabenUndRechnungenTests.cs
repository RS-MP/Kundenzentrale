using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Domain.Vorgaenge;
using Rausoft.Erp.OdooImport;

namespace Rausoft.Erp.Tests.Vorgaenge;

/// <summary>
/// Gruppen der Liste „Meine Aufgaben" und die Zuordnung gebuchter Odoo-Rechnungen
/// zu Aufträgen für das automatische Abhaken von „Rechnung in Odoo stellen".
/// </summary>
public class AufgabenUndRechnungenTests
{
    private static readonly DateOnly Heute = new(2026, 10, 28);

    [Theory]
    [InlineData(-1, false, AufgabenGruppen.Ueberfaellig)]
    [InlineData(0, false, AufgabenGruppen.Heute)]
    [InlineData(1, false, AufgabenGruppen.Woche)]
    [InlineData(7, false, AufgabenGruppen.Woche)]
    [InlineData(8, false, AufgabenGruppen.Spaeter)]
    [InlineData(-5, true, AufgabenGruppen.Wartet)]
    public void GruppeNachFaelligkeit(int tage, bool wartetNoch, string erwartet) =>
        Assert.Equal(erwartet, AufgabenGruppen.Gruppe(Heute.AddDays(tage), wartetNoch, Heute));

    [Fact]
    public void OhneDatumKommtNachHintenUndWartendeGanzZuletzt()
    {
        Assert.Equal(AufgabenGruppen.Spaeter, AufgabenGruppen.Gruppe(null, false, Heute));
        Assert.True(AufgabenGruppen.Reihenfolge(AufgabenGruppen.Ueberfaellig) < AufgabenGruppen.Reihenfolge(AufgabenGruppen.Heute));
        Assert.True(AufgabenGruppen.Reihenfolge(AufgabenGruppen.Spaeter) < AufgabenGruppen.Reihenfolge(AufgabenGruppen.Wartet));
    }

    [Fact]
    public void RechnungWirdExaktUeberDieAuftragsnummerZugeordnet()
    {
        var rechnungen = new[]
        {
            new OdooRechnung("RE/2026/00800", "S01194", new DateOnly(2026, 11, 2)),
            new OdooRechnung("RE/2026/00790", "S01194, S01241", new DateOnly(2026, 10, 30)),
            new OdooRechnung("RE/2026/00700", "S0119", new DateOnly(2026, 9, 1)),   // ähnliche Nummer, anderer Auftrag
        };

        var treffer = AuftragsRechnungen.Zuordnen(rechnungen, ["S01241", "s01194", "S0999"]);

        Assert.Equal("RE/2026/00790", treffer["S01241"].Name);
        Assert.Equal("RE/2026/00790", treffer["S01194"].Name);   // früheste Rechnung zum Auftrag
        Assert.False(treffer.ContainsKey("S0999"));
        Assert.DoesNotContain(treffer.Values, r => r.Name == "RE/2026/00700");
    }

    [Fact]
    public void FilterVerknuepftMehrereAuftraegeMitOder()
    {
        var filter = AuftragsRechnungen.Filter(["S1", "S2", "S3"]);

        // 2 feste Bedingungen + 2 ODER + 3 Herkunftsbedingungen
        Assert.Equal(7, filter.Length);
        Assert.Equal("|", filter[2]);
        Assert.Equal("|", filter[3]);
        Assert.Equal(new object[] { "invoice_origin", "ilike", "S3" }, (object[])filter[6]);
        Assert.Single(AuftragsRechnungen.Filter(["S1"]).OfType<object[]>(), b => (string)b[0] == "invoice_origin");
    }

    [Fact]
    public void AuftragsauswahlSuchtBestaetigteAuftraegeDesPartnersUndSeinerKontakte()
    {
        var filter = KundenAuftraege.Filter(4711);

        Assert.Equal(new object[] { "partner_id", "child_of", 4711 }, (object[])filter[0]);
        var status = (object[])filter[1];
        Assert.Equal("state", status[0]);
        Assert.Equal(new[] { "sale", "done" }, (string[])status[2]);
    }

    [Fact]
    public void AuftragsinhaltZeigtDreiProdukteUndDenRestAlsZahl()
    {
        Assert.Null(KundenAuftraege.Inhalt([]));
        Assert.Equal("Shared Cloud", KundenAuftraege.Inhalt(["Shared Cloud", "shared cloud ", " "]));
        Assert.Equal("A, B, C +2", KundenAuftraege.Inhalt(["A", "B", "C", "D", "E"]));
    }

    [Fact]
    public void AutomatischesAbhakenGehtAuchBeiGesperrtemSchritt()
    {
        var vorlage = new List<VorgangstypSchritt>
        {
            new() { Nr = 1, Titel = "Leistung erbringen", BereichId = 1, FristTage = 20 },
            new() { Nr = 2, Titel = "Rechnung in Odoo", BereichId = 2, FristTage = 3, WartetAufNr = 1, AutoAbhaken = AutoAbhaken.OdooRechnung },
        };
        var s = VorgangsLogik.SchritteErzeugen(vorlage, new HashSet<int>(), [], [], Heute);
        Assert.True(VorgangsLogik.Gesperrt(s[1]));

        VorgangsLogik.Erledigen(s[1], s, Heute, DateTime.UtcNow, null, trotzSperre: true);

        Assert.Equal(AufgabenStatus.Erledigt, s[1].Status);
        Assert.Null(s[1].ErledigtVonId);
    }
}
