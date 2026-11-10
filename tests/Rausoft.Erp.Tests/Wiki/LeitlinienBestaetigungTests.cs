using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Domain.Wiki;

namespace Rausoft.Erp.Tests.Wiki;

/// <summary>
/// Kreis der Bestätigungspflichtigen: Lesebestätigung ist Pflicht für verbotene
/// Aussagen und Pflichthinweise; betroffen sind die Rollen der Zielgruppen
/// (Vertrieb, Support) — die Zielgruppe KI betrifft keinen Mitarbeiter. Die
/// Neuerungen-Mechanik kennt dafür Zielrollen (leer = alle, wie bisher).
/// </summary>
public class LeitlinienBestaetigungTests
{
    [Theory]
    [InlineData(SprachregelArt.Verboten, true)]
    [InlineData(SprachregelArt.Pflichthinweis, true)]
    [InlineData(SprachregelArt.Verwenden, false)]
    [InlineData(SprachregelArt.Vermeiden, false)]
    [InlineData(SprachregelArt.Grundsatz, false)]
    public void Bestaetigungspflicht_NurVerbotenUndPflichthinweis(SprachregelArt art, bool erwartet) =>
        Assert.Equal(erwartet, SprachregelRegeln.IstBestaetigungspflichtig(art));

    [Fact]
    public void BetroffeneRollen_AusZielgruppen()
    {
        Assert.Equal(MitarbeiterRolle.Vertrieb, SprachregelRegeln.BetroffeneRollen(SprachregelZielgruppe.Vertrieb));
        Assert.Equal(MitarbeiterRolle.Support, SprachregelRegeln.BetroffeneRollen(SprachregelZielgruppe.Support));
        Assert.Equal(MitarbeiterRolle.Vertrieb | MitarbeiterRolle.Support,
            SprachregelRegeln.BetroffeneRollen(SprachregelZielgruppe.Vertrieb | SprachregelZielgruppe.Support | SprachregelZielgruppe.KI));
        Assert.Equal(MitarbeiterRolle.Vertrieb,
            SprachregelRegeln.BetroffeneRollen(SprachregelZielgruppe.Vertrieb | SprachregelZielgruppe.KI));
    }

    [Fact]
    public void BetroffeneRollen_NurKi_BetrifftKeinenMitarbeiter() =>
        Assert.Equal(MitarbeiterRolle.Keine, SprachregelRegeln.BetroffeneRollen(SprachregelZielgruppe.KI));

    [Fact]
    public void Betrifft_OhneZielrollenAlle_SonstNurDieRollen()
    {
        // Bisheriges Verhalten (Preisliste, Sanktionslisten): keine Zielrollen = alle
        Assert.True(WikiNeuerung.Betrifft(MitarbeiterRolle.Keine, MitarbeiterRolle.Keine));
        Assert.True(WikiNeuerung.Betrifft(MitarbeiterRolle.Keine, MitarbeiterRolle.Buchhaltung));

        Assert.True(WikiNeuerung.Betrifft(MitarbeiterRolle.Vertrieb, MitarbeiterRolle.Vertrieb));
        Assert.True(WikiNeuerung.Betrifft(MitarbeiterRolle.Vertrieb | MitarbeiterRolle.Support, MitarbeiterRolle.Support | MitarbeiterRolle.Admin));
        Assert.False(WikiNeuerung.Betrifft(MitarbeiterRolle.Vertrieb, MitarbeiterRolle.Support));
        Assert.False(WikiNeuerung.Betrifft(MitarbeiterRolle.Vertrieb, MitarbeiterRolle.Keine));
        // Admin und Compliance geben frei, müssen aber nicht bestätigen, wenn sie die Rolle nicht tragen
        Assert.False(WikiNeuerung.Betrifft(MitarbeiterRolle.Vertrieb | MitarbeiterRolle.Support, MitarbeiterRolle.Admin | MitarbeiterRolle.Compliance));
    }

