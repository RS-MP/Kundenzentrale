using Rausoft.Erp.Domain.Margen;

namespace Rausoft.Erp.Tests.Margen;

/// <summary>
/// Rechenregeln des Margenrechners. Alle Zahlen hier sind erfunden und so
/// gewählt, dass sich die Erwartungen von Hand nachrechnen lassen — echte
/// Preise und die Einkaufsstaffel stehen nirgends im Repository.
/// </summary>
public class MargenrechnungTests
{
    // Baskets 1.000 / 10.000 / 100.000 Cases; Tabelle bis 500.000 Cases, darüber 100.000er Stufen:
    // 5.000 bei 500.000 → 8.000 bei 1 Mio. → 10.000 bei 2 Mio., danach 0,005 je Case
    private static MargenDaten Daten(double ankerBeiEinerMio = 8000) => new(
        Baskets: [new(1_000, 50, 100), new(10_000, 300, 600), new(100_000, 1000, 2000)],
        Staffel: [new(100_000, 2000, 1000), new(200_000, 3000, 1500), new(500_000, 5000, 5000)],
        LinearSchritt: 100_000,
        LinearAnker: [new(500_000, 5000), new(1_000_000, ankerBeiEinerMio), new(2_000_000, 10_000)]);

    private static Margenrechnung Rechnung() => new(Daten());

    private static MargenErgebnis Rechne(int[] sanctionsOnly, int[] fullFile, double aS = 0, double aF = 0) =>
        Rechnung().Berechnen(new MargenEingabe(sanctionsOnly, fullFile, aS, aF));

    private static void Gleich(double erwartet, double? ist) => Assert.Equal(erwartet, ist!.Value, 6);

    [Theory]
    [InlineData(1, 0)]
    [InlineData(100_000, 0)]        // genau auf der Grenze gilt noch die Stufe
    [InlineData(100_001, 1)]        // dazwischen die höhere
    [InlineData(500_000, 2)]
    [InlineData(500_001, 3)]        // erste lineare Stufe
    [InlineData(600_000, 3)]
    [InlineData(2_000_000, 17)]
    [InlineData(2_000_001, 18)]
    public void Staffel_VolumenFaelltInDieRichtigeStufe(double volumen, int stufe) =>
        Assert.Equal(stufe, Rechnung().StufenIndex(volumen));

    [Fact]
    public void Staffel_OberhalbDerTabelleLinearZwischenDenAnkern()
    {
        var r = Rechnung();
        Assert.Equal(new Margenrechnung.Stufe(500_000, 5000, 5000), r.StufeAn(2));
        Assert.Equal(new Margenrechnung.Stufe(600_000, 5600, 5600), r.StufeAn(3));     // 5.000 + 1/5 von 3.000
        Assert.Equal(new Margenrechnung.Stufe(1_000_000, 8000, 8000), r.StufeAn(7));
        Assert.Equal(new Margenrechnung.Stufe(1_100_000, 8200, 8200), r.StufeAn(8));   // 8.000 + 1/10 von 2.000
        Assert.Equal(new Margenrechnung.Stufe(2_000_000, 10_000, 10_000), r.StufeAn(17));
        // über dem letzten Anker mit dessen Preis je Case weiter, ohne Obergrenze
        Assert.Equal(new Margenrechnung.Stufe(2_100_000, 10_500, 10_500), r.StufeAn(18));
        Assert.Equal(new Margenrechnung.Stufe(50_000_000, 250_000, 250_000), r.StufeAn(497));
        Assert.Equal(500_000, r.StufeAb(3));
        Assert.Equal(0, r.StufeAb(0));
    }

    [Fact]
    public void Staffel_LineareGebuehrWirdKaufmaennischGerundet()
    {
        // 5.000 + 1/5 von 3.002,50 = 5.600,50 → 5.601 (halbe aufwärts, nicht zur geraden Zahl)
        var r = new Margenrechnung(Daten(ankerBeiEinerMio: 8002.5));
        Assert.Equal(5601, r.StufeAn(3).FullFile);
    }

