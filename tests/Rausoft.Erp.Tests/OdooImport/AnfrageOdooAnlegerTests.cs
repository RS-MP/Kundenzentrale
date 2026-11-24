using Rausoft.Erp.Domain.Customers;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.OdooImport;

namespace Rausoft.Erp.Tests.OdooImport;

/// <summary>
/// Der ganze Ablauf „Anfrage in Odoo anlegen“ gegen ein nachgestelltes Odoo im Speicher —
/// mit erfundenen Daten (Domänen auf .example).
/// </summary>
public class AnfrageOdooAnlegerTests
{
    private const string Basis = "https://firma-test1.odoo.example";
    private const string Email = "erika.muster@muster-logistik.example";
    private static readonly DateOnly Heute = new(2026, 11, 24);
    private static readonly DateTime Jetzt = new(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Dictionary<string, string> KeineNamen = [];

    private static Anfrage Anfrage() => new()
    {
        Id = 7,
        Formular = "Infomappe",
        Website = "produkt.example",
        Firma = "Muster Logistik GmbH",
        Vorname = "Erika",
        Nachname = "Muster",
        Email = Email,
        Telefon = "+49 30 1234567",
        Nachricht = "Bitte um Unterlagen <b>fett</b>",
        DatenschutzBestaetigt = true,
        EingegangenAm = Jetzt.AddHours(-2),
        ErfasstAm = Jetzt,
        KampagnenNr = "123456",
        UeberAnzeige = true,
        Einstiegsseite = "https://produkt.example/infomappe",
        Gclid = "KLICK-G", Gbraid = "KLICK-GB", Wbraid = "KLICK-WB", Msclkid = "KLICK-MS",
    };

    private static AnfrageOdooAnlage Stand() =>
        new() { AnfrageId = 7, Ziel = OdooSchreibZiel.Test, Datenbank = "firma-test1.odoo.example/firma-test1", BegonnenAm = Jetzt };

    /// <summary>Wahl wie in der Vorschau vorbelegt: Vorschlag der Firma, Anrede Frau, neue Verkaufschance.</summary>
    private static OdooAnlageWahl Wahl(OdooAnlageLage lage, int? leadId = null, bool haUmstellen = false) =>
        new(lage.FirmaVorschlag, Anrede.Frau, haUmstellen, leadId, PhaseId: 2, StichwortIds: [1], BenutzerId: 2,
            AktivitaetsartId: 15, QuelleId: 1, MediumId: 10);

    private static async Task<(OdooAnlageLage Lage, List<string> Protokoll)> AnlegenAsync(
        NachgestelltesOdoo odoo, Anfrage anfrage, AnfrageOdooAnlage stand, Func<OdooAnlageLage, OdooAnlageWahl>? wahl = null)
    {
        var anleger = new AnfrageOdooAnleger(odoo, Basis);
        var lage = await anleger.LageAsync(anfrage, stand);
        var protokoll = await anleger.AusfuehrenAsync(anfrage, lage, (wahl ?? (l => Wahl(l)))(lage), stand, KeineNamen, Heute, Jetzt,
            () => Task.CompletedTask);
        return (lage, protokoll);
    }

    [Fact]
    public async Task AllesNeu_LegtFirmaKontaktVerkaufschanceUndAktivitaetAn()
    {
        var odoo = new NachgestelltesOdoo();
        var stand = Stand();

        var (lage, protokoll) = await AnlegenAsync(odoo, Anfrage(), stand);

        Assert.Null(lage.Kontakt);
        Assert.Empty(lage.Firmen);
        Assert.Equal(OdooAnlageStand.Fertig, stand.Stand);
        Assert.True(stand is { FirmaNeu: true, KontaktNeu: true, LeadNeu: true, AktivitaetErledigt: true, HauptansprechpartnerErledigt: true });
        Assert.Equal(5, protokoll.Count);

        var firma = odoo.Satz("res.partner", stand.FirmaId!.Value);
        Assert.Equal("Muster Logistik GmbH", firma["name"]);
        Assert.Equal(true, firma["is_company"]);
        Assert.Equal("https://www.muster-logistik.example", firma["website"]);
        // Die anfragende Person wird Hauptansprechpartner, weil die Firma noch keinen hat
        Assert.Equal(stand.KontaktId, firma["x_studio_hauptansprechpartner"]);

        var kontakt = odoo.Satz("res.partner", stand.KontaktId!.Value);
        Assert.Equal("Muster", kontakt["name"]); // Konvention: Feld „Name“ = Nachname
        Assert.Equal("Erika", kontakt["x_studio_vorname"]);
        Assert.Equal(2, kontakt["x_studio_anrede"]); // „Frau“ in dieser Datenbank
        Assert.Equal(Email, kontakt["email"]);
        Assert.Equal(stand.FirmaId, kontakt["parent_id"]);

        var lead = odoo.Satz("crm.lead", stand.LeadId!.Value);
        Assert.Equal("Infomappe - Muster Logistik GmbH", lead["name"]);
        Assert.Equal("opportunity", lead["type"]);
        Assert.Equal(stand.FirmaId, lead["partner_id"]);
        Assert.Equal((2, 2, 1, 10), (lead["stage_id"], lead["user_id"], lead["source_id"], lead["medium_id"]));
        Assert.Equal([1], (int[])lead["tag_ids"]!);
        Assert.Equal("Infomappe", lead["x_studio_form_source"]);
        Assert.Equal("google", lead["x_studio_utm_source"]);
        // Hängt die Verkaufschance an der Firma, stehen E-Mail und Telefon der Person nur in der Beschreibung
        Assert.False(lead.ContainsKey("email_from"));
        var beschreibung = (string)lead["description"]!;
        Assert.Contains($"Kontakt: Erika Muster, {Email}", beschreibung);
        Assert.Contains("&lt;b&gt;fett&lt;/b&gt;", beschreibung); // Formulartext ist maskiert

        var aktivitaet = odoo.Satz("mail.activity", stand.AktivitaetId!.Value);
        Assert.Equal(("crm.lead", stand.LeadId, 15, 2), (aktivitaet["res_model"], aktivitaet["res_id"], aktivitaet["activity_type_id"], aktivitaet["user_id"]));
        Assert.Equal(("Infomappe Erstkontakt", "2026-11-24"), (aktivitaet["summary"], aktivitaet["date_deadline"]));
        Assert.Contains($"{Basis}/odoo/contacts/{stand.KontaktId}", (string)aktivitaet["note"]!);
    }

    [Fact]
    public async Task KlickKennungen_StehenNirgendsInOdoo()
    {
        var odoo = new NachgestelltesOdoo();
        await AnlegenAsync(odoo, Anfrage(), Stand());

        var alles = System.Text.Json.JsonSerializer.Serialize(odoo.Tabellen);
        Assert.DoesNotContain("KLICK-", alles);
        var lead = odoo.Tabelle("crm.lead").Single();
        Assert.DoesNotContain(lead.Keys, k => k.Contains("clid") || k.Contains("braid"));
    }

    [Fact]
    public async Task VorhandeneFirmaUndKontakt_WerdenWiederverwendet_HauptansprechpartnerBleibt()
    {
        var odoo = new NachgestelltesOdoo();
        odoo.Neu("res.partner", 50, new() { ["name"] = "Muster Logistik GmbH", ["is_company"] = true, ["x_studio_hauptansprechpartner"] = 52 });
        odoo.Neu("res.partner", 51, new() { ["name"] = "erika.muster@muster-logistik.example", ["email"] = "Erika.Muster@muster-logistik.example", ["parent_id"] = 50 });
        odoo.Neu("res.partner", 52, new() { ["name"] = "Beispiel", ["email"] = "max.beispiel@muster-logistik.example", ["parent_id"] = 50, ["category_id"] = new[] { 2 } });
        var stand = Stand();

        var (lage, protokoll) = await AnlegenAsync(odoo, Anfrage(), stand);

        Assert.Equal(51, lage.Kontakt!.Id);
        Assert.Equal(50, lage.FirmaVorschlag);
        Assert.Equal((50, 51, false, false), (stand.FirmaId, stand.KontaktId, stand.FirmaNeu, stand.KontaktNeu));
        Assert.Equal(0, odoo.Anzahl("res.partner.create"));
        // Lücken am vorhandenen Kontakt gefüllt; der von Odoo aus der Mail gebildete Name wird ersetzt
        var kontakt = odoo.Satz("res.partner", 51);
        Assert.Equal(("Muster", "Erika", 2), (kontakt["name"], kontakt["x_studio_vorname"], kontakt["x_studio_anrede"]));
        // Der bisherige Hauptansprechpartner bleibt (Entscheidung MP 24.11.2026)
        Assert.Equal(52, odoo.Satz("res.partner", 50)["x_studio_hauptansprechpartner"]);
        Assert.Contains(protokoll, z => z.Contains("bleibt „Beispiel“"));
        Assert.Equal(50, odoo.Tabelle("crm.lead").Single()["partner_id"]);
    }

    [Fact]
    public async Task HauptansprechpartnerUmstellen_NurAufWunsch_AlterVerliertDasStichwort()
    {
        var odoo = new NachgestelltesOdoo();
        odoo.Neu("res.partner", 50, new() { ["name"] = "Muster Logistik GmbH", ["is_company"] = true, ["x_studio_hauptansprechpartner"] = 52 });
        odoo.Neu("res.partner", 52, new() { ["name"] = "Beispiel", ["email"] = "max.beispiel@muster-logistik.example", ["parent_id"] = 50, ["category_id"] = new[] { 2, 9 } });
        var stand = Stand();

        await AnlegenAsync(odoo, Anfrage(), stand, l => Wahl(l, haUmstellen: true));

        Assert.Equal(stand.KontaktId, odoo.Satz("res.partner", 50)["x_studio_hauptansprechpartner"]);
        Assert.Equal([9], (int[])odoo.Satz("res.partner", 52)["category_id"]!);
    }

    [Fact]
    public async Task OffeneVerkaufschance_WirdErgaenzt_StattEineZweiteAnzulegen()
    {
        var odoo = new NachgestelltesOdoo();
        odoo.Neu("res.partner", 50, new() { ["name"] = "Muster Logistik GmbH", ["is_company"] = true });
        odoo.Neu("crm.lead", 70, new() { ["name"] = "Testversion - Muster Logistik GmbH", ["partner_id"] = 50, ["stage_id"] = 1 });
        odoo.Neu("crm.lead", 71, new() { ["name"] = "Altvertrag", ["partner_id"] = 50, ["stage_id"] = 4 }); // gewonnen = nicht offen
        var stand = Stand();

        var (lage, _) = await AnlegenAsync(odoo, Anfrage(), stand, l => Wahl(l, leadId: l.OffeneLeads(50).Single().Id));

        Assert.Equal([70], lage.OffeneLeads(50).Select(l => l.Id));
        Assert.Equal((70, false), (stand.LeadId, stand.LeadNeu));
        Assert.Equal(0, odoo.Anzahl("crm.lead.create"));
        var notiz = odoo.Tabelle("mail.message").Single();
        Assert.Equal(70, notiz["res_id"]);
        Assert.Equal(true, notiz["body_is_html"]);
        Assert.Contains("Website-Anfrage (Infomappe)", (string)notiz["body"]!);
        Assert.Equal(70, odoo.Tabelle("mail.activity").Single()["res_id"]);
    }

    [Fact]
    public async Task GleicheOffeneAktivitaet_WirdNichtVerdoppelt()
    {
        var odoo = new NachgestelltesOdoo();
        odoo.Neu("res.partner", 50, new() { ["name"] = "Muster Logistik GmbH", ["is_company"] = true });
        odoo.Neu("crm.lead", 70, new() { ["name"] = "Infomappe - Muster Logistik GmbH", ["partner_id"] = 50, ["stage_id"] = 2 });
        odoo.Neu("mail.activity", 90, new() { ["res_model"] = "crm.lead", ["res_id"] = 70, ["activity_type_id"] = 15, ["summary"] = "Infomappe Erstkontakt" });
        var stand = Stand();

        var (_, protokoll) = await AnlegenAsync(odoo, Anfrage(), stand, l => Wahl(l, leadId: 70));

        Assert.Single(odoo.Tabelle("mail.activity"));
        Assert.True(stand.AktivitaetErledigt);
        Assert.Null(stand.AktivitaetId);
        Assert.Contains(protokoll, z => z.Contains("bestand bereits"));
    }

    [Fact]
    public async Task Teilfehler_IstErkennbar_UndDieWiederholungLegtNichtsDoppeltAn()
    {
        var odoo = new NachgestelltesOdoo { Fehler = aufruf => aufruf == "crm.lead.create" ? "odoo.exceptions.AccessError" : null };
        var stand = Stand();
        var gespeichert = 0;
        var anleger = new AnfrageOdooAnleger(odoo, Basis);
        var lage = await anleger.LageAsync(Anfrage(), stand);

        var fehler = await Assert.ThrowsAsync<OdooSchrittFehler>(() => anleger.AusfuehrenAsync(Anfrage(), lage, Wahl(lage), stand,
            KeineNamen, Heute, Jetzt, () => { gespeichert++; return Task.CompletedTask; }));

        // Firma und Kontakt stehen in Odoo und im Stand; die Verkaufschance fehlt
        Assert.Equal("Verkaufschance anlegen", fehler.Schritt);
        Assert.Equal("Odoo hat den Zugriff abgelehnt — dem API-Benutzer fehlt das Recht dafür.", OdooFehlerText.Fuer(fehler));
        Assert.Equal(OdooAnlageStand.Unvollstaendig, stand.Stand);
        Assert.NotNull(stand.FirmaId);
        Assert.NotNull(stand.KontaktId);
        Assert.Null(stand.LeadId);
        Assert.Equal(3, gespeichert); // nach Firma, Kontakt und Hauptansprechpartner
        var (firmaId, kontaktId) = (stand.FirmaId, stand.KontaktId);

        // Zweiter Versuch, nachdem das Recht da ist: setzt auf, ohne Firma oder Kontakt neu anzulegen
        odoo.Fehler = null;
        var (lage2, _) = await AnlegenAsync(odoo, Anfrage(), stand);

        Assert.Equal([firmaId!.Value], lage2.Firmen.Select(f => f.Firma.Id)); // die Firma steht fest
        Assert.Equal(kontaktId, lage2.Kontakt!.Id);
        Assert.Equal(2, odoo.Anzahl("res.partner.create"));
        Assert.Equal(2, odoo.Tabelle("res.partner").Count);
        Assert.Single(odoo.Tabelle("crm.lead"));
        Assert.Single(odoo.Tabelle("mail.activity"));
        Assert.Equal((firmaId, kontaktId, OdooAnlageStand.Fertig), (stand.FirmaId, stand.KontaktId, stand.Stand));
    }

    [Fact]
    public async Task Wiederholung_NachVerlorenemStand_FindetDasAngelegteInOdooWieder()
    {
        // Schlimmster Fall: Odoo hat alles angelegt, aber der Stand ging verloren (Absturz vor dem Speichern)
        var odoo = new NachgestelltesOdoo();
        await AnlegenAsync(odoo, Anfrage(), Stand());

        var neuerStand = Stand();
        var anleger = new AnfrageOdooAnleger(odoo, Basis);
        var lage = await anleger.LageAsync(Anfrage(), neuerStand);

        // Die Vorschau zeigt Firma, Kontakt und die offene Verkaufschance als vorhanden
        Assert.NotNull(lage.Kontakt);
        Assert.NotNull(lage.FirmaVorschlag);
        var offene = Assert.Single(lage.OffeneLeads(lage.FirmaVorschlag));

        await anleger.AusfuehrenAsync(Anfrage(), lage, Wahl(lage, leadId: offene.Id), neuerStand, KeineNamen, Heute, Jetzt, () => Task.CompletedTask);
        Assert.Equal(2, odoo.Tabelle("res.partner").Count);
        Assert.Single(odoo.Tabelle("crm.lead"));
        Assert.Single(odoo.Tabelle("mail.activity"));
    }

    [Fact]
    public async Task Wahl_WirdGegenOdoosListenGeprueft()
    {
        var odoo = new NachgestelltesOdoo();
        odoo.Neu("res.partner", 50, new() { ["name"] = "Muster Logistik GmbH", ["is_company"] = true });
        odoo.Neu("res.partner", 60, new() { ["name"] = "Fremde Firma AG", ["is_company"] = true });
        odoo.Neu("crm.lead", 80, new() { ["name"] = "Fremde Verkaufschance", ["partner_id"] = 60, ["stage_id"] = 1 });
        var (anfrage, stand) = (Anfrage(), Stand());
        var lage = await new AnfrageOdooAnleger(odoo, Basis).LageAsync(anfrage, stand);
        var gut = Wahl(lage);
        string? Fehler(OdooAnlageWahl wahl) => AnfrageOdooAnleger.WahlFehler(anfrage, lage, wahl, stand);

        Assert.Null(Fehler(gut));
        Assert.Null(Fehler(gut with { FirmaId = null, PhaseId = null, QuelleId = null, MediumId = null, StichwortIds = [] }));
        // Eine Firma oder Verkaufschance, die nicht zu den Treffern gehört, lässt sich nicht unterschieben
        Assert.Contains("Firma", Fehler(gut with { FirmaId = 60 }));
        Assert.Contains("Verkaufschance", Fehler(gut with { LeadId = 80 }));
        Assert.Contains("Anrede", Fehler(gut with { Anrede = null }));
        Assert.Contains("Art der Aktivität", Fehler(gut with { AktivitaetsartId = 30 })); // gehört zu einem anderen Modell
        Assert.Contains("zugewiesen", Fehler(gut with { BenutzerId = 9 })); // Portal-Nutzer
        Assert.Contains("Phase", Fehler(gut with { PhaseId = 99 }));
        Assert.Contains("Stichwort", Fehler(gut with { StichwortIds = [1, 99] }));
        Assert.Contains("Quelle", Fehler(gut with { QuelleId = 99 }));
        Assert.Contains("Medium", Fehler(gut with { MediumId = 99 }));

        // Ohne gültige Wahl wird nichts geschrieben
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AnfrageOdooAnleger(odoo, Basis).AusfuehrenAsync(
            anfrage, lage, gut with { FirmaId = 60 }, stand, KeineNamen, Heute, Jetzt, () => Task.CompletedTask));
        Assert.Equal(0, odoo.Anzahl("res.partner.create") + odoo.Anzahl("res.partner.write") + odoo.Anzahl("crm.lead.create"));
    }

