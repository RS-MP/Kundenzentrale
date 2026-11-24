using Rausoft.Erp.Domain.Customers;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Tests.Customers;

/// <summary>Regeln für das Anlegen einer Website-Anfrage in Odoo — alle Daten sind erfunden.</summary>
public class OdooAnlageRegelnTests
{
    private const string TestDb = "firma-test1.odoo.example/firma-test1";
    private const string ProdDb = "firma.odoo.example/firma";
    private static readonly DateTime Jetzt = new(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc);

    private static Anfrage Anfrage() => new()
    {
        Id = 7,
        Formular = "Infomappe",
        Firma = "Muster Logistik GmbH",
        Vorname = "Erika",
        Nachname = "Muster",
        Email = "erika.muster@muster-logistik.example",
        Telefon = "+49 30 1234567",
        EingegangenAm = Jetzt.AddHours(-2),
        ErfasstAm = Jetzt,
    };

    private static AnfrageOdooAnlage Anlage(OdooSchreibZiel ziel, string datenbank, OdooAnlageStand stand) => new()
    {
        AnfrageId = 7, Ziel = ziel, Datenbank = datenbank, Stand = stand, BegonnenAm = Jetzt.AddDays(-1),
        AbgeschlossenAm = stand == OdooAnlageStand.Fertig ? Jetzt.AddDays(-1) : null, LeadId = 4711,
    };

    // ---- Sperre: nichts doppelt, Test- und Produktiv-Datenbank getrennt ----

    [Fact]
    public void Kennung_IstRechnerUndDatenbank_KleinGeschrieben()
    {
        Assert.Equal(TestDb, OdooAnlageRegeln.Kennung("https://Firma-Test1.odoo.example/odoo", " Firma-Test1 "));
        Assert.NotEqual(OdooAnlageRegeln.Kennung("https://firma.odoo.example", "firma"),
            OdooAnlageRegeln.Kennung("https://firma.odoo.example", "firma-kopie"));
    }

    [Fact]
    public void Sperre_NeueAnfrage_DarfAngelegtWerden() =>
        Assert.Null(OdooAnlageRegeln.Sperre(Anfrage(), [], TestDb, Jetzt));

    [Fact]
    public void Sperre_InDieserDatenbankFertig_NichtNochEinmal()
    {
        var sperre = OdooAnlageRegeln.Sperre(Anfrage(), [Anlage(OdooSchreibZiel.Test, TestDb, OdooAnlageStand.Fertig)], TestDb, Jetzt);
        Assert.Contains("Testdatenbank angelegt", sperre);
        Assert.Contains("4711", sperre);
    }

    [Fact]
    public void Sperre_InAltererTestdatenbankFertig_SperrtDieNeueNicht()
    {
        // Nach 14 Tagen gibt es eine neue Testdatenbank; auch der Wechsel auf „Produktiv“ bleibt möglich
        var anlagen = new[] { Anlage(OdooSchreibZiel.Test, TestDb, OdooAnlageStand.Fertig) };
        Assert.Null(OdooAnlageRegeln.Sperre(Anfrage(), anlagen, "firma-test2.odoo.example/firma-test2", Jetzt));
        Assert.Null(OdooAnlageRegeln.Sperre(Anfrage(), anlagen, ProdDb, Jetzt));
    }

    [Fact]
    public void Sperre_ProduktivFertig_IstEndgueltig_AuchFuerDieTestdatenbank()
    {
        var anlagen = new[] { Anlage(OdooSchreibZiel.Produktiv, ProdDb, OdooAnlageStand.Fertig) };
        Assert.Contains("in Odoo angelegt", OdooAnlageRegeln.Sperre(Anfrage(), anlagen, ProdDb, Jetzt));
        Assert.Contains("in Odoo angelegt", OdooAnlageRegeln.Sperre(Anfrage(), anlagen, TestDb, Jetzt));
    }