    [Fact]
    public void Einkauf_GebuehrDerStufeMalVolumenanteilJeBereich()
    {
        var r = Rechnung();
        // 60.000 Cases → erste Stufe: 2.000 × 5/6 + 1.000 × 1/6
        Gleich(1833.333333, r.Einkauf(50_000, 10_000));
        // 160.000 Cases → zweite Stufe: 3.000 × 15/16 + 1.500 × 1/16
        Gleich(2906.25, r.Einkauf(150_000, 10_000));
        Assert.Equal(0, r.Einkauf(0, 0));
    }

    [Fact]
    public void Bestand_VerkaufEinkaufGewinnUndMarge()
    {
        // 10 Kunden Sanctions Only mit 1.000er Basket, 5 Kunden Full File mit 10.000er Basket
        var e = Rechne([10, 0, 0], [0, 5, 0]);

        Assert.True(e.HatBestand);
        Gleich(3500, e.Kennzahlen.Verkaufspreis);            // 10 × 50 + 5 × 600
        Gleich(1833.333333, e.Kennzahlen.Einkaufspreis);
        Gleich(1666.666667, e.Kennzahlen.Gewinn);
        Gleich(0.476190, e.Kennzahlen.Marge);
        Assert.Equal(15, e.Kennzahlen.Kunden);
        Assert.Equal(100_000, e.Kennzahlen.StaffelBis);

        Gleich(60_000, e.Volumen.Cases);
        Gleich(5d / 6, e.Volumen.AnteilFullFile);
        Gleich(1d / 6, e.Volumen.AnteilSanctionsOnly);
        Assert.False(e.Volumen.Linear);
        Assert.Equal(2000, e.Volumen.GebuehrFullFile);
        Assert.Equal(1000, e.Volumen.GebuehrSanctionsOnly);

        Assert.Equal(10, e.Summen.KundenSanctionsOnly);
        Assert.Equal(5, e.Summen.KundenFullFile);
        Gleich(500, e.Summen.VkSanctionsOnly);
        Gleich(3000, e.Summen.VkFullFile);
        Gleich(166.666667, e.Summen.EkSanctionsOnly);
        Gleich(1666.666667, e.Summen.EkFullFile);
    }

    [Fact]
    public void Zeilen_EkAnteilBeitragUndWeitererKunde()
    {
        var e = Rechne([10, 0, 0], [0, 5, 0]);

        // 1.000er Basket Sanctions Only: Anteil 1.000 × 1.000 / 60.000
        var klein = e.Zeilen[0].SanctionsOnly;
        Assert.Equal(10, klein.Kunden);
        Gleich(50, klein.VkJeKunde);
        Gleich(16.666667, klein.EkAnteil);
        Gleich(33.333333, klein.Beitrag);

        // 10.000er Basket Full File: Anteil 10.000 × 2.000 / 60.000
        var mittel = e.Zeilen[1].FullFile;
        Gleich(333.333333, mittel.EkAnteil);
        Gleich(266.666667, mittel.Beitrag);

        // Ein weiterer Full-File-Kunde mit 100.000er Basket hebt den Bestand in die zweite Stufe:
        // Einkauf steigt von 1.833,33 auf 2.906,25 — vom VK 2.000 bleiben 927,08
        Gleich(927.083333, e.Zeilen[2].FullFile.WeitererKunde);
    }

    [Fact]
    public void Preisaenderung_WirktJeBereichAufDenVerkaufspreis()
    {
        var e = Rechne([10, 0, 0], [0, 5, 0], aS: 10, aF: -50);
        Gleich(55, e.Zeilen[0].SanctionsOnly.VkJeKunde);
        Gleich(300, e.Zeilen[1].FullFile.VkJeKunde);
        Gleich(550 + 1500, e.Kennzahlen.Verkaufspreis);
        Gleich(1833.333333, e.Kennzahlen.Einkaufspreis);     // der Einkauf hängt nur am Volumen
    }

