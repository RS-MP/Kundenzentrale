using System.Reflection;
using Rausoft.Erp.Domain.Margen;
using Rausoft.Erp.Domain.Shared;
using Dto = Rausoft.Erp.Dtos.Margen;

namespace Rausoft.Erp.Tests.Margen;

/// <summary>
/// Der Margenrechner zeigt Einkaufspreise und Margen — Zugang hat nur, wer
/// die Rolle „Margenrechner" einzeln bekommen hat. Keine andere Rolle
/// schließt sie ein, auch Admin nicht.
/// </summary>
public class MargenrechnerZugangTests
{
    [Theory]
    [InlineData(MitarbeiterRolle.Keine)]
    [InlineData(MitarbeiterRolle.Admin)]
    [InlineData(MitarbeiterRolle.Vertrieb)]
    [InlineData(MitarbeiterRolle.Compliance)]
    [InlineData(MitarbeiterRolle.Buchhaltung)]
    [InlineData(MitarbeiterRolle.VertraulicheVertraege)]
    [InlineData(MitarbeiterRolle.WikiRedaktion)]
    [InlineData(MitarbeiterRolle.Admin | MitarbeiterRolle.Vertrieb | MitarbeiterRolle.Compliance)]
    public void OhneDieRolle_KeinZugang(MitarbeiterRolle rollen) =>
        Assert.False(MargenrechnerZugang.Darf(rollen));

    [Fact]
    public void AlleAnderenRollenZusammen_ReichenNicht()
    {
        var alleAusser = Enum.GetValues<MitarbeiterRolle>()
            .Where(r => r != MitarbeiterRolle.Margenrechner)
            .Aggregate(MitarbeiterRolle.Keine, (summe, r) => summe | r);
        Assert.False(MargenrechnerZugang.Darf(alleAusser));
    }

    [Theory]
    [InlineData(MitarbeiterRolle.Margenrechner)]
    [InlineData(MitarbeiterRolle.Margenrechner | MitarbeiterRolle.Admin)]
    [InlineData(MitarbeiterRolle.Margenrechner | MitarbeiterRolle.Vertrieb)]
    public void MitDerRolle_Zugang(MitarbeiterRolle rollen) =>
        Assert.True(MargenrechnerZugang.Darf(rollen));

    [Fact]
    public void Rolle_UeberschneidetSichMitKeinerAnderen()
    {
        var andere = Enum.GetValues<MitarbeiterRolle>().Where(r => r != MitarbeiterRolle.Margenrechner);
        Assert.All(andere, r => Assert.Equal(MitarbeiterRolle.Keine, r & MitarbeiterRolle.Margenrechner));
    }
}

/// <summary>
/// Das Frontend liest die Antworten des Rechners über gespiegelte Records
/// (Rausoft.Erp.Dtos.Margen). Laufen Domäne und Spiegel auseinander — Feld
/// vergessen, umbenannt, anderer Typ, andere Reihenfolge —, schlägt dieser
/// Test an, bevor im Browser Werte fehlen.
/// </summary>
public class MargenDtoTests
{
    [Theory]
    [InlineData(typeof(MargenErgebnis), typeof(Dto.MargenErgebnis))]
    [InlineData(typeof(MargenEingabe), typeof(Dto.MargenEingabe))]
    [InlineData(typeof(MargenDaten), typeof(Dto.MargenDaten))]
    [InlineData(typeof(MargenrechnerStart), typeof(Dto.MargenrechnerStart))]
    [InlineData(typeof(MargenDatenAnsicht), typeof(Dto.MargenDatenAnsicht))]
    [InlineData(typeof(MargenrechnerZugangInfo), typeof(Dto.MargenrechnerZugangInfo))]
    public void SpiegelStimmtMitDerDomaeneUeberein(Type domaene, Type spiegel) =>
        Vergleiche(domaene, spiegel, domaene.Name, []);

    private static void Vergleiche(Type domaene, Type spiegel, string pfad, HashSet<Type> besucht)
    {
        // Nullable<T>
        var dN = Nullable.GetUnderlyingType(domaene);
        var sN = Nullable.GetUnderlyingType(spiegel);
        Assert.True(dN is null == sN is null, $"{pfad}: nur eine Seite ist nullable");
        if (dN is not null)
        {
            Vergleiche(dN, sN!, pfad, besucht);
            return;
        }

        // Listen (List<T>, IReadOnlyList<T>)
        var dE = Element(domaene);
        var sE = Element(spiegel);
        Assert.True(dE is null == sE is null, $"{pfad}: nur eine Seite ist eine Liste");
        if (dE is not null)
        {
            Vergleiche(dE, sE!, pfad + "[]", besucht);
            return;
        }

        if (domaene.IsEnum)
        {
            Assert.True(spiegel.IsEnum, $"{pfad}: Spiegel ist kein Enum");
            Assert.Equal(Enum.GetNames(domaene), Enum.GetNames(spiegel));
            Assert.Equal(Enum.GetValues(domaene).Cast<object>().Select(Convert.ToInt32),
                Enum.GetValues(spiegel).Cast<object>().Select(Convert.ToInt32));
            return;
        }

        if (domaene.IsPrimitive || domaene == typeof(string) || domaene == typeof(DateTime))
        {
            Assert.True(domaene == spiegel, $"{pfad}: {domaene.Name} gegenüber {spiegel.Name}");
            return;
        }

        if (!besucht.Add(domaene))
            return;
        var dP = domaene.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract").ToList();
        var sP = spiegel.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract").ToList();
        Assert.True(dP.Select(p => p.Name).SequenceEqual(sP.Select(p => p.Name)),
            $"{pfad}: Felder {string.Join(", ", dP.Select(p => p.Name))} gegenüber {string.Join(", ", sP.Select(p => p.Name))}");
        for (var i = 0; i < dP.Count; i++)
            Vergleiche(dP[i].PropertyType, sP[i].PropertyType, pfad + "." + dP[i].Name, besucht);
    }

    private static Type? Element(Type typ) =>
        typ.IsGenericType && typ.GetGenericTypeDefinition() is var g
            && (g == typeof(List<>) || g == typeof(IReadOnlyList<>))
            ? typ.GetGenericArguments()[0]
            : null;
}