    [Fact]
    public async Task DatenbankOhneEigeneFelder_SchreibtNurWasEsGibt()
    {
        var odoo = new NachgestelltesOdoo();
        foreach (var feld in odoo.Schema["crm.lead"].Keys.Where(k => k.StartsWith("x_studio_")).ToList())
            odoo.Schema["crm.lead"].Remove(feld);
        foreach (var feld in (string[])["x_studio_vorname", "x_studio_anrede", "x_studio_hauptansprechpartner"])
            odoo.Schema["res.partner"].Remove(feld);
        var stand = Stand();

        var (lage, _) = await AnlegenAsync(odoo, Anfrage(), stand);

        Assert.Empty(lage.AnredeIds);
        Assert.Contains(lage.Hinweise, h => h.Contains("Anrede"));
        Assert.Contains(lage.Hinweise, h => h.Contains("Hauptansprechpartner"));
        Assert.Equal(OdooAnlageStand.Fertig, stand.Stand);
        Assert.DoesNotContain(odoo.Tabelle("crm.lead").Single().Keys, k => k.StartsWith("x_studio_"));
        Assert.DoesNotContain(odoo.Satz("res.partner", stand.KontaktId!.Value).Keys, k => k.StartsWith("x_studio_"));
    }

    [Fact]
    public async Task NeueFirma_WirdAufWunschVonOdooAngereichert_MitPruefhinweis()
    {
        var odoo = new NachgestelltesOdoo
        {
            Anreicherung = domaene => domaene == "muster-logistik.example"
                ? new Dictionary<string, object?>
                {
                    ["name"] = "Muster Logistik Holding SE", ["vat"] = "DE000000000", ["street"] = "Beispielweg 1",
                    ["zip"] = "12345", ["city"] = "Beispielstadt", ["country_id"] = new Dictionary<string, object?> { ["id"] = 57 },
                    ["phone"] = "+49 30 000000", ["email"] = "info@muster-logistik.example", ["website"] = false,
                }
                : null,
        };
        var stand = Stand();

        await AnlegenAsync(odoo, Anfrage(), stand, l => Wahl(l) with { FirmaAnreichern = true });

        var firma = odoo.Satz("res.partner", stand.FirmaId!.Value);
        // Der Name aus dem Formular bleibt; Adresse und USt-ID kommen aus der Anreicherung
        Assert.Equal("Muster Logistik GmbH", firma["name"]);
        Assert.Equal(("DE000000000", "Beispielweg 1", "12345", "Beispielstadt", 57),
            (firma["vat"], firma["street"], firma["zip"], firma["city"], firma["country_id"]));
        Assert.Equal("info@muster-logistik.example", firma["email"]);
        Assert.Equal("https://www.muster-logistik.example", firma["website"]); // leere Angabe überschreibt nichts
        // Was Odoo eingetragen hat, steht als Prüfhinweis an Verkaufschance und Aktivität
        var beschreibung = (string)odoo.Tabelle("crm.lead").Single()["description"]!;
        Assert.Contains("angereichert", beschreibung);
        Assert.Contains("USt-ID DE000000000", beschreibung);
        Assert.Contains("Muster Logistik Holding SE", beschreibung); // abweichender Name fällt auf
        Assert.Contains("Konzernmutter", (string)odoo.Tabelle("mail.activity").Single()["note"]!);
    }

