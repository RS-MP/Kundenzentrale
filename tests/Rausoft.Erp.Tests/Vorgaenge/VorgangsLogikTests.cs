using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Domain.Vorgaenge;

namespace Rausoft.Erp.Tests.Vorgaenge;

/// <summary>
/// Regeln der Vorgänge (ANFORDERUNGEN.md, „Vorgänge", Entscheidungen 28.10.2026):
/// Schritte aus der Vorlage mit Bedingungen, Entfernen und manuellen Schritten,
/// Abhängigkeiten und Fristen, berechneter Vorgangsstatus.
/// </summary>
public class VorgangsLogikTests
{
    private static readonly DateOnly Start = new(2026, 10, 28);
    private static readonly DateTime Jetzt = new(2026, 10, 28, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>Wie „Neue Bestellung": 1 Mandant, 2 Vertrag senden, 3 Vertrag erfassen (wartet auf 2), 4 Rechnung, 5 Zugang (wartet auf 1), 6 Schulung (Bedingung).</summary>
    private static List<VorgangstypSchritt> NeueBestellung() =>
    [
        new() { Nr = 1, Titel = "Mandant anlegen", BereichId = 2, FristTage = 3, Pflichtfeld = "Mandanten-ID" },
        new() { Nr = 2, Titel = "Vertrag senden", BereichId = 1, FristTage = 2 },
        new() { Nr = 3, Titel = "Vertrag erfassen", BereichId = 1, FristTage = 14, WartetAufNr = 2 },
        new() { Nr = 4, Titel = "Rechnung in Odoo", BereichId = 3, FristTage = 3, AutoAbhaken = AutoAbhaken.OdooRechnung },
        new() { Nr = 5, Titel = "Zugang senden", BereichId = 2, FristTage = 2, WartetAufNr = 1 },
        new() { Nr = 6, Titel = "Schulung", BereichId = 4, FristTage = 7, Bedingung = "Schulung bestellt" },
    ];

    private static List<Aufgabe> Erzeugen(IReadOnlySet<int>? entfernt = null, string[]? bedingungen = null,
        ManuellerSchritt[]? manuelle = null) =>
        VorgangsLogik.SchritteErzeugen(NeueBestellung(), entfernt ?? new HashSet<int>(), bedingungen ?? [], manuelle ?? [], Start);

    [Fact]
    public void SchritteOhneVorgaengerStartenSofortMitFrist()
    {
        var s = Erzeugen(bedingungen: ["Schulung bestellt"]);

        Assert.Equal(6, s.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6], s.Select(x => x.Reihenfolge!.Value));
        Assert.Equal(Start.AddDays(3), s[0].Faellig);
        Assert.Equal(Start.AddDays(2), s[1].Faellig);
        Assert.Null(s[2].Faellig);                         // wartet auf „Vertrag senden"
        Assert.True(VorgangsLogik.Gesperrt(s[2]));
        Assert.Same(s[1], s[2].WartetAuf);
        Assert.Equal(Start.AddDays(7), s[5].Faellig);      // Schulung bestellt → aktiv
        Assert.Equal("Mandanten-ID", s[0].Pflichtfeld);
        Assert.Equal(AutoAbhaken.OdooRechnung, s[3].AutoAbhaken);
        Assert.All(s, x => Assert.Equal(SchrittHerkunft.Vorlage, x.Herkunft));
    }

    [Fact]
    public void NichtErfuellteBedingungLaesstSchrittEntfallen()
    {
        var s = Erzeugen();

        Assert.Equal(AufgabenStatus.Entfallen, s[5].Status);
        Assert.Equal(VorgangsLogik.GrundBedingung, s[5].EntfallenGrund);
        Assert.Null(s[5].Faellig);
    }

    [Fact]
    public void BedingungIstUnabhaengigVonGrossKleinschreibung()
    {
        var s = Erzeugen(bedingungen: [" schulung BESTELLT "]);
        Assert.Equal(AufgabenStatus.Offen, s[5].Status);
    }

    [Fact]
    public void EntfernterVorgaengerGibtWartendenSchrittFrei()
    {
        // „Vertrag senden" entfernt → „Vertrag erfassen" startet sofort
        var s = Erzeugen(entfernt: new HashSet<int> { 2 });

        Assert.Equal(AufgabenStatus.Entfallen, s[1].Status);
        Assert.Equal(VorgangsLogik.GrundEntfernt, s[1].EntfallenGrund);
        Assert.Null(s[2].WartetAuf);
        Assert.False(VorgangsLogik.Gesperrt(s[2]));
        Assert.Equal(Start.AddDays(14), s[2].Faellig);
    }

