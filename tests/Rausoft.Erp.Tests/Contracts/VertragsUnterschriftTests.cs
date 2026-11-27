using Rausoft.Erp.Domain.Contracts;

namespace Rausoft.Erp.Tests.Contracts;

/// <summary>
/// Wann gilt ein Vertrag als unterschrieben vorliegend? An dieser Regel hängt, ob ein Schritt
/// wie „World-Check-Daten freischalten“ freigegeben wird — deshalb zählt nur beides zusammen:
/// erfasste Unterschrift und abgelegte Datei, die nicht der erzeugte Entwurf ist. Erfundene Daten.
/// </summary>
public class VertragsUnterschriftTests
{
    private static Vertragsdokument Datei(int version, bool ausVorlage) => new()
    {
        Version = version, Dateipfad = @"X:\ablage\vertrag.pdf", DateinameOriginal = "vertrag.pdf", Sha256 = "00",
        VorlagenVersionId = ausVorlage ? 7 : null,
    };

    private static Vertrag Vertrag(params Vertragsbestandteil[] bestandteile) =>
        new() { Vertragsnummer = "V-2026-0001", Bestandteile = [.. bestandteile] };

    private static Vertragsbestandteil Teil(BestandteilTyp typ, DateOnly? unterzeichnet, params Vertragsdokument[] dateien) =>
        new() { Typ = typ, Titel = "Vertrag", UnterzeichnetAm = unterzeichnet, Dokumente = [.. dateien] };

    private static readonly DateOnly Tag = new(2026, 11, 20);

    [Fact]
    public void UnterschriftUndAbgelegteDateiZusammenReichen()
    {
        var v = Vertrag(Teil(BestandteilTyp.Lizenzvertrag, Tag, Datei(1, ausVorlage: true), Datei(2, ausVorlage: false)));

        Assert.Equal(Tag, VertragsUnterschrift.LiegtVorSeit(v));
        Assert.Null(VertragsUnterschrift.WasFehlt(v));
    }

    [Fact]
    public void DatumAlleinReichtNicht()
    {
        // Unterschrift eingetragen, aber nur der aus der Vorlage erzeugte Entwurf liegt in der Akte
        var v = Vertrag(Teil(BestandteilTyp.Lizenzvertrag, Tag, Datei(1, ausVorlage: true)));

        Assert.Null(VertragsUnterschrift.LiegtVorSeit(v));
        Assert.Contains("noch nicht abgelegt", VertragsUnterschrift.WasFehlt(v));
    }

    [Fact]
    public void DateiAlleinReichtNicht()
    {
        var v = Vertrag(Teil(BestandteilTyp.Lizenzvertrag, null, Datei(1, ausVorlage: false)));

        Assert.Null(VertragsUnterschrift.LiegtVorSeit(v));
        Assert.Contains("Unterschrift ist nicht erfasst", VertragsUnterschrift.WasFehlt(v));
    }

    [Fact]
    public void DatumUndDateiMuessenAmSelbenBestandteilStehen()
    {
        var v = Vertrag(
            Teil(BestandteilTyp.Lizenzvertrag, Tag, Datei(1, ausVorlage: true)),
            Teil(BestandteilTyp.Avv, null, Datei(1, ausVorlage: false)));

        Assert.Null(VertragsUnterschrift.LiegtVorSeit(v));
    }

    [Theory]
    [InlineData(BestandteilTyp.Bestellung)]
    [InlineData(BestandteilTyp.Installationsprotokoll)]
    [InlineData(BestandteilTyp.Kuendigung)]
    public void BestellungProtokollUndKuendigungSindKeinUnterschriebenerVertrag(BestandteilTyp typ)
    {
        var v = Vertrag(Teil(typ, Tag, Datei(1, ausVorlage: false)));

        Assert.Null(VertragsUnterschrift.LiegtVorSeit(v));
        Assert.NotNull(VertragsUnterschrift.WasFehlt(v));
    }

    [Fact]
    public void LeererVertragNenntBeideSchritte()
    {
        var fehlt = VertragsUnterschrift.WasFehlt(Vertrag());

        Assert.Contains("Unterzeichnet am", fehlt);
        Assert.Contains("Version hinzufügen", fehlt);
    }

    [Fact]
    public void BeiMehrerenUnterschriebenenTeilenGiltDerFruehesteTag()
    {
        var v = Vertrag(
            Teil(BestandteilTyp.Avv, Tag.AddDays(5), Datei(1, ausVorlage: false)),
            Teil(BestandteilTyp.Lizenzvertrag, Tag, Datei(1, ausVorlage: false)));

        Assert.Equal(Tag, VertragsUnterschrift.LiegtVorSeit(v));
    }
}
