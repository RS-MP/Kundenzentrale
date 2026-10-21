using Rausoft.Erp.Domain.Servers;

namespace Rausoft.Erp.Tests.Servers;

/// <summary>
/// Erinnerungslogik der SSL-Zertifikate: ab dem Vorlauf täglich erinnern,
/// bis bestätigt wird; Statusampel; Vorschlag für das neue Ablaufdatum.
/// </summary>
public class ZertifikatErinnerungTests
{
    private static readonly DateTime Jetzt = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Heute = DateOnly.FromDateTime(Jetzt);

    [Fact]
    public void VorDemVorlaufKeineErinnerung()
    {
        var gueltigBis = Heute.AddDays(30);
        Assert.False(ZertifikatErinnerung.ErinnerungFaellig(gueltigBis, 14, null, 1, Jetzt));
        Assert.Equal("ok", ZertifikatErinnerung.Status(gueltigBis, 14, Heute));
    }

    [Fact]
    public void AbDemVorlaufSofortFaellig()
    {
        var gueltigBis = Heute.AddDays(14);
        Assert.True(ZertifikatErinnerung.ErinnerungFaellig(gueltigBis, 14, null, 1, Jetzt));
        Assert.Equal("bald", ZertifikatErinnerung.Status(gueltigBis, 14, Heute));
        Assert.Equal(14, ZertifikatErinnerung.TageBisAblauf(gueltigBis, Heute));
    }

    [Fact]
    public void WiederholungErstNachIntervall()
    {
        var gueltigBis = Heute.AddDays(5);
        Assert.False(ZertifikatErinnerung.ErinnerungFaellig(gueltigBis, 14, Jetzt.AddHours(-20), 1, Jetzt));
        Assert.True(ZertifikatErinnerung.ErinnerungFaellig(gueltigBis, 14, Jetzt.AddHours(-25), 1, Jetzt));
        // Intervall 3 Tage: nach 2 Tagen noch nicht
        Assert.False(ZertifikatErinnerung.ErinnerungFaellig(gueltigBis, 14, Jetzt.AddDays(-2), 3, Jetzt));
        Assert.True(ZertifikatErinnerung.ErinnerungFaellig(gueltigBis, 14, Jetzt.AddDays(-3), 3, Jetzt));
    }

    [Fact]
    public void AbgelaufenBleibtFaelligBisBestaetigt()
    {
        var gueltigBis = Heute.AddDays(-10);
        Assert.Equal("abgelaufen", ZertifikatErinnerung.Status(gueltigBis, 14, Heute));
        Assert.True(ZertifikatErinnerung.ErinnerungFaellig(gueltigBis, 14, Jetzt.AddDays(-1), 1, Jetzt));

        var z = new Zertifikat { Bezeichnung = "x", GueltigBis = gueltigBis, LetzteErinnerungAm = Jetzt.AddDays(-1), ErinnerungenGesendet = 9 };
        z.Erneuern(Heute.AddMonths(6), 1, Jetzt);
        Assert.False(ZertifikatErinnerung.ErinnerungFaellig(z.GueltigBis, z.VorlaufTage, z.LetzteErinnerungAm, 1, Jetzt));
        Assert.Equal(0, z.ErinnerungenGesendet);
        Assert.Null(z.LetzteErinnerungAm);
        Assert.Equal(Jetzt, z.ErneuertAm);
    }

    [Fact]
    public void VorschlagNimmtLiveDatumWennNeuerSonstLaufzeit()
    {
        var bisher = Heute.AddDays(10);
        Assert.Equal(bisher.AddMonths(6), ZertifikatErinnerung.VorschlagNeuesGueltigBis(bisher, 6, null, Heute));
        Assert.Equal(bisher.AddMonths(6), ZertifikatErinnerung.VorschlagNeuesGueltigBis(bisher, 6, bisher.AddDays(-1), Heute));
        var live = Heute.AddMonths(5);
        Assert.Equal(live, ZertifikatErinnerung.VorschlagNeuesGueltigBis(bisher, 6, live, Heute));
        // Abgelaufen: ab heute rechnen
        Assert.Equal(Heute.AddMonths(6), ZertifikatErinnerung.VorschlagNeuesGueltigBis(Heute.AddDays(-30), 6, null, Heute));
    }

    [Fact]
    public void EmpfaengerlisteTrenntUndBereinigt()
    {
        var liste = ZertifikatErinnerung.Empfaenger(" a@rausoft.de; b@rausoft.de ,a@rausoft.de\nkeine-adresse ");
        Assert.Equal(["a@rausoft.de", "b@rausoft.de"], liste);
        Assert.Empty(ZertifikatErinnerung.Empfaenger(null));
    }
}