    [Fact]
    public void Grenzwerte_PreisuntergrenzeMindestbestandUndNaechsteStaffel()
    {
        var g = Rechne([10, 0, 0], [0, 5, 0]).Grenzwerte!;

        Gleich(1833.333333 / 3500 - 1, g.Preisuntergrenze);

        // Gleiche Mischung: Verlust, solange der Umsatz unter dem Einkauf der ersten Stufe liegt —
        // Faktor 1.833,33 / 3.500 = 0,5238 → unter 31.429 Cases bzw. 8 Kunden (7,86 aufgerundet)
        Assert.Equal(MargenMindestbestandArt.AbKunden, g.Mindestbestand.Art);
        Assert.Equal(8, g.Mindestbestand.Kunden);
        Gleich(31428.571429, g.Mindestbestand.UnterCases);
        Assert.False(g.Mindestbestand.ImVerlust);
        Assert.Empty(g.Mindestbestand.WeitereZonen);

        // Noch 40.000 Cases bis zur Stufe bis 200.000: (3.000 − 2.000) × 5/6 + (1.500 − 1.000) × 1/6
        Gleich(916.666667, g.NaechsteStaffel.Mehrkosten);
        Gleich(40_000, g.NaechsteStaffel.RestCases);
        Assert.Equal(200_000, g.NaechsteStaffel.NaechsteBis);

        Assert.Equal(0, g.KundenUnterEinstand);
        Assert.Equal(0, g.LueckeUnterEinstand);
    }

    [Fact]
    public void Grenzwerte_KundenUnterEinstandUndIhreLuecke()
    {
        // 1 Kunde Sanctions Only mit 100.000er Basket neben 5 kleinen: 105.000 Cases → zweite Stufe.
        // Sein Anteil 100.000 × 1.500 / 105.000 = 1.428,57 liegt über dem VK von 1.000
        var e = Rechne([5, 0, 1], [0, 0, 0]);
        Assert.Equal(1, e.Grenzwerte!.KundenUnterEinstand);
        Gleich(428.571429, e.Grenzwerte.LueckeUnterEinstand);
        Assert.Contains(new MargenBasketBereich(100_000, MargenBereich.SanctionsOnly), e.Befunde.UnterEinstandMitKunden);
        Assert.True(e.Kennzahlen.Gewinn < 0);
        Assert.True(e.Grenzwerte.Preisuntergrenze > 0);     // die Preise müssten steigen
    }

    [Fact]
    public void Mindestbestand_NieWennDerVkJeCaseUnterDemEinkaufJeCaseLiegt()
    {
        // 100.000er Basket Sanctions Only um 90 % gesenkt: 100 je Kunde = 0,001 je Case, Einkauf am Ende 0,005
        var e = Rechne([0, 0, 1], [0, 0, 0], aS: -90);
        Assert.Equal(MargenMindestbestandArt.Nie, e.Grenzwerte!.Mindestbestand.Art);

        var dauerhaft = Assert.Single(e.Befunde.DauerhaftUnterEinkauf, x => x.Basket == 100_000 && x.Bereich == MargenBereich.SanctionsOnly);
        Gleich(100, dauerhaft.Vk);
        Assert.Equal(500, dauerhaft.Noetig);                // 0,005 × 100.000
        Gleich(0.005, e.Befunde.EkJeCaseUnendlich);

        var grenze = e.BasketGrenzen[2].SanctionsOnly;
        Assert.True(grenze.TraegtSich.Nie);
        Assert.True(grenze.AlleinGewinn.Nie);
    }

    [Fact]
    public void OhneBestand_KeineGrenzwerteAberTabellenUndBefunde()
    {
        var e = Rechne([0, 0, 0], [0, 0, 0]);
        Assert.False(e.HatBestand);
        Assert.Null(e.Grenzwerte);
        Assert.Empty(e.Kurve);
        Assert.Null(e.Kennzahlen.Marge);
        Assert.Null(e.Kennzahlen.StaffelBis);
        Assert.Null(e.Zeilen[0].SanctionsOnly.EkAnteil);
        Assert.Null(e.Zeilen[0].SanctionsOnly.Beitrag);
        // Der erste Kunde trägt die Gebühr der ersten Stufe allein: 50 − 1.000
        Gleich(-950, e.Zeilen[0].SanctionsOnly.WeitererKunde);
        Assert.Equal(3, e.BasketGrenzen.Count);
        Assert.Empty(e.Befunde.UnterEinstandMitKunden);
        Assert.Null(e.Befunde.MargeUnendlich);
    }