    [Fact]
    public async Task Anreicherung_NurAufWunsch_UndNieBeiVorhandenerFirmaOderFreemailer()
    {
        var odoo = new NachgestelltesOdoo();
        await AnlegenAsync(odoo, Anfrage(), Stand()); // nicht gewählt
        Assert.Equal(0, odoo.Anzahl("res.partner.enrich_by_domain"));

        var mitFirma = new NachgestelltesOdoo();
        mitFirma.Neu("res.partner", 50, new() { ["name"] = "Muster Logistik GmbH", ["is_company"] = true });
        await AnlegenAsync(mitFirma, Anfrage(), Stand(), l => Wahl(l) with { FirmaAnreichern = true });
        Assert.Equal(0, mitFirma.Anzahl("res.partner.enrich_by_domain"));

        var freemail = new NachgestelltesOdoo();
        var anfrage = Anfrage();
        anfrage.Email = "erika.muster@gmail.com";
        await AnlegenAsync(freemail, anfrage, Stand(), l => Wahl(l) with { FirmaAnreichern = true });
        Assert.Equal(0, freemail.Anzahl("res.partner.enrich_by_domain"));
    }

    [Theory]
    [InlineData(false, "kein Treffer")]
    [InlineData(true, "fehlgeschlagen")]
    public async Task Anreicherung_OhneErgebnis_HaeltDasAnlegenNichtAuf(bool fehler, string hinweis)
    {
        var odoo = new NachgestelltesOdoo();
        if (fehler)
            odoo.Fehler = aufruf => aufruf == "res.partner.enrich_by_domain" ? "odoo.exceptions.UserError" : null;
        var stand = Stand();

        await AnlegenAsync(odoo, Anfrage(), stand, l => Wahl(l) with { FirmaAnreichern = true });

        Assert.Equal(OdooAnlageStand.Fertig, stand.Stand);
        Assert.False(odoo.Satz("res.partner", stand.FirmaId!.Value).ContainsKey("vat"));
        Assert.Contains(hinweis, (string)odoo.Tabelle("crm.lead").Single()["description"]!);
    }

