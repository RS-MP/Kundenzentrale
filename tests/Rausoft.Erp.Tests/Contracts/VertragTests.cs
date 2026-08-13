using Rausoft.Erp.Domain.Contracts;

namespace Rausoft.Erp.Tests.Contracts;

public class VertragTests
{
    private static Vertrag Beispielvertrag() => new()
    {
        Vertragsnummer = "V00001",
        Beginn = new DateOnly(2026, 1, 1),
        MindestlaufzeitMonate = 12,
        VerlaengerungMonate = 12,
        KuendigungsfristMonate = 3,
    };

    [Fact]
    public void Vertragsende_InnerhalbDerFrist_EndeDerMindestlaufzeit()
    {
        var vertrag = Beispielvertrag();
        // Stichtag 30.09.2026: Kündigungsfrist (3 Monate) zum 01.01.2027 noch einhaltbar.
        var ende = vertrag.NaechstesVertragsende(new DateOnly(2026, 9, 30));
        Assert.Equal(new DateOnly(2027, 1, 1), ende);
    }

    [Fact]
    public void Vertragsende_FristVerpasst_VerlaengertUmPeriode()
    {
        var vertrag = Beispielvertrag();
        // Stichtag 15.11.2026: Frist zum 01.01.2027 verpasst → Verlängerung bis 01.01.2028.
        var ende = vertrag.NaechstesVertragsende(new DateOnly(2026, 11, 15));
        Assert.Equal(new DateOnly(2028, 1, 1), ende);
    }

    [Fact]
    public void Vertragsende_OhneVerlaengerung_BleibtBeimLaufzeitende()
    {
        var vertrag = Beispielvertrag();
        vertrag.VerlaengerungMonate = null;
        var ende = vertrag.NaechstesVertragsende(new DateOnly(2026, 12, 15));
        Assert.Equal(new DateOnly(2027, 1, 1), ende);
    }

    [Fact]
    public void Vertragsende_Gekuendigt_LiefertKuendigungsdatum()
    {
        var vertrag = Beispielvertrag();
        vertrag.GekuendigtZum = new DateOnly(2027, 1, 1);
        var ende = vertrag.NaechstesVertragsende(new DateOnly(2026, 6, 1));
        Assert.Equal(new DateOnly(2027, 1, 1), ende);
    }

    [Fact]
    public void Vertragsende_OhneLaufzeitangaben_Null()
    {
        var vertrag = new Vertrag { Vertragsnummer = "V00002", Beginn = new DateOnly(2026, 1, 1) };
        Assert.Null(vertrag.NaechstesVertragsende(new DateOnly(2026, 6, 1)));
    }
}