    [Fact]
    public void WartenderSchrittHaengtSichAnDenVorgaengerDesEntferntenSchritts()
    {
        var vorlage = new List<VorgangstypSchritt>
        {
            new() { Nr = 1, Titel = "A", BereichId = 1, FristTage = 1 },
            new() { Nr = 2, Titel = "B", BereichId = 1, FristTage = 1, WartetAufNr = 1 },
            new() { Nr = 3, Titel = "C", BereichId = 1, FristTage = 1, WartetAufNr = 2 },
        };
        var s = VorgangsLogik.SchritteErzeugen(vorlage, new HashSet<int> { 2 }, [], [], Start);

        Assert.Same(s[0], s[2].WartetAuf);
        Assert.True(VorgangsLogik.Gesperrt(s[2]));
    }

    [Fact]
    public void ManuelleSchritteKommenHintenDazuUndKoennenWarten()
    {
        var s = Erzeugen(manuelle:
        [
            new ManuellerSchritt("Altdaten übernehmen", 2, 5, WartetAufNr: 1),
            new ManuellerSchritt("Doku senden", 2, 3, WartetAufNr: null, ZustaendigId: 7),
        ]);

        Assert.Equal(8, s.Count);
        Assert.Equal(SchrittHerkunft.Manuell, s[6].Herkunft);
        Assert.Same(s[0], s[6].WartetAuf);
        Assert.Null(s[6].Faellig);
        Assert.Equal(7, s[7].ZustaendigId);
        Assert.Equal(Start.AddDays(3), s[7].Faellig);
        Assert.Equal(8, s[7].Reihenfolge);
    }

    [Fact]
    public void ErledigenGibtNachfolgerFreiUndSetztDessenFrist()
    {
        var s = Erzeugen();
        var heute = Start.AddDays(4);

        var frei = VorgangsLogik.Erledigen(s[1], s, heute, Jetzt, 5);

        Assert.Equal(AufgabenStatus.Erledigt, s[1].Status);
        Assert.Equal(5, s[1].ErledigtVonId);
        Assert.Equal([s[2]], frei);
        Assert.Equal(heute.AddDays(14), s[2].Faellig);
    }

    [Fact]
    public void GesperrterSchrittLaesstSichNichtErledigen()
    {
        var s = Erzeugen();
        Assert.Throws<InvalidOperationException>(() => VorgangsLogik.Erledigen(s[2], s, Start, Jetzt, null));
    }

    [Fact]
    public void EntfernenImLaufendenVorgangHaengtWartendeUm()
    {
        var s = Erzeugen();
        var heute = Start.AddDays(1);

        var frei = VorgangsLogik.Entfernen(s[1], s, heute);

        Assert.Equal(AufgabenStatus.Entfallen, s[1].Status);
        Assert.Null(s[2].WartetAuf);
        Assert.Equal([s[2]], frei);
        Assert.Equal(heute.AddDays(14), s[2].Faellig);
    }

    [Fact]
    public void StatusNeuSolangeNiemandBegonnenHat()
    {
        var s = Erzeugen();
        Assert.Equal(VorgangsStatus.Neu, VorgangsLogik.Status(s, Start));
    }

    [Fact]
    public void StatusLaeuftSobaldEinSchrittBegonnenIst()
    {
        var s = Erzeugen();
        s[0].Status = AufgabenStatus.InArbeit;
        Assert.Equal(VorgangsStatus.Laeuft, VorgangsLogik.Status(s, Start));
    }

    [Fact]
    public void StatusHaengtBeiUeberfaelligemAktivemSchritt()
    {
        var s = Erzeugen();
        Assert.Equal(VorgangsStatus.Haengt, VorgangsLogik.Status(s, Start.AddDays(3)));
        Assert.True(VorgangsLogik.Ueberfaellig(s[1], Start.AddDays(3)));
        // Gesperrte Schritte haben keine Frist und hängen nie
        Assert.False(VorgangsLogik.Ueberfaellig(s[2], Start.AddDays(30)));
    }

    [Fact]
    public void WartetAufKundenNurWennAlleAktivenSchritteWarten()
    {
        var s = Erzeugen();
        var heute = Start.AddDays(1);
        foreach (var a in s.Where(VorgangsLogik.Aktiv).Take(2))
        {
            a.Status = AufgabenStatus.WartetAufKunden;
            a.Wiedervorlage = heute.AddDays(10);
        }
        // Rechnung (Schritt 4) ist noch intern offen → Läuft
        Assert.Equal(VorgangsStatus.Laeuft, VorgangsLogik.Status(s, heute));

        foreach (var a in s.Where(VorgangsLogik.Aktiv))
        {
            a.Status = AufgabenStatus.WartetAufKunden;
            a.Wiedervorlage = heute.AddDays(10);
        }
        Assert.Equal(VorgangsStatus.WartetAufKunden, VorgangsLogik.Status(s, heute));
    }

