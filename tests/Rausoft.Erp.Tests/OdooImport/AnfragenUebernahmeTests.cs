using Rausoft.Erp.Domain.Customers;
using Rausoft.Erp.OdooImport;

namespace Rausoft.Erp.Tests.OdooImport;

public class AnfragenUebernahmeTests
{
    private static readonly DateTime Jetzt = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Verkaufschance mit erfundenen Angaben; Standard: zehn Tage alt, offen.</summary>
    private static AnfrageLead Lead(
        int id = 1, DateTime? angelegt = null, string? name = "Infomappe - Muster AG", bool aktiv = true,
        string? formular = "Infomappe", string? firma = "Muster AG", string? kontakt = "Erika Beispiel",
        string? vorname = null, string? email = "erika@muster.example", string? telefon = null,
        string? anliegen = null, string? anliegenDetail = null, bool datenschutz = true,
        string? seitenUrl = "https://www.idprove.de/service/infomappe/?gclid=Abc-123",
        Dictionary<string, string>? herkunft = null, bool stufeGewonnen = false, string? wonStatus = null,
        string? verlustGrund = null) =>
        new(id, angelegt ?? Jetzt.AddDays(-10), name, aktiv, formular, firma, kontakt, vorname, email, telefon,
            anliegen, anliegenDetail, datenschutz, seitenUrl, herkunft ?? [], stufeGewonnen, wonStatus, verlustGrund);

    [Theory]
    [InlineData("Infomappe", null, "Infomappe")]
    [InlineData("Infomappe_LP1", null, "Infomappe")]
    [InlineData("Testversion 3.0", null, "Testversion")]
    [InlineData("Contact From", null, "Kontakt")]
    [InlineData("Checkliste", null, "Checkliste")]
    [InlineData("Unbekanntes Formular", "Angebot Lizenzerweiterung", WebsiteAnfrageLeser.FormularUnbekannt)]
    [InlineData("Unbekanntes Formular", "Testversion - Muster AG", "Testversion")]
    [InlineData("Webinar Herbst", null, "Webinar Herbst")]
    [InlineData(null, "INFOMAPPE Beispiel GmbH", "Infomappe")]
    [InlineData(null, "Angebot Lizenzerweiterung", WebsiteAnfrageLeser.FormularUnbekannt)]
    [InlineData(null, null, WebsiteAnfrageLeser.FormularUnbekannt)]
    public void FormularName_FuehrtVariantenZusammen(string? feld, string? leadName, string erwartet) =>
        Assert.Equal(erwartet, AnfragenUebernahme.FormularName(feld, leadName));

    [Theory]
    [InlineData(true, false, null, AnfrageErgebnis.Offen)]
    [InlineData(false, false, null, AnfrageErgebnis.Verloren)]
    [InlineData(true, true, null, AnfrageErgebnis.Gewonnen)]
    // Das Feld won_status neuerer Odoo-Versionen hat Vorrang
    [InlineData(true, false, "lost", AnfrageErgebnis.Verloren)]
    [InlineData(false, false, "won", AnfrageErgebnis.Gewonnen)]
    [InlineData(false, false, "pending", AnfrageErgebnis.Verloren)]
    public void Ergebnis_AusStufeArchivierungUndWonStatus(bool aktiv, bool stufeGewonnen, string? wonStatus, AnfrageErgebnis erwartet) =>
        Assert.Equal(erwartet, AnfragenUebernahme.Ergebnis(Lead(aktiv: aktiv, stufeGewonnen: stufeGewonnen, wonStatus: wonStatus)));

    [Theory]
    [InlineData("erika@muster.example", "erika@muster.example")]
    [InlineData("\"Erika Beispiel\" <erika@muster.example>", "erika@muster.example")]
    [InlineData("erika@muster.example, zweite@muster.example", "erika@muster.example")]
    [InlineData("keine adresse", null)]
    [InlineData("erika@lokal", null)]
    [InlineData(null, null)]
    public void EmailAdresse_NimmtDieErsteGueltige(string? roh, string? erwartet) =>
        Assert.Equal(erwartet, AnfragenUebernahme.EmailAdresse(roh));

    [Theory]
    [InlineData("Erika", "Erika Beispiel", "Erika", "Beispiel")]
    [InlineData("Erika", "Beispiel", "Erika", "Beispiel")]
    [InlineData("Erika", "Erika", "Erika", null)]
    [InlineData(null, "Erika Maria Beispiel", "Erika Maria", "Beispiel")]
    [InlineData(null, "Beispiel", null, "Beispiel")]
    [InlineData("Erika", null, "Erika", null)]
    public void Namen_AusVornameUndKontakt(string? vorname, string? kontakt, string? erwVorname, string? erwNachname) =>
        Assert.Equal((erwVorname, erwNachname), AnfragenUebernahme.Namen(vorname, kontakt));