    [Fact]
    public void OffeneBestaetigung_FilterFuerNavigationUndSeiten()
    {
        var fuerVertrieb = Neuerung(pflicht: true, MitarbeiterRolle.Vertrieb);
        var fuerAlle = Neuerung(pflicht: true, MitarbeiterRolle.Keine);
        var nurInfo = Neuerung(pflicht: false, MitarbeiterRolle.Vertrieb);
        var bestaetigt = Neuerung(pflicht: true, MitarbeiterRolle.Vertrieb,
            new WikiNeuerungLesung { MitarbeiterId = 7, GesehenAm = DateTime.UtcNow, BestaetigtAm = DateTime.UtcNow });
        var nurGesehen = Neuerung(pflicht: true, MitarbeiterRolle.Vertrieb,
            new WikiNeuerungLesung { MitarbeiterId = 7, GesehenAm = DateTime.UtcNow });
        var vonAnderemBestaetigt = Neuerung(pflicht: true, MitarbeiterRolle.Vertrieb,
            new WikiNeuerungLesung { MitarbeiterId = 8, GesehenAm = DateTime.UtcNow, BestaetigtAm = DateTime.UtcNow });

        var vertrieb = WikiNeuerung.OffeneBestaetigung(7, MitarbeiterRolle.Vertrieb).Compile();
        Assert.True(vertrieb(fuerVertrieb));
        Assert.True(vertrieb(fuerAlle));
        Assert.False(vertrieb(nurInfo));
        Assert.False(vertrieb(bestaetigt));
        Assert.True(vertrieb(nurGesehen));
        Assert.True(vertrieb(vonAnderemBestaetigt));

        var buchhaltung = WikiNeuerung.OffeneBestaetigung(7, MitarbeiterRolle.Buchhaltung).Compile();
        Assert.False(buchhaltung(fuerVertrieb));
        Assert.True(buchhaltung(fuerAlle));
    }

    [Fact]
    public void NeuerungZurFreigabe_TitelAusArtUndAussage_TextMitBegruendung()
    {
        var regel = new Sprachregel
        {
            Art = SprachregelArt.Verboten,
            Aussage = "Zusage rechtlicher\nSicherheit",
            Thema = "recht",
            Stattdessen = "beschreiben, was die Prüfung leistet",
            Begruendung = "Die Verantwortung bleibt beim Kunden.",
            GueltigAb = new DateOnly(2026, 12, 1),
        };

        var (titel, text) = SprachregelNeuerung.Freigabe(regel, ersetztVorgaenger: false);
        Assert.Equal("Niemals sagen: Zusage rechtlicher Sicherheit", titel);
        Assert.Contains("**Niemals sagen:** Zusage rechtlicher\nSicherheit", text);
        Assert.Contains("**Stattdessen:** beschreiben, was die Prüfung leistet", text);
        Assert.Contains("Die Verantwortung bleibt beim Kunden.", text);
        Assert.Contains("Gilt ab 01.12.2026.", text);
        Assert.DoesNotContain("Neufassung", text);

        var (_, neufassung) = SprachregelNeuerung.Freigabe(regel, ersetztVorgaenger: true);
        Assert.StartsWith("*Neufassung", neufassung);
    }

    [Fact]
    public void NeuerungZurFreigabe_PflichthinweisMitAnlass_UndInfoBeiNeufassung()
    {
        var hinweis = new Sprachregel { Art = SprachregelArt.Pflichthinweis, Aussage = "Hinweistext", Anlass = "bei jedem Angebot", Thema = "recht" };
        var (titel, text) = SprachregelNeuerung.Freigabe(hinweis, ersetztVorgaenger: false);
        Assert.Equal("Pflichthinweis: Hinweistext", titel);
        Assert.Contains("**Anlass:** bei jedem Angebot", text);

        // Nicht bestätigungspflichtige Arten bekommen nur bei einer Neufassung eine (Info-)Neuerung
        var grundsatz = new Sprachregel { Art = SprachregelArt.Grundsatz, Aussage = "Wir siezen.", Thema = "ton" };
        Assert.Equal("Neu gefasst · Grundsatz: Wir siezen.", SprachregelNeuerung.Freigabe(grundsatz, ersetztVorgaenger: true).Titel);
    }

    [Fact]
    public void Neuerung_TitelHoechstens200Zeichen_RuecknahmeNenntDieRegel()
    {
        var lang = new Sprachregel { Art = SprachregelArt.Verboten, Aussage = new string('x', 480), Thema = "recht", Begruendung = "Grund" };
        var titel = SprachregelNeuerung.Freigabe(lang, false).Titel;
        Assert.Equal(200, titel.Length);
        Assert.EndsWith("…", titel);

        var regel = new Sprachregel { Art = SprachregelArt.Vermeiden, Aussage = "garantiert", Thema = "produktleistung" };
        var (ruecktitel, text) = SprachregelNeuerung.Ruecknahme(regel);
        Assert.Equal("Gilt nicht mehr: garantiert", ruecktitel);
        Assert.Contains("„garantiert“", text);
        Assert.Contains("gilt nicht mehr", text);
    }

    private static WikiNeuerung Neuerung(bool pflicht, MitarbeiterRolle zielrollen, params WikiNeuerungLesung[] lesungen) => new()
    {
        Thema = WikiThema.Leitlinien,
        Titel = "Titel",
        Text = "Text",
        BestaetigungErforderlich = pflicht,
        Zielrollen = zielrollen,
        Lesungen = [.. lesungen],
    };
}