    [Fact]
    public async Task FirmenkennzeichenNurBerechnet_WirdNichtMitgeschickt()
    {
        // Führt Odoo „ist eine Firma“ nur noch berechnet (z. B. aus der USt-ID), darf das Anlegen daran nicht scheitern
        var odoo = new NachgestelltesOdoo();
        odoo.Schema["res.partner"]["is_company"] = new("boolean", NurLesen: true);
        var stand = Stand();

        var (lage, _) = await AnlegenAsync(odoo, Anfrage(), stand);

        Assert.False(lage.FirmenkennzeichenSetzbar);
        Assert.Equal(OdooAnlageStand.Fertig, stand.Stand);
        Assert.False(odoo.Satz("res.partner", stand.FirmaId!.Value).ContainsKey("is_company"));
    }

    [Fact]
    public async Task OhneFirmenname_KeineFirma_VerkaufschanceHaengtAmKontakt()
    {
        var odoo = new NachgestelltesOdoo();
        var anfrage = Anfrage();
        (anfrage.Firma, anfrage.Email) = (null, "erika.muster@gmail.com");
        var stand = Stand();

        await AnlegenAsync(odoo, anfrage, stand);

        Assert.Null(stand.FirmaId);
        var lead = odoo.Tabelle("crm.lead").Single();
        Assert.Equal(stand.KontaktId, lead["partner_id"]);
        Assert.Equal("Infomappe - Erika Muster", lead["name"]);
        Assert.Equal("erika.muster@gmail.com", lead["email_from"]);
        Assert.Single(odoo.Tabelle("res.partner"));
    }