    [Fact]
    public void Sauber_EntferntSteuerzeichen_UndBegrenztDieLaenge()
    {
        Assert.Equal("Zeile eins Zeile zwei", AnfragenUebernahme.Sauber(" Zeile eins\r\nZeile zwei​ "));
        Assert.Null(AnfragenUebernahme.Sauber(" \t\n"));
        Assert.Equal(WebsiteAnfrageLeser.MaxFeldLaenge, AnfragenUebernahme.Sauber(new string('x', 2000))!.Length);
    }

    [Fact]
    public void IstWebsiteAnfrage_BrauchtFormularSeiteOderHerkunft()
    {
        Assert.True(AnfragenUebernahme.IstWebsiteAnfrage(Lead()));
        Assert.True(AnfragenUebernahme.IstWebsiteAnfrage(Lead(formular: null, seitenUrl: null,
            herkunft: new() { ["utm_source"] = "google" })));
        // Nur der Name nennt ein Formular — das reicht nicht
        Assert.False(AnfragenUebernahme.IstWebsiteAnfrage(Lead(formular: null, seitenUrl: null)));
    }

    [Theory]
    [InlineData("doc_referrer", "https://tagassistant.google.com/", true)]
    [InlineData("last_page_before_form", "https://www.idprove.de/service/infomappe/?gtm_debug=1763000000000", true)]
    [InlineData("doc_referrer", "https://www.google.com/", false)]
    [InlineData("utm_source", "google", false)]
    public void IstTestEintrag_ErkenntDenGoogleTagAssistant(string feld, string wert, bool erwartet) =>
        Assert.Equal(erwartet, AnfragenUebernahme.IstTestEintrag(Lead(seitenUrl: null, herkunft: new() { [feld] = wert })));

    [Fact]
    public void IstTestEintrag_AuchUeberDieSeiteDesFormulars() =>
        Assert.True(AnfragenUebernahme.IstTestEintrag(Lead(seitenUrl: "https://www.idprove.de/kontakt/?gtm_debug=1763000000000")));

    [Fact]
    public void Bericht_NenntAusgelassene_UndZaehltSieNichtBeiDenKanaelen()
    {
        UebernahmeZeile[] zeilen = [new(AnfragenUebernahme.Aus(Lead(), Jetzt), UebernahmeArt.Neu)];

        var bericht = AnfragenUebernahme.Bericht(zeilen, 342, [], probelauf: true, new DateTime(2026, 11, 24, 15, 0, 0),
            testEintraege: 5, geloeschte: 2);

        Assert.Contains("davon Website-Anfragen: 8", bericht);
        Assert.Matches(@"neu anlegen\s+1\b", bericht);
        Assert.Matches(@"ausgelassen: Test über Google Tag Assistant\s+5\b", bericht);
        Assert.Matches(@"ausgelassen: in der Kundenzentrale gelöscht\s+2\b", bericht);
        Assert.Matches(@"Summe\s+1\s+1\s+0\s+0\b", bericht);

        // Nur Ausgelassenes: der Bericht endet nach der Zählung
        var nurAusgelassen = AnfragenUebernahme.Bericht([], 342, [], probelauf: false, new DateTime(2026, 11, 24), testEintraege: 5);
        Assert.Matches(@"ausgelassen: Test über Google Tag Assistant\s+5\b", nurAusgelassen);
        Assert.DoesNotContain("Kanal und Ausgang", nurAusgelassen);
    }

