using Rausoft.Erp.Domain.Contracts;
using Rausoft.Erp.OdooImport;

namespace Rausoft.Erp.Tests.Contracts;

/// <summary>
/// Verträge zusammenfassen (11/2026): Regeln für Hauptvertrag und Anhänge,
/// Bezeichnung und der Schutz der Handpflege vor einem erneuten Bestandsscan.
/// </summary>
public class VertragsGruppeTests
{
    private static Vertrag Kundenvertrag(int id, int kundeId = 1, int? hauptvertragId = null) => new()
    {
        Id = id,
        Vertragsnummer = $"V{id:D5}",
        KundeId = kundeId,
        HauptvertragId = hauptvertragId,
        Beginn = new DateOnly(2026, 1, 1),
    };

    private static Vertrag Partnervertrag(int id, int partnerId) => new()
    {
        Id = id,
        Vertragsnummer = $"V{id:D5}",
        PartnerId = partnerId,
        Beginn = new DateOnly(2026, 1, 1),
    };

    [Fact]
    public void Zuordnung_GleicherKunde_Erlaubt()
    {
        Assert.Null(VertragsGruppe.ZuordnungsFehler(Kundenvertrag(2), Kundenvertrag(1), anhangHatAnhaenge: false));
    }

    [Fact]
    public void Zuordnung_AnSichSelbst_Abgelehnt()
    {
        var vertrag = Kundenvertrag(1);
        var fehler = VertragsGruppe.ZuordnungsFehler(vertrag, vertrag, anhangHatAnhaenge: false);
        Assert.Contains("eigener Anhang", fehler);
    }

    [Fact]
    public void Zuordnung_AndererKunde_Abgelehnt()
    {
        var fehler = VertragsGruppe.ZuordnungsFehler(
            Kundenvertrag(2, kundeId: 7), Kundenvertrag(1, kundeId: 8), anhangHatAnhaenge: false);
        Assert.Contains("selben Kunden", fehler);
    }

    [Fact]
    public void Zuordnung_KundenvertragAnPartnervertrag_Abgelehnt()
    {
        // Gleiche Zahl als Kunden- und Partner-Id darf nicht als „dieselbe Partei" durchgehen
        var fehler = VertragsGruppe.ZuordnungsFehler(
            Kundenvertrag(2, kundeId: 5), Partnervertrag(1, partnerId: 5), anhangHatAnhaenge: false);
        Assert.NotNull(fehler);
    }

    [Fact]
    public void Zuordnung_GleicherPartner_Erlaubt()
    {
        Assert.Null(VertragsGruppe.ZuordnungsFehler(
            Partnervertrag(2, partnerId: 3), Partnervertrag(1, partnerId: 3), anhangHatAnhaenge: false));
    }

    [Fact]
    public void Zuordnung_HauptvertragIstSelbstAnhang_KeineKette()
    {
        // 3 → 2 → 1 wäre eine Kette: 2 ist bereits Anhang von 1
        var fehler = VertragsGruppe.ZuordnungsFehler(
            Kundenvertrag(3), Kundenvertrag(2, hauptvertragId: 1), anhangHatAnhaenge: false);
        Assert.Contains("selbst ein Anhang", fehler);
    }

    [Fact]
    public void Zuordnung_AnhangHatEigeneAnhaenge_KeineKette()
    {
        // 1 hat Anhänge und soll selbst Anhang von 9 werden
        var fehler = VertragsGruppe.ZuordnungsFehler(Kundenvertrag(1), Kundenvertrag(9), anhangHatAnhaenge: true);
        Assert.Contains("hat selbst Anhänge", fehler);
    }

    [Fact]
    public void Zuordnung_Kreis_Abgelehnt()
    {
        // 2 ist Anhang von 1. Umgekehrt (1 als Anhang von 2) ergäbe einen Kreis —
        // beide Kettenregeln greifen: 2 ist selbst Anhang, 1 hat einen Anhang.
        var haupt = Kundenvertrag(1);
        var anhang = Kundenvertrag(2, hauptvertragId: 1);
        Assert.NotNull(VertragsGruppe.ZuordnungsFehler(haupt, anhang, anhangHatAnhaenge: true));
        Assert.NotNull(VertragsGruppe.ZuordnungsFehler(haupt, anhang, anhangHatAnhaenge: false));
    }