    [Fact]
    public async Task OhneLeserechtAufDieModellListe_LegtOdooDieAktivitaetAnDerVerkaufschanceAn()
    {
        var odoo = new NachgestelltesOdoo { Fehler = aufruf => aufruf == "ir.model.search_read" ? "odoo.exceptions.AccessError" : null };
        var stand = Stand();

        var (lage, _) = await AnlegenAsync(odoo, Anfrage(), stand);

        Assert.Null(lage.LeadModellId);
        Assert.Equal(1, odoo.Anzahl("crm.lead.activity_schedule"));
        Assert.Equal(0, odoo.Anzahl("mail.activity.create"));
        Assert.Equal(stand.LeadId, odoo.Satz("mail.activity", stand.AktivitaetId!.Value)["res_id"]);
    }

    [Fact]
    public async Task Lesefehler_BrichtDieVorschauAb_MitFestemText()
    {
        var odoo = new NachgestelltesOdoo { Fehler = aufruf => aufruf == "crm.tag.search_read" ? "odoo.exceptions.AccessError" : null };

        var fehler = await Assert.ThrowsAsync<OdooSchrittFehler>(() => new AnfrageOdooAnleger(odoo, Basis).LageAsync(Anfrage(), null));

        Assert.Equal("Stichwörter lesen", fehler.Schritt);
        Assert.Equal("odoo.exceptions.AccessError", OdooFehlerText.Art(fehler));
        // Odoos Rohtext erscheint nie in der Meldung für die Oberfläche
        Assert.DoesNotContain("ROHTEXT", OdooFehlerText.Fuer(fehler));
    }