    [Fact]
    public void Aus_UebernimmtHerkunftWieBeimEinfuegen_UndDenAusgang()
    {
        var angelegt = new DateTime(2026, 11, 14, 14, 43, 0, DateTimeKind.Utc);
        var a = AnfragenUebernahme.Aus(Lead(
            id: 4711, angelegt: angelegt, aktiv: false, formular: "Infomappe_LP1", vorname: "Erika",
            email: "Erika Beispiel <erika@muster.example>", telefon: "0151 0000000",
            anliegen: "Beratung", anliegenDetail: "Bitte um Rückruf", verlustGrund: "Kein Bedarf",
            herkunft: new()
            {
                ["gclid"] = "Abc-123",
                ["utm_source"] = "google",
                ["utm_medium"] = "cpc",
                ["utm_campaign"] = "google_ads_24257565344",
                ["doc_referrer"] = "https://www.google.com/",
                ["landing_page"] = "https://www.idprove.de/?gclid=Abc-123",
                ["last_page_before_form"] = "https://www.idprove.de/service/infomappe/?_gl=1*6b&gclid=Abc-123",
                ["first_touch_at"] = "2026-11-14 14:40:00",
            }), Jetzt);

        Assert.Equal(4711, a.OdooLeadId);
        Assert.Equal(angelegt, a.EingegangenAm);
        Assert.Equal(Jetzt, a.ErfasstAm);
        Assert.Null(a.ErfasstVonId);
        Assert.Equal("Infomappe", a.Formular);
        Assert.Equal("idprove.de", a.Website);
        Assert.Equal("Muster AG", a.Firma);
        Assert.Equal("Erika", a.Vorname);
        Assert.Equal("Beispiel", a.Nachname);
        Assert.Equal("erika@muster.example", a.Email);
        Assert.Equal("0151 0000000", a.Telefon);
        Assert.Equal("Beratung: Bitte um Rückruf", a.Nachricht);
        Assert.True(a.DatenschutzBestaetigt);

        Assert.Equal("Google Ads", AnfrageHerkunft.Kanal(a));
        Assert.Equal("24257565344", a.KampagnenNr);
        Assert.Null(a.Kampagne);
        // Adressen ohne Abfrageteil — sonst stünde die Klick-Kennung noch einmal darin
        Assert.Equal("https://www.idprove.de/", a.Einstiegsseite);
        Assert.Equal("https://www.idprove.de/service/infomappe/", a.LetzteSeite);
        Assert.Equal(new DateTime(2026, 11, 14, 14, 40, 0, DateTimeKind.Utc), a.ErsterBesuch);
        // Zehn Tage alt: die Klick-Kennung kommt mit und läuft über den normalen Löschlauf ab
        Assert.Equal("Abc-123", a.Gclid);

        Assert.Equal(AnfrageErgebnis.Verloren, a.Ergebnis);
        Assert.Equal("Kein Bedarf", a.VerlustGrund);
    }

    [Fact]
    public void Aus_AlteAnfrage_OhneKlickKennung_AberWeiterUeberAnzeige()
    {
        var a = AnfragenUebernahme.Aus(Lead(
            angelegt: Jetzt.AddDays(-AnfrageHerkunft.KlickKennungTage - 1),
            herkunft: new() { ["gclid"] = "Abc-123", ["gbraid"] = "0AAA", ["msclkid"] = "ms-1" },
            verlustGrund: "Kein Bedarf"), Jetzt);

        Assert.False(a.HatKlickKennung());
        Assert.True(a.UeberAnzeige);
        // Ein Verlustgrund zählt nur bei verlorenen Verkaufschancen
        Assert.Equal(AnfrageErgebnis.Offen, a.Ergebnis);
        Assert.Null(a.VerlustGrund);
    }

    [Theory]
    [InlineData("infomappe", null, null)]                          // wiederholt nur das Formular
    [InlineData("infomappe", "Bitte per Post", "Bitte per Post")]
    [InlineData("Beratung", null, "Beratung")]
    [InlineData("Beratung", "Bitte um Rückruf", "Beratung: Bitte um Rückruf")]
    [InlineData(null, null, null)]
    public void Aus_Nachricht_AusAnliegenUndDetail(string? anliegen, string? detail, string? erwartet) =>
        Assert.Equal(erwartet, AnfragenUebernahme.Aus(Lead(anliegen: anliegen, anliegenDetail: detail), Jetzt).Nachricht);

    [Fact]
    public void Aus_MicrosoftKennung_BleibtAlsKanalErkennbar()
    {
        var a = AnfragenUebernahme.Aus(Lead(herkunft: new() { ["msclkid"] = "ms-1" }), Jetzt);

        Assert.Equal("ms-1", a.Msclkid);
        Assert.True(a.HatKlickKennung());
        Assert.Equal("bing", a.Quelle);
        Assert.Equal("Microsoft Ads", AnfrageHerkunft.Kanal(a));
    }

    [Fact]
    public void PassendeHandAnfrage_GleicheAdresse_KurzVorDerVerkaufschance()
    {
        var angelegt = new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);
        Anfrage Hand(int id, string email, DateTime eingang, int? lead = null) =>
            new() { Id = id, Formular = "Infomappe", Email = email, EingegangenAm = eingang, OdooLeadId = lead };