    [Fact]
    public void Befunde_MargeBeiGrossemVolumenUndStaffelEckdaten()
    {
        var e = Rechne([10, 0, 0], [0, 5, 0]);
        var jeCase = 3500d / 60_000;

        var anker = Assert.Single(e.Befunde.MargeBeiAnkern);   // nur der mittlere Anker (1 Mio.)
        Assert.Equal(1_000_000, anker.Cases);
        Gleich(1 - 8000 / (jeCase * 1_000_000), anker.Marge);
        Gleich(1 - 0.005 / jeCase, e.Befunde.MargeUnendlich);
        Assert.Equal(2_000_000, e.Befunde.LetzterAnkerCases);
        Assert.Equal(500_000, e.Befunde.LinearAb);
        Gleich(600, e.Befunde.MaxAnstiegJeStufe);              // 3.000 über fünf Stufen

        var w = e.Rechenweg;
        Assert.Equal(3, w.StufenAnzahl);
        Assert.Equal(100_000, w.ErsteStufeBis);
        Assert.Equal(500_000, w.LetzteStufeBis);
        Assert.Equal(1000, w.GebuehrVon);
        Assert.Equal(5000, w.GebuehrBis);
        Assert.Null(w.Anker[0].AnstiegJeStufe);
        Gleich(600, w.Anker[1].AnstiegJeStufe);
        Gleich(200, w.Anker[2].AnstiegJeStufe);                // 2.000 über zehn Stufen
        Gleich(500, w.AnstiegJeStufeUnendlich);                // 0,005 × 100.000
    }

    [Fact]
    public void Befunde_WeitererKundeDerDenGewinnSenkt_SchlechtesterZuerst()
    {
        var e = Rechne([10, 0, 0], [0, 5, 0]);
        var senkt = e.Befunde.WeitererKundeSenktGewinn;
        Assert.Equal(senkt.OrderBy(x => x.Aenderung).ToList(), senkt);
        Assert.All(senkt, x => Assert.True(x.Aenderung < 0));
        // Genau die Zellen der Tabelle mit negativem „+1 Kunde"
        var ausTabelle = e.Zeilen.SelectMany(z => new[] { z.SanctionsOnly.WeitererKunde, z.FullFile.WeitererKunde }).Count(x => x < 0);
        Assert.Equal(ausTabelle, senkt.Count);
    }

    [Fact]
    public void Verteilungen_NurMitBasketsDieEsInDenDatenGibt()
    {
        // Die Testdaten kennen keinen 500.000er Basket — vier der sechs Verteilungen entfallen
        var v = Rechne([0, 0, 0], [0, 0, 0]).Verteilungen;
        Assert.Equal(2, v.Count);

        var klein = v[0];                                      // 50 Kunden mit 1.000er Basket Sanctions Only
        Assert.Equal([50, 0, 0], klein.KundenSanctionsOnly);
        Gleich(50_000, klein.Cases);
        Assert.Equal(100_000, klein.StaffelBis);
        Gleich(2500, klein.Vk);
        Gleich(1000, klein.Ek);
        Gleich(1500, klein.Gewinn);

        var mittel = v[1];                                     // 4 Kunden mit 100.000er Basket Full File
        Assert.Equal([0, 0, 4], mittel.KundenFullFile);
        Gleich(8000, mittel.Vk);
        Gleich(5000, mittel.Ek);                               // 400.000 Cases → Stufe bis 500.000
    }