    [Fact]
    public void AmStichtagIstDerSchrittWiederOffenUndDanachUeberfaellig()
    {
        var s = Erzeugen();
        var a = s[1];
        a.Status = AufgabenStatus.WartetAufKunden;
        a.Wiedervorlage = Start.AddDays(10);

        Assert.True(VorgangsLogik.WartetAufKunden(a, Start.AddDays(9)));
        Assert.False(VorgangsLogik.Ueberfaellig(a, Start.AddDays(9)));     // Frist ist egal, solange gewartet wird
        Assert.False(VorgangsLogik.WartetAufKunden(a, Start.AddDays(10)));
        Assert.False(VorgangsLogik.Ueberfaellig(a, Start.AddDays(10)));
        Assert.True(VorgangsLogik.Ueberfaellig(a, Start.AddDays(11)));
    }

    [Fact]
    public void AbgeschlossenWennAllesErledigtOderEntfallen()
    {
        var vorgang = new Vorgang { Titel = "x", StrangSchluessel = "vorgang:1", StrangName = "x", Schritte = Erzeugen() };
        var heute = Start.AddDays(1);
        foreach (var a in vorgang.Schritte.Where(a => a.Status != AufgabenStatus.Entfallen).OrderBy(a => a.Reihenfolge).ToList())
        {
            if (VorgangsLogik.Gesperrt(a))
                continue;
            VorgangsLogik.Erledigen(a, vorgang.Schritte, heute, Jetzt, null);
        }
        // Nach dem ersten Durchgang sind die wartenden Schritte frei
        foreach (var a in vorgang.Schritte.Where(VorgangsLogik.Aktiv).ToList())
            VorgangsLogik.Erledigen(a, vorgang.Schritte, heute, Jetzt, null);

        Assert.Equal(VorgangsStatus.Abgeschlossen, VorgangsLogik.Status(vorgang.Schritte, heute));
        Assert.True(VorgangsLogik.AbschlussAktualisieren(vorgang, heute, Jetzt));
        Assert.Equal(Jetzt, vorgang.AbgeschlossenAm);
        Assert.False(VorgangsLogik.AbschlussAktualisieren(vorgang, heute, Jetzt));

        // Wieder öffnen hebt den Abschluss auf
        vorgang.Schritte[0].Status = AufgabenStatus.Offen;
        Assert.False(VorgangsLogik.AbschlussAktualisieren(vorgang, heute, Jetzt));
        Assert.Null(vorgang.AbgeschlossenAm);
    }

    [Fact]
    public void BedingungenInSchrittreihenfolgeOhneDoppelte()
    {
        var schritte = NeueBestellung();
        schritte.Add(new VorgangstypSchritt { Nr = 7, Titel = "Nochmal", BereichId = 1, Bedingung = "schulung bestellt" });
        schritte.Add(new VorgangstypSchritt { Nr = 8, Titel = "Nachtrag", BereichId = 1, Bedingung = "Nachtrag nötig" });

        Assert.Equal(["Schulung bestellt", "Nachtrag nötig"], VorgangsLogik.Bedingungen(schritte));
    }

    [Theory]
    [InlineData("", "Namen")]
    [InlineData("Neue Bestellung", null)]
    public void VorlagePruefenName(string name, string? fehlerTeil)
    {
        var fehler = VorgangsLogik.VorlagePruefen(name, NeueBestellung());
        if (fehlerTeil is null)
            Assert.Null(fehler);
        else
            Assert.Contains(fehlerTeil, fehler);
    }

    [Fact]
    public void VorlagePruefenVerbietetWartenAufSpaetereSchritteUndLeereVorlagen()
    {
        var schritte = NeueBestellung();
        schritte[1].WartetAufNr = 3;
        Assert.Contains("davor", VorgangsLogik.VorlagePruefen("X", schritte));

        Assert.Contains("mindestens einen", VorgangsLogik.VorlagePruefen("X", []));

        var ohneBereich = NeueBestellung();
        ohneBereich[0].BereichId = 0;
        Assert.Contains("Bereich", VorgangsLogik.VorlagePruefen("X", ohneBereich));

        var frist = NeueBestellung();
        frist[0].FristTage = 400;
        Assert.Contains("Frist", VorgangsLogik.VorlagePruefen("X", frist));
    }

    [Fact]
    public void StandardtypenSindGueltig()
    {
        Assert.Equal(4, VorgangstypStandard.Alle.Count);
        foreach (var t in VorgangstypStandard.Alle)
        {
            var schritte = t.Schritte.Select((s, i) => new VorgangstypSchritt
            {
                Nr = i + 1, Titel = s.Titel, BereichId = 1, FristTage = s.FristTage, WartetAufNr = s.WartetAufNr,
                Bedingung = s.Bedingung, Pflichtfeld = s.Pflichtfeld,
            }).ToList();
            Assert.Null(VorgangsLogik.VorlagePruefen(t.Name, schritte));
        }
    }
}