    [Fact]
    public void Sperre_UnvollstaendigerLauf_LaesstSichFortsetzen()
    {
        var anlage = Anlage(OdooSchreibZiel.Test, TestDb, OdooAnlageStand.Unvollstaendig);
        Assert.Null(OdooAnlageRegeln.Sperre(Anfrage(), [anlage], TestDb, Jetzt));
    }

    [Fact]
    public void Sperre_LaufenderLauf_SperrtFuerKurzeZeit()
    {
        var anlage = Anlage(OdooSchreibZiel.Test, TestDb, OdooAnlageStand.Unvollstaendig);
        anlage.LaeuftSeit = Jetzt.AddMinutes(-1);
        Assert.Contains("gerade", OdooAnlageRegeln.Sperre(Anfrage(), [anlage], TestDb, Jetzt));
        // Ein hängengebliebener Lauf sperrt nicht für immer
        anlage.LaeuftSeit = Jetzt.AddMinutes(-OdooAnlageRegeln.LaufSperreMinuten - 1);
        Assert.Null(OdooAnlageRegeln.Sperre(Anfrage(), [anlage], TestDb, Jetzt));
    }

    [Fact]
    public void Sperre_AusOdooUebernommeneAnfrage_GibtEsDortSchon()
    {
        var anfrage = Anfrage();
        anfrage.OdooLeadId = 815;
        Assert.Contains("815", OdooAnlageRegeln.Sperre(anfrage, [], ProdDb, Jetzt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("kein-at-zeichen")]
    [InlineData("erika@localhost")]
    [InlineData("100%@muster.example")] // „%“ wäre in Odoos Suche ein Platzhalter
    public void Sperre_OhneBrauchbareEmail_KeinAnlegen(string? email)
    {
        var anfrage = Anfrage();
        anfrage.Email = email;
        Assert.Contains("E-Mail-Adresse", OdooAnlageRegeln.Sperre(anfrage, [], TestDb, Jetzt));
    }

    [Fact]
    public void Email_WirdKleinGeschrieben_UndFuerDieSucheMaskiert()
    {
        Assert.Equal("erika_muster@muster.example", OdooAnlageRegeln.EmailFuerOdoo(" Erika_Muster@Muster.example "));
        Assert.Equal(@"erika\_muster@muster.example", OdooAnlageRegeln.IlikeMaske("erika_muster@muster.example"));
        Assert.Equal(@"100\%", OdooAnlageRegeln.IlikeMaske("100%"));
    }

    // ---- Person und Anrede ----

    [Fact]
    public void Person_EindeutigerVorname_AnredeAlsVorschlag_NameFeldIstNachname()
    {
        var p = OdooPersonenRegeln.Aus("Erika", "Muster", "erika.muster@muster.example");
        Assert.Equal(Anrede.Frau, p.AnredeVorschlag);
        Assert.Equal(("Erika", "Muster", "Muster", "Erika Muster"), (p.Vorname, p.Nachname, p.NameFeld, p.Anzeigename));
        Assert.Empty(p.Hinweise);
        Assert.Equal(Anrede.Herr, OdooPersonenRegeln.Aus("Jürgen", "Beispiel", null).AnredeVorschlag);
    }

    [Theory]
    [InlineData("Andrea")] // in Italien männlich
    [InlineData("Kim")]
    [InlineData("Xylophon")] // unbekannt
    public void Person_UnklarerVorname_KeinVorschlag_AberEinHinweis(string vorname)
    {
        var p = OdooPersonenRegeln.Aus(vorname, "Muster", null);
        Assert.Equal(Anrede.Keine, p.AnredeVorschlag);
        Assert.Contains(p.Hinweise, h => h.Contains("bitte wählen"));
    }

    [Fact]
    public void Person_TitelUndNamenszusatz_WerdenRichtigZerlegt()
    {
        // Gespeichert ist „Dr. Max von“ / „Muster“ (letztes Wort als Nachname) — hier wird neu zerlegt
        var p = OdooPersonenRegeln.Aus("Dr. Max von", "Muster", null);
        Assert.Equal(("Dr.", "Max", "von Muster"), (p.Titel, p.Vorname, p.Nachname));
        Assert.Equal("Dr. von Muster", p.NameFeld);
        Assert.Equal("Dr. Max von Muster", p.Anzeigename);
        Assert.Equal(Anrede.Herr, p.AnredeVorschlag);
    }

    [Fact]
    public void Person_SchreibweiseUndGenannteAnrede()
    {
        var p = OdooPersonenRegeln.Aus("Frau ERIKA", "muster-beispiel", null);
        Assert.Equal(("Erika", "Muster-Beispiel"), (p.Vorname, p.Nachname));
        Assert.Equal(Anrede.Frau, p.AnredeVorschlag);
        Assert.Equal("McMuster", OdooPersonenRegeln.Schreibweise("McMuster"));
        // „Nachname, Vorname“
        var gedreht = OdooPersonenRegeln.Aus("Muster,", "Erika", null);
        Assert.Equal(("Erika", "Muster"), (gedreht.Vorname, gedreht.Nachname));
    }

    [Fact]
    public void Person_OhneName_AusDerEmail_AberNichtAusSammelpostfaechern()
    {
        var p = OdooPersonenRegeln.Aus(null, null, "erika.muster@muster.example");
        Assert.Equal(("Erika", "Muster"), (p.Vorname, p.Nachname));
        Assert.Contains(p.Hinweise, h => h.Contains("E-Mail-Adresse"));

        var sammel = OdooPersonenRegeln.Aus(null, null, "info@muster.example");
        Assert.Null(sammel.Nachname);
        Assert.Equal("info@muster.example", sammel.NameFeld);
        Assert.Null(OdooPersonenRegeln.NameAusEmail("erika.muster2@muster.example"));
    }

    [Fact]
    public void Person_NurEinWort_VornameOderNachname()
    {
        Assert.Equal("Erika", OdooPersonenRegeln.Aus(null, "Erika", null).Vorname);
        Assert.Equal("Beispielmann", OdooPersonenRegeln.Aus(null, "Beispielmann", null).Nachname);
        // Der Nachname lässt sich aus der Adresse ergänzen, wenn der Vorname dazu passt
        var p = OdooPersonenRegeln.Aus(null, "Erika", "erika.muster@muster.example");
        Assert.Equal(("Erika", "Muster"), (p.Vorname, p.Nachname));
    }

    // ---- Firma: Dublettensuche ----

    private static OdooFirma Firma(int id, string name, string? email = null, string? website = null, bool istFirma = true, int kinder = 1) =>
        new(id, name, email, website, istFirma, kinder, null, null);

    [Fact]
    public void Firma_Domaene_OhneFreemailer_UndOhneUnterdomaene()
    {
        Assert.Equal("muster.example", OdooFirmenRegeln.BasisDomaene("mail.Muster.example"));
        Assert.Equal("firma.co.uk", OdooFirmenRegeln.BasisDomaene("post.firma.co.uk"));
        Assert.Equal("muster.example", OdooFirmenRegeln.FirmenDomaene("erika@mail.muster.example"));
        Assert.Null(OdooFirmenRegeln.FirmenDomaene("erika.muster@gmail.com"));
    }

    [Fact]
    public void Firma_NameOhneRechtsform_UndSuchwort()
    {
        Assert.Equal(OdooFirmenRegeln.Streng("Muster & Söhne GmbH"), OdooFirmenRegeln.Streng("MUSTER und Sohne"));
        Assert.Equal(OdooFirmenRegeln.Locker("Muster Logistik Holding GmbH"), OdooFirmenRegeln.Locker("Muster Logistik"));
        Assert.Equal("Logistik", OdooFirmenRegeln.SuchWort("Muster Logistik GmbH"));
        Assert.Null(OdooFirmenRegeln.SuchWort("AG"));
    }

    [Fact]
    public void Firma_Bewertung_DomaeneUndNameSindSicher_AehnlichesNurKandidat()
    {
        var bewertet = OdooFirmenRegeln.Bewerten(
            [
                Firma(10, "Muster Logistik GmbH", website: "https://www.muster-logistik.example"),
                Firma(11, "Muster Logistik Holding AG"),
                Firma(12, "Logistikzentrum Beispielstadt"),
                Firma(13, "Anders GmbH"),
                Firma(14, "Erika Logistik", istFirma: false, kinder: 0), // einzelner Kontakt, kein Firmenname
            ],
            "Muster Logistik GmbH", "erika.muster@muster-logistik.example", null, new HashSet<int>());

        Assert.Equal([10, 11, 12, 13, 14], bewertet.Select(b => b.Firma.Id));
        Assert.Equal(85, bewertet[0].Punkte); // Domäne (80) und Name: +5
        Assert.Equal(40, bewertet[1].Punkte);
        Assert.Equal(10, bewertet[2].Punkte);
        Assert.Equal(5, bewertet[3].Punkte);
        Assert.Equal(0, bewertet[4].Punkte);
        Assert.Equal(10, OdooFirmenRegeln.Vorschlag(bewertet)!.Firma.Id);
        Assert.Equal([10, 11, 12], OdooFirmenRegeln.Kandidaten(bewertet).Select(b => b.Firma.Id));
    }

    [Fact]
    public void Firma_KeinSichererTreffer_KeinVorschlag_UndFirmaDesKontaktsGewinnt()
    {
        var nurAehnlich = OdooFirmenRegeln.Bewerten([Firma(11, "Muster Logistik Holding AG")],
            "Muster Logistik GmbH", "erika@gmail.com", null, new HashSet<int>());
        Assert.Null(OdooFirmenRegeln.Vorschlag(nurAehnlich));

        var ueberKontakt = OdooFirmenRegeln.Bewerten([Firma(20, "Ganz anderer Name KG"), Firma(21, "Muster Logistik GmbH")],
            "Muster Logistik GmbH", "erika@gmail.com", kontaktFirmaId: 20, new HashSet<int>());
        Assert.Equal(20, OdooFirmenRegeln.Vorschlag(ueberKontakt)!.Firma.Id);
    }

    [Fact]
    public void Firma_MehrereGleichGute_AeltesteMitHinweis()
    {
        var bewertet = OdooFirmenRegeln.Bewerten([Firma(31, "Muster Logistik GmbH"), Firma(30, "Muster Logistik")],
            "Muster Logistik GmbH", null, null, new HashSet<int>());
        Assert.Equal(30, OdooFirmenRegeln.Vorschlag(bewertet)!.Firma.Id);
        Assert.Contains("Dubletten", OdooFirmenRegeln.MehrfachHinweis(bewertet));
    }

    // ---- Verkaufschance: was übertragen wird und was nicht ----

    [Fact]
    public void KlickKennungen_GehenNichtNachOdoo()
    {
        var a = Anfrage();
        (a.Gclid, a.Gbraid, a.Wbraid, a.Msclkid) = ("KLICK-G", "KLICK-GB", "KLICK-WB", "KLICK-MS");
        (a.KampagnenNr, a.UeberAnzeige, a.Suchbegriff) = ("123456", true, "sanktionslistenprüfung");
        var person = OdooPersonenRegeln.Aus(a.Vorname, a.Nachname, a.Email);

        var felder = OdooLeadRegeln.TextFelder(a, person);
        Assert.DoesNotContain(felder.Keys, k => k.Contains("clid") || k.Contains("braid"));
        var alles = string.Join('\n', felder.Values.Concat(OdooLeadRegeln.Beschreibung(a, new Dictionary<string, string>())));
        Assert.DoesNotContain("KLICK-", alles);

        // Lesbare Herkunft geht mit — Anzeigen ohne eigene Angabe wie im bisherigen Workflow
        Assert.Equal("google", felder["x_studio_utm_source"]);
        Assert.Equal("cpc", felder["x_studio_utm_medium"]);
        Assert.Equal("google_ads_123456", felder["x_studio_utm_campaign"]);
        Assert.Equal("123456", felder["x_studio_gad_campaignid"]);
        Assert.Equal("Infomappe", felder["x_studio_form_source"]);
        Assert.Equal("Erika", felder["x_studio_vorname"]);
    }

    [Fact]
    public void Herkunft_OrganischeSuche_AusDemVerweis()
    {
        var a = Anfrage();
        a.Verweis = "https://www.google.com/";
        Assert.Equal(("google", "organic", "organic_search"),
            (OdooLeadRegeln.UtmQuelle(a), OdooLeadRegeln.UtmMedium(a), OdooLeadRegeln.UtmKampagne(a)));
        a.Verweis = "https://beispiel-portal.example/artikel";
        Assert.Null(OdooLeadRegeln.UtmQuelle(a));
    }

    [Theory]
    [InlineData("Infomappe", "Infomappe Erstkontakt")]
    [InlineData("Testversion", "Testversion geladen")]
    [InlineData("Checkliste", "Checkliste Erstkontakt")]
    [InlineData("Kontakt", "Kontaktanfrage beantworten")]
    [InlineData("Demo", "Website-Anfrage prüfen (Demo)")]
    public void Aktivitaet_TextJeFormular(string formular, string erwartet) =>
        Assert.Equal(erwartet, OdooLeadRegeln.AktivitaetsText(formular));

    [Fact]
    public void Titel_FirmaSonstPerson()
    {
        var a = Anfrage();
        var person = OdooPersonenRegeln.Aus(a.Vorname, a.Nachname, a.Email);
        Assert.Equal("Infomappe - Muster Logistik GmbH", OdooLeadRegeln.Titel(a, person));
        a.Firma = null;
        Assert.Equal("Infomappe - Erika Muster", OdooLeadRegeln.Titel(a, person));
    }

    [Fact]
    public void Passend_FindetOdooEintraegeAmNamen_OhneFesteNummern()
    {
        (int, string)[] phasen = [(1, "Testversion/Checkliste"), (2, "Infomappe"), (3, "Angebot")];
        Assert.Equal(2, OdooLeadRegeln.Passend(phasen, "infomappe"));
        Assert.Equal(1, OdooLeadRegeln.Passend(phasen, "checkliste"));
        Assert.Null(OdooLeadRegeln.Passend(phasen, "kontakt"));

        (int, string)[] quellen = [(1, "Suchmaschine"), (5, "X"), (6, "LinkedIn"), (7, "Kino-Werbung")];
        var a = Anfrage();
        a.Quelle = "Google";
        Assert.Equal(1, OdooLeadRegeln.Passend(quellen, OdooLeadRegeln.QuelleStichwoerter(a)));
        a.Quelle = "twitter";
        Assert.Equal(5, OdooLeadRegeln.Passend(quellen, OdooLeadRegeln.QuelleStichwoerter(a)));
        // „ki“ passt nur als ganzes Wort, nicht als Teil von „Kino“
        a.Quelle = "chatgpt";
        Assert.Null(OdooLeadRegeln.Passend(quellen, OdooLeadRegeln.QuelleStichwoerter(a)));

        (int, string)[] medien = [(1, "Website"), (4, "E-Mail"), (10, "Google Adwords")];
        a.UeberAnzeige = true;
        Assert.Equal(10, OdooLeadRegeln.Passend(medien, OdooLeadRegeln.MediumStichwoerter(a)));
        a.UeberAnzeige = false;
        (a.Quelle, a.Medium) = ("newsletter", "email");
        Assert.Equal(4, OdooLeadRegeln.Passend(medien, OdooLeadRegeln.MediumStichwoerter(a)));
    }

    [Fact]
    public void Html_MaskiertAllesAusDemFormular()
    {
        var html = OdooLeadRegeln.Html("<script>alert(1)</script>\nZeile \"2\" & 'mehr' für Ärger");
        Assert.Equal("&lt;script&gt;alert(1)&lt;/script&gt;<br>Zeile &quot;2&quot; &amp; &#39;mehr&#39; für Ärger", html);
    }

    [Fact]
    public void ErsterBesuch_ImFormatDesOdooFelds()
    {
        var a = Anfrage();
        a.ErsterBesuch = new DateTime(2026, 11, 20, 8, 5, 9, DateTimeKind.Utc);
        Assert.Equal("2026-11-20 08:05:09", OdooLeadRegeln.ErsterBesuch(a, "datetime"));
        Assert.Equal("2026-11-20T08:05:09Z", OdooLeadRegeln.ErsterBesuch(a, "char"));
        a.ErsterBesuch = null;
        Assert.Null(OdooLeadRegeln.ErsterBesuch(a, "char"));
    }

    // ---- Vorbelegung: gemerkt werden Namen, keine Odoo-Nummern ----

    [Fact]
    public void Vorbelegung_MerktNamen_UndFindetSieInDerAktuellenListe()
    {
        var wert = OdooVorbelegung.Merken("", [new(OdooVorbelegung.Zuweisung, "Dana Beispiel"),
            new(OdooVorbelegung.Phase("Infomappe"), "Infomappe"), new(OdooVorbelegung.Aktivitaet("Infomappe"), "Infomappe; versenden")], 1000);
        var gelesen = OdooVorbelegung.Lesen(wert);
        Assert.Equal("Dana Beispiel", gelesen[OdooVorbelegung.Zuweisung]);
        Assert.Equal(6, OdooVorbelegung.Finden(gelesen, OdooVorbelegung.Zuweisung, [(2, "Mika Muster"), (6, "dana beispiel")]));
        // Sonderzeichen im Namen stören das Format nicht
        Assert.Equal(15, OdooVorbelegung.Finden(gelesen, OdooVorbelegung.Aktivitaet("Infomappe"), [(15, "Infomappe; versenden")]));
        // Gibt es den Namen in dieser Datenbank nicht (mehr), gibt es keine Vorbelegung
        Assert.Null(OdooVorbelegung.Finden(gelesen, OdooVorbelegung.Zuweisung, [(2, "Mika Muster")]));

        // Neue Wahl ersetzt die alte; „nichts gewählt“ entfernt den Eintrag
        var neu = OdooVorbelegung.Lesen(OdooVorbelegung.Merken(wert,
            [new(OdooVorbelegung.Zuweisung, "Mika Muster"), new(OdooVorbelegung.Phase("Infomappe"), null)], 1000));
        Assert.Equal("Mika Muster", neu[OdooVorbelegung.Zuweisung]);
        Assert.False(neu.ContainsKey(OdooVorbelegung.Phase("Infomappe")));
    }

    [Fact]
    public void Vorbelegung_BleibtInDerSpaltenlaenge()
    {
        var wert = "";
        for (var i = 0; i < 60; i++)
            wert = OdooVorbelegung.Merken(wert, [new($"Quelle:kanal-{i}", new string('x', 30))], 1000);
        Assert.True(wert.Length <= 1000);
        // Das zuletzt Gemerkte bleibt, das Älteste fällt heraus
        var gelesen = OdooVorbelegung.Lesen(wert);
        Assert.True(gelesen.ContainsKey("Quelle:kanal-59"));
        Assert.False(gelesen.ContainsKey("Quelle:kanal-0"));
    }
}