    [Fact]
    public void Zuordnung_Umhaengen_AnAnderenHauptvertrag_Erlaubt()
    {
        // 2 hängt an 1 und soll an 3 — erlaubt, solange 2 selbst keine Anhänge hat
        Assert.Null(VertragsGruppe.ZuordnungsFehler(
            Kundenvertrag(2, hauptvertragId: 1), Kundenvertrag(3), anhangHatAnhaenge: false));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  Wartungsvertrag  ID.prove ", "Wartungsvertrag ID.prove")]
    [InlineData("Zeile 1\r\nZeile 2", "Zeile 1 Zeile 2")]
    public void Bezeichnung_WirdBereinigt(string? eingabe, string? erwartet)
    {
        Assert.Equal(erwartet, VertragsGruppe.BezeichnungBereinigen(eingabe));
    }

    [Fact]
    public void Bezeichnung_Ueberlaenge_WirdGekuerzt()
    {
        var bereinigt = VertragsGruppe.BezeichnungBereinigen(new string('x', 500));
        Assert.Equal(VertragsGruppe.BezeichnungMaxLaenge, bereinigt!.Length);
    }

    [Fact]
    public void Vertraulich_NurWennAlleBestandteileVertraulichSind()
    {
        Assert.False(VertragsGruppe.IstVertraulich([]));             // leere Akte
        Assert.False(VertragsGruppe.IstVertraulich([false, false]));
        Assert.False(VertragsGruppe.IstVertraulich([true, false]));  // gemischt: Vertrag bleibt für alle pflegbar
        Assert.True(VertragsGruppe.IstVertraulich([true]));
        Assert.True(VertragsGruppe.IstVertraulich([true, true]));
    }

    // --- Dokumente innerhalb einer Akte: Anlage zu einem anderen Bestandteil ----

    private static Vertragsbestandteil Dokument(int id, int vertragId = 1, int? gehoertZuId = null) => new()
    {
        Id = id,
        VertragId = vertragId,
        Typ = BestandteilTyp.Nachtrag,
        Titel = $"Dokument {id}",
        GehoertZuId = gehoertZuId,
    };

    [Fact]
    public void Anlage_GleicheAkte_Erlaubt()
    {
        Assert.Null(VertragsGruppe.AnlageFehler(Dokument(2), Dokument(1), anlageHatAnlagen: false));
    }

    [Fact]
    public void Anlage_AnSichSelbst_Abgelehnt()
    {
        var dokument = Dokument(1);
        Assert.Contains("eigene Anlage", VertragsGruppe.AnlageFehler(dokument, dokument, anlageHatAnlagen: false));
    }

    [Fact]
    public void Anlage_AndereAkte_Abgelehnt()
    {
        var fehler = VertragsGruppe.AnlageFehler(
            Dokument(2, vertragId: 7), Dokument(1, vertragId: 8), anlageHatAnlagen: false);
        Assert.Contains("selben Vertragsakte", fehler);
    }

    [Fact]
    public void Anlage_HauptdokumentIstSelbstAnlage_KeineKette()
    {
        var fehler = VertragsGruppe.AnlageFehler(Dokument(3), Dokument(2, gehoertZuId: 1), anlageHatAnlagen: false);
        Assert.Contains("selbst eine Anlage", fehler);
    }

    [Fact]
    public void Anlage_HatEigeneAnlagen_KeineKette()
    {
        var fehler = VertragsGruppe.AnlageFehler(Dokument(1), Dokument(9), anlageHatAnlagen: true);
        Assert.Contains("hat selbst Anlagen", fehler);
    }

    [Fact]
    public void Anlage_Kreis_Abgelehnt()
    {
        // 2 ist Anlage zu 1 — umgekehrt ergäbe einen Kreis
        var haupt = Dokument(1);
        var anlage = Dokument(2, gehoertZuId: 1);
        Assert.NotNull(VertragsGruppe.AnlageFehler(haupt, anlage, anlageHatAnlagen: true));
        Assert.NotNull(VertragsGruppe.AnlageFehler(haupt, anlage, anlageHatAnlagen: false));
    }

    [Fact]
    public void Anlage_Umhaengen_AnAnderesHauptdokument_Erlaubt()
    {
        Assert.Null(VertragsGruppe.AnlageFehler(Dokument(2, gehoertZuId: 1), Dokument(3), anlageHatAnlagen: false));
    }

    // --- Handpflege übersteht den erneuten Bestandsscan ------------------------

    [Fact]
    public void Bestandsscan_UmbenannterBestandteil_BehaeltSeinenTitel()
    {
        var vertrag = Kundenvertrag(1);
        vertrag.Bezeichnung = "Wartungsvertrag ID.prove";
        vertrag.HauptvertragId = 9;
        // Beim ersten Scan als „4228-1" angelegt, danach von Hand umbenannt
        var umbenannt = new Vertragsbestandteil
        {
            Id = 10, Typ = BestandteilTyp.Nachtrag, Titel = "Preisanpassung 2024", Vertrag = vertrag,
            GehoertZuId = 5, // von Hand als Anlage zugeordnet
        };
        vertrag.Bestandteile.Add(umbenannt);

        // Erneuter Scan findet eine weitere Datei „4228-2"
        var neu = VertragsBestandScanner.BestandteilFinden(vertrag, BestandteilTyp.Nachtrag, "4228-2");

        Assert.NotSame(umbenannt, neu);
        Assert.Equal("Preisanpassung 2024", umbenannt.Titel);
        Assert.Equal(5, umbenannt.GehoertZuId);
        Assert.Null(neu.GehoertZuId); // neue Dateien kommen eigenständig dazu
        Assert.Equal(2, vertrag.Bestandteile.Count);
        // Bezeichnung und Zuordnung der Akte fasst der Scan nicht an
        Assert.Equal("Wartungsvertrag ID.prove", vertrag.Bezeichnung);
        Assert.Equal(9, vertrag.HauptvertragId);
    }

    [Fact]
    public void Bestandsscan_GleicherTitel_NutztVorhandenenBestandteil()
    {
        var vertrag = Kundenvertrag(1);
        var vorhanden = new Vertragsbestandteil
        {
            Id = 10, Typ = BestandteilTyp.Wartungsvertrag, Titel = "Wartungsvertrag", Vertrag = vertrag,
        };
        vertrag.Bestandteile.Add(vorhanden);

        var gefunden = VertragsBestandScanner.BestandteilFinden(vertrag, BestandteilTyp.Wartungsvertrag, "Wartungsvertrag");

        Assert.Same(vorhanden, gefunden);
        Assert.Single(vertrag.Bestandteile);
    }
}