    [Theory]
    [InlineData("odoo.exceptions.AccessDenied", "Zugriff abgelehnt")]
    [InlineData("odoo.exceptions.ValidationError", "Angaben abgelehnt")]
    [InlineData("builtins.KeyError", "meldet einen Fehler")]
    public void Fehlertexte_SindFesteKategorien(string art, string erwartet) =>
        Assert.Contains(erwartet, OdooFehlerText.Fuer(new OdooFehlerException(art, "Odoo-Fehler: geheim")));

    [Fact]
    public void Fehlertexte_AnmeldungUndNetz()
    {
        Assert.Contains("Anmeldung abgelehnt", OdooFehlerText.Fuer(new InvalidOperationException("Odoo-Anmeldung fehlgeschlagen — Benutzer 'x'")));
        Assert.Contains("antwortet nicht", OdooFehlerText.Fuer(new HttpRequestException("https://intern.example verweigert")));
        Assert.Contains("antwortet nicht", OdooFehlerText.Fuer(new OdooSchrittFehler("Firma anlegen", new TimeoutException())));
        Assert.DoesNotContain("intern.example", OdooFehlerText.Fuer(new HttpRequestException("https://intern.example verweigert")));
    }

    [Fact]
    public void Suchbedingungen_MaskierenPlatzhalter_UndSuchenNurHauptkontakte()
    {
        var kontakt = System.Text.Json.JsonSerializer.Serialize(AnfrageOdooAnleger.KontaktBedingungen("erika_muster@muster-logistik.example"));
        Assert.Contains(@"erika\\_muster@muster-logistik.example", kontakt);
        Assert.Contains("%@muster-logistik.example", kontakt);

        var freemail = AnfrageOdooAnleger.KontaktBedingungen("erika@gmail.com");
        Assert.Single(freemail); // bei Freemailern nur die genaue Adresse, nie die ganze Domäne

        var firma = System.Text.Json.JsonSerializer.Serialize(AnfrageOdooAnleger.FirmenBedingungen("Muster Logistik GmbH", Email, []));
        Assert.StartsWith("""[["parent_id","=",false]""", firma);
        Assert.Contains("""["name","ilike","Logistik"]""", firma);
    }
}