        Anfrage[] hand =
        [
            Hand(1, "erika@muster.example", angelegt.AddDays(-30)),          // zu alt
            Hand(2, "ERIKA@muster.example", angelegt.AddDays(-2)),           // passt
            Hand(3, "erika@muster.example", angelegt.AddHours(-1), lead: 9), // schon verknüpft
            Hand(4, "andere@muster.example", angelegt.AddHours(-1)),
            Hand(5, "erika@muster.example", angelegt.AddDays(3)),            // nach der Verkaufschance
        ];

        Assert.Equal(2, AnfragenUebernahme.PassendeHandAnfrage(hand, "erika@muster.example", angelegt)?.Id);
        Assert.Null(AnfragenUebernahme.PassendeHandAnfrage(hand, "niemand@muster.example", angelegt));
        Assert.Null(AnfragenUebernahme.PassendeHandAnfrage(hand, null, angelegt));
    }

    [Fact]
    public void Bericht_ZaehltLaufKanalUndAusgang_OhnePersonen()
    {
        Anfrage Aus(int id, bool aktiv = true, bool gewonnen = false, Dictionary<string, string>? herkunft = null,
            string? grund = null, int? kunde = null)
        {
            var a = AnfragenUebernahme.Aus(Lead(id: id, aktiv: aktiv, stufeGewonnen: gewonnen, herkunft: herkunft, verlustGrund: grund), Jetzt);
            a.KundeId = kunde;
            return a;
        }
        Dictionary<string, string> anzeige = new() { ["gclid"] = "Abc-123", ["utm_source"] = "google", ["utm_medium"] = "cpc" };

        UebernahmeZeile[] zeilen =
        [
            new(Aus(1, herkunft: anzeige, kunde: 7), UebernahmeArt.Neu),
            new(Aus(2, aktiv: false, herkunft: anzeige, grund: "Kein Bedarf"), UebernahmeArt.Neu),
            new(Aus(3, gewonnen: true, kunde: 8), UebernahmeArt.Neu),
            new(Aus(4, aktiv: false), UebernahmeArt.Verknuepft),
            new(Aus(5), UebernahmeArt.Unveraendert),
        ];
        ZusatzFeld[] zusatz = [new("x_studio_channel", "Herkunftskanal", 3, "nicht übernommen (nur gezählt)", ["   2  Ads", "   1  Organisch"])];

        var bericht = AnfragenUebernahme.Bericht(zeilen, gelesen: 342, zusatz, probelauf: true, new DateTime(2026, 11, 24, 13, 0, 0));

        Assert.Contains("PROBELAUF — es wird nichts gespeichert", bericht);
        Assert.Contains("Verkaufschancen gelesen: 342 · davon Website-Anfragen: 5", bericht);
        Assert.Matches(@"neu anlegen\s+3\b", bericht);
        Assert.Matches(@"mit Hand-Anfrage verknüpfen\s+1\b", bericht);
        Assert.Matches(@"schon übernommen, unverändert\s+1\b", bericht);
        Assert.Contains("einem Kunden zugeordnet: 2 · ohne Kunde: 3", bericht);
        Assert.Contains("neue Anfragen mit Klick-Kennung (jünger als 90 Tage): 2", bericht);
        // Spalten: Kanal, gesamt, offen, gewonnen, verloren
        Assert.Matches(@"Google Ads\s+2\s+1\s+0\s+1\b", bericht);
        Assert.Matches(@"Direkt\s+3\s+1\s+1\s+1\b", bericht);
        Assert.Matches(@"Summe\s+5\s+2\s+1\s+2\b", bericht);
        Assert.Matches(@"\s1\s+Kein Bedarf", bericht);
        Assert.Matches(@"\s1\s+\(kein Grund gepflegt\)", bericht);
        Assert.Contains("x_studio_channel", bericht);
        Assert.Contains("2  Ads", bericht);
        // Der Bericht nennt keine Personen
        Assert.DoesNotContain("Erika", bericht);
        Assert.DoesNotContain("muster.example", bericht);
        Assert.DoesNotContain("Muster AG", bericht);

        var echt = AnfragenUebernahme.Bericht(zeilen, 342, [], probelauf: false, new DateTime(2026, 11, 24, 13, 0, 0));
        Assert.DoesNotContain("PROBELAUF", echt);
        Assert.Matches(@"neu angelegt\s+3\b", echt);
    }

    [Fact]
    public void Bericht_OhneTreffer_BleibtLesbar() =>
        Assert.Contains("Keine Verkaufschance mit Formular oder Herkunft gefunden.",
            AnfragenUebernahme.Bericht([], 12, [], probelauf: true, new DateTime(2026, 11, 24)));
}