    [Fact]
    public void Kurve_VonZehntausendBisFuenfzigMillionenCases()
    {
        var e = Rechne([10, 0, 0], [0, 5, 0]);
        Gleich(Margenrechnung.KurveVon, e.Kurve[0].Cases);
        Assert.Equal(Margenrechnung.KurveBis, e.Kurve[^1].Cases, 3);
        Assert.All(e.Kurve, p => Assert.True(p.Marge >= -1.25));
        Assert.Equal(e.Kurve.Select(p => p.Cases).OrderBy(c => c).ToList(), e.Kurve.Select(p => p.Cases).ToList());
        // Jeder Punkt: Marge = 1 − Kosten der Stufe / Umsatz bei diesem Volumen
        var jeCase = 3500d / 60_000;
        var punkt = e.Kurve.First(p => p.Cases > 60_000);
        Gleich(1 - punkt.Kosten / (jeCase * punkt.Cases), punkt.Marge);
        Assert.Equal(100_000, punkt.StaffelBis);
    }

    [Fact]
    public void BasketGrenzen_TraegtSichUndAlleinGewinn()
    {
        var e = Rechne([10, 0, 0], [0, 5, 0]);

        // 1.000er Basket Sanctions Only (VK 50): deckt 1.000 × Gebühr / Volumen erst ab 20.000 Cases
        // (1.000 × 1.000 / 50); in der zweiten Stufe ab 30.000 → dort schon gedeckt
        var klein = e.BasketGrenzen[0].SanctionsOnly;
        Gleich(0.05, klein.VkJeCase);
        Assert.False(klein.TraegtSich.Nie);
        Assert.False(klein.TraegtSich.VonAnfangAn);
        Gleich(20_000, klein.TraegtSich.AbCases);
        Assert.False(klein.TraegtSich.JetztUngedeckt);         // Bestand hat 60.000 Cases
        Assert.Null(klein.TraegtSich.DauerhaftNichtAb);

        // Allein: n × 50 muss die 1.000 der ersten Stufe übersteigen → ab 21 Kunden;
        // ab 101 Kunden (zweite Stufe, 1.500) reichen 5.050 längst
        Assert.False(klein.AlleinGewinn.Nie);
        Assert.Equal(21, klein.AlleinGewinn.AbKunden);
        Assert.Null(klein.AlleinGewinn.DauerhaftVerlustAb);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-60, -60)]
    [InlineData(-85, 40)]
    [InlineData(150, -90)]
    public void AlleinGewinn_StimmtMitDemEinzelnenDurchzaehlenUeberein(double aS, double aF)
    {
        // Die Vorlage prüft jede Kundenzahl einzeln; die Rechnung geht je Stufe vor. Beides muss dasselbe ergeben.
        var daten = Daten();
        var r = new Margenrechnung(daten);
        var e = r.Berechnen(new MargenEingabe([0, 0, 0], [0, 0, 0], aS, aF));

        for (var i = 0; i < daten.Baskets.Count; i++)
        {
            var b = daten.Baskets[i];
            Assert.Equal(Durchgezaehlt(r, b.Cases, b.VkSanctionsOnly * (1 + aS / 100), MargenBereich.SanctionsOnly),
                Kurz(e.BasketGrenzen[i].SanctionsOnly.AlleinGewinn));
            Assert.Equal(Durchgezaehlt(r, b.Cases, b.VkFullFile * (1 + aF / 100), MargenBereich.FullFile),
                Kurz(e.BasketGrenzen[i].FullFile.AlleinGewinn));
        }
    }

    private static string Kurz(MargenAlleinGewinn a) =>
        $"{a.Nie}|{a.AbKunden}|{string.Join(",", a.VerlustWieder.Select(z => $"{z.Von}-{z.Bis}"))}|{a.WeitereVerlustbereiche}|{a.DauerhaftVerlustAb}";

    /// <summary>Verfahren der Vorlage: jede Kundenzahl bis zum Rechenhorizont einzeln prüfen.</summary>
    private static string Durchgezaehlt(Margenrechnung r, long basket, double preis, MargenBereich bereich)
    {
        var nMax = (long)Math.Floor(Margenrechnung.Horizont / basket);
        var zonen = new List<(long Von, long Bis)>();
        var offen = false;
        for (long n = 1; n <= nMax; n++)
        {
            var verlust = n * preis - r.StufeAn(r.StufenIndex(n * basket)).Gebuehr(bereich) <= 0;
            if (verlust && offen) zonen[^1] = (zonen[^1].Von, n);
            else if (verlust) zonen.Add((n, n));
            offen = verlust;
        }
        var zOffen = zonen.Count > 0 && zonen[^1].Bis >= nMax;
        if (zonen.Count == 0)
            return Kurz(new MargenAlleinGewinn(false, 1, [], false, null));
        if (zonen.Count == 1 && zonen[0].Von == 1 && zOffen)
            return Kurz(new MargenAlleinGewinn(true, 0, [], false, null));
        var abEins = zonen[0].Von == 1;
        var rest = abEins ? zonen.Skip(1).ToList() : zonen;
        var geschlossen = zOffen ? rest.Take(rest.Count - 1).ToList() : rest;
        return Kurz(new MargenAlleinGewinn(false, abEins ? zonen[0].Bis + 1 : 1,
            geschlossen.Take(2).Select(z => new MargenKundenSpanne(z.Von, z.Bis)).ToList(), geschlossen.Count > 2,
            zOffen ? zonen[^1].Von : null));
    }

    [Fact]
    public void Eingabe_WirdBegrenztStattZuScheitern()
    {
        var r = Rechnung();
        // zu kurze Liste, negative und übergroße Kundenzahlen, Preisänderung außerhalb −90 … 300 %
        var e = r.Berechnen(new MargenEingabe([-5, 2_000_000], null, 999, double.NaN));
        Assert.Equal(0, e.Zeilen[0].SanctionsOnly.Kunden);
        Assert.Equal(Margenrechnung.MaxKundenJeFeld, e.Zeilen[1].SanctionsOnly.Kunden);
        Assert.Equal(0, e.Zeilen[2].SanctionsOnly.Kunden);
        Assert.Equal(0, e.Summen.KundenFullFile);
        Gleich(50 * 4, e.Zeilen[0].SanctionsOnly.VkJeKunde);  // +300 %
        Gleich(100, e.Zeilen[0].FullFile.VkJeKunde);           // unlesbare Angabe = keine Änderung
    }

    [Fact]
    public void Daten_GueltigerStandHatKeineFehler() => Assert.Empty(Daten().Pruefen());

    [Fact]
    public void Daten_FehlerWerdenBenannt()
    {
        var d = Daten();
        Assert.NotEmpty((d with { Baskets = [] }).Pruefen());
        Assert.NotEmpty((d with { Baskets = [new(10_000, 1, 1), new(1_000, 1, 1)] }).Pruefen());      // nicht aufsteigend
        Assert.NotEmpty((d with { Baskets = [new(50, 1, 1)] }).Pruefen());                            // Basket zu klein
        Assert.NotEmpty((d with { Baskets = [new(1_000, -1, 1)] }).Pruefen());                        // negativer Preis
        Assert.NotEmpty((d with { Baskets = [new(1_000, double.NaN, 1)] }).Pruefen());
        Assert.NotEmpty((d with { Staffel = [] }).Pruefen());
        Assert.NotEmpty((d with { LinearSchritt = 100 }).Pruefen());                                  // Schrittweite zu klein
        Assert.NotEmpty((d with { LinearAnker = [] }).Pruefen());
        Assert.NotEmpty((d with { LinearAnker = [new(400_000, 5000), new(1_000_000, 8000)] }).Pruefen()); // beginnt nicht am Tabellenende
        Assert.NotEmpty((d with { LinearAnker = [new(500_000, 0)] }).Pruefen());
        Assert.NotEmpty((d with { Stand = new string('x', 101) }).Pruefen());
        Assert.Throws<ArgumentException>(() => new Margenrechnung(d with { Staffel = [] }));
    }

    [Fact]
    public void Daten_EinAnkerGenuegt_DarueberGiltSeinPreisJeCase()
    {
        var r = new Margenrechnung(Daten() with { LinearAnker = [new(500_000, 5000)] });
        Assert.Equal(6000, r.StufeAn(3).FullFile);             // 0,01 × 600.000
        var e = r.Berechnen(new MargenEingabe([10, 0, 0], [0, 5, 0]));
        Assert.Empty(e.Befunde.MargeBeiAnkern);
    }
}
