using System.Text.Json;
using Rausoft.Erp.OdooImport;

namespace Rausoft.Erp.Tests.OdooImport;

/// <summary>
/// Odoo-Projekte zu bestätigten Aufträgen — gegen ein nachgestelltes Odoo mit erfundenen
/// Daten. Entscheidend: Die sichere Spur (Auftrag, Verkaufschance) gewinnt, die Vermutung
/// über den Kunden hängt ein Projekt nie an eine frühere oder an zwei Bestellungen, und
/// es wird kein Feld abgefragt, das die Odoo-Einrichtung nicht kennt.
/// </summary>
public class AuftragsProjekteTests
{
    private sealed class Attrappe(Func<string, string, object?> antwort) : IOdooAufruf
    {
        public List<(string Modell, string Methode, object[] Args, Dictionary<string, object>? Kwargs)> Aufrufe { get; } = [];

        public Task<JsonElement> AusfuehrenAsync(string modell, string methode, object[] args, Dictionary<string, object>? kwargs = null)
        {
            Aufrufe.Add((modell, methode, args, kwargs));
            return Task.FromResult(JsonSerializer.SerializeToElement(antwort(modell, methode)));
        }

        public bool Gerufen(string modell, string methode) => Aufrufe.Any(a => a.Modell == modell && a.Methode == methode);

        public string[] Felder(string modell, string methode) =>
            (string[])Aufrufe.Single(a => a.Modell == modell && a.Methode == methode).Kwargs!["fields"];
    }

    /// <summary>Nachgestelltes Odoo: Felder je Modell und die Zeilen, die jede Abfrage liefert.</summary>
    private sealed class Odoo
    {
        public Dictionary<string, object> AuftragFelder { get; } = new() { ["name"] = Feld("char") };
        public Dictionary<string, object> ProjektFelder { get; } = new()
        {
            ["name"] = Feld("char"), ["active"] = Feld("boolean"), ["partner_id"] = Feld("many2one", "res.partner"),
        };
        public Dictionary<string, object> AufgabenFelder { get; } = new()
        {
            ["project_id"] = Feld("many2one", "project.project"), ["state"] = Feld("selection"),
        };
        public List<Dictionary<string, object>> Auftraege { get; } = [];
        public List<Dictionary<string, object>> Chancen { get; } = [];
        public List<Dictionary<string, object>> Projekte { get; } = [];
        public List<Dictionary<string, object>> Positionen { get; } = [];
        public List<Dictionary<string, object>> Partner { get; } = [];
        public List<Dictionary<string, object>> Aufgaben { get; } = [];

        public Attrappe Bauen() => new((modell, methode) => (modell, methode) switch
        {
            ("sale.order", "fields_get") => AuftragFelder,
            ("project.project", "fields_get") => ProjektFelder,
            ("project.task", "fields_get") => AufgabenFelder,
            ("sale.order", "read") => Auftraege,
            ("crm.lead", "search_read") => Chancen,
            ("project.project", "search_read") => Projekte,
            ("sale.order.line", "search_read") => Positionen,
            ("res.partner", "read") => Partner,
            ("project.task", "search_read") => Aufgaben,
            _ => throw new InvalidOperationException($"Unerwarteter Aufruf {modell}.{methode}"),
        });
    }

    private static Dictionary<string, object> Feld(string typ, string? relation = null, bool suchbar = true)
    {
        var feld = new Dictionary<string, object> { ["type"] = typ, ["searchable"] = suchbar };
        if (relation is not null)
            feld["relation"] = relation;
        return feld;
    }

    private static OdooVerkauf Auftrag(int id, int firma, DateOnly datum) =>
        new(id, $"S0{id}", true, firma, "Muster GmbH", datum, null, null, null, false, false, null, []);

    private static Dictionary<string, object> Projekt(int id, string name, int partner, string angelegt, bool aktiv = true) => new()
    {
        ["id"] = id, ["name"] = name, ["partner_id"] = new object[] { partner, "Muster GmbH" },
        ["create_date"] = $"{angelegt} 11:00:00", ["write_date"] = $"{angelegt} 11:00:00", ["active"] = aktiv,
    };

    private static Dictionary<string, object> Aufgabe(int projekt, string zustand, string geaendert) => new()
    {
        ["project_id"] = new object[] { projekt, "Projekt" }, ["state"] = zustand, ["write_date"] = $"{geaendert} 11:00:00",
    };

    [Fact]
    public async Task ProjektAmAuftragZaehltAufgabenUndLetzteBewegung()
    {
        var odoo = new Odoo();
        odoo.AuftragFelder["project_ids"] = Feld("many2many", "project.project", suchbar: false);
        odoo.Auftraege.Add(new() { ["id"] = 1300, ["project_ids"] = new[] { 70 } });
        odoo.Projekte.Add(Projekt(70, "Muster GmbH – Einführung", 5, "2026-11-02"));
        odoo.Aufgaben.AddRange([Aufgabe(70, "1_done", "2026-11-05"), Aufgabe(70, "1_canceled", "2026-11-04"), Aufgabe(70, "01_in_progress", "2026-11-12")]);
        var attrappe = odoo.Bauen();

        var ergebnis = await AuftragsProjekte.LadenAsync(attrappe, [Auftrag(1300, 5, new DateOnly(2026, 11, 2))]);

        var p = Assert.Single(ergebnis[1300]);
        Assert.Equal(ProjektBezug.Auftrag, p.Bezug);
        Assert.Equal("Muster GmbH – Einführung", p.Name);
        Assert.Equal(3, p.Aufgaben);
        Assert.Equal(2, p.AufgabenErledigt);                    // erledigt und abgebrochen sind beide geschlossen
        Assert.Equal(new DateOnly(2026, 11, 12), p.LetzteBewegung);
        Assert.Equal(new DateOnly(2026, 11, 2), p.AngelegtAm);
        Assert.False(p.Abgeschlossen);
        Assert.Null(p.Phase);
        // Nur Felder, die es in dieser Einrichtung gibt
        Assert.DoesNotContain("stage_id", attrappe.Felder("project.project", "search_read"));
        Assert.DoesNotContain("last_update_status", attrappe.Felder("project.project", "search_read"));
        Assert.Equal(["project_ids"], attrappe.Felder("sale.order", "read"));
        // Archivierte (abgeschlossene) Projekte werden mitgelesen
        var kontext = (Dictionary<string, object>)attrappe.Aufrufe.Single(a => a is ("project.project", "search_read", _, _)).Kwargs!["context"];
        Assert.False((bool)kontext["active_test"]);
    }

    [Fact]
    public async Task ProjektUeberAuftragspositionUndArchiviertGiltAlsAbgeschlossen()
    {
        var odoo = new Odoo();
        odoo.ProjektFelder["sale_line_id"] = Feld("many2one", "sale.order.line");
        odoo.ProjektFelder["last_update_status"] = Feld("selection");
        var projekt = Projekt(71, "Altprojekt", 5, "2026-10-01", aktiv: false);
        projekt["sale_line_id"] = new object[] { 900, "S01301 - Einrichtung" };
        projekt["last_update_status"] = "on_hold";
        odoo.Projekte.Add(projekt);
        odoo.Positionen.Add(new() { ["id"] = 900, ["order_id"] = new object[] { 1301, "S01301" } });
        var attrappe = odoo.Bauen();

        var ergebnis = await AuftragsProjekte.LadenAsync(attrappe, [Auftrag(1301, 5, new DateOnly(2026, 10, 1))]);

        var p = Assert.Single(ergebnis[1301]);
        Assert.Equal(ProjektBezug.Auftrag, p.Bezug);
        Assert.True(p.Abgeschlossen);
        Assert.Equal("pausiert", p.Status);
        Assert.Equal(0, p.Aufgaben);
        // Gesucht wird über die Position des Auftrags
        var suche = JsonSerializer.Serialize(attrappe.Aufrufe.Single(a => a is ("project.project", "search_read", _, _)).Args[0]);
        Assert.Contains("\"sale_line_id.order_id\",\"in\",[1301]", suche);
        Assert.False(attrappe.Gerufen("sale.order", "read"));   // der Auftrag kennt weder Projekte noch Verkaufschance
    }

    [Fact]
    public async Task ProjektAnDerVerkaufschanceWirdDemAuftragZugeordnet()
    {
        var odoo = new Odoo();
        odoo.AuftragFelder["opportunity_id"] = Feld("many2one", "crm.lead");
        odoo.ProjektFelder["x_chance"] = Feld("many2one", "crm.lead");
        odoo.Auftraege.Add(new() { ["id"] = 1302, ["opportunity_id"] = new object[] { 40, "Muster GmbH – Anfrage" } });
        odoo.Chancen.Add(new() { ["id"] = 40, ["date_closed"] = "2026-11-03 10:00:00" });
        var projekt = Projekt(72, "Projekt aus der Verkaufschance", 99, "2026-11-03");
        projekt["x_chance"] = new object[] { 40, "Muster GmbH – Anfrage" };
        odoo.Projekte.Add(projekt);

        var ergebnis = await AuftragsProjekte.LadenAsync(odoo.Bauen(), [Auftrag(1302, 5, new DateOnly(2026, 11, 10))]);

        Assert.Equal(ProjektBezug.Verkaufschance, Assert.Single(ergebnis[1302]).Bezug);
    }

    [Fact]
    public async Task VermutungUeberDenKundenNurAmJuengstenPassendenAuftrag()
    {
        var odoo = new Odoo();
        // Projekt hängt am Ansprechpartner (8) der Firma (5)
        odoo.Partner.Add(new() { ["id"] = 8, ["commercial_partner_id"] = new object[] { 5, "Muster GmbH" } });
        odoo.Projekte.Add(Projekt(73, "Neues Projekt", 8, "2026-11-11"));
        odoo.Projekte.Add(Projekt(74, "Projekt einer früheren Bestellung", 8, "2026-08-01"));
        var frueher = Auftrag(1303, 5, new DateOnly(2026, 9, 1));
        var passend = Auftrag(1304, 5, new DateOnly(2026, 11, 10));
        var spaeter = Auftrag(1305, 5, new DateOnly(2026, 11, 20));
        var andererKunde = Auftrag(1306, 6, new DateOnly(2026, 11, 10));

        var ergebnis = await AuftragsProjekte.LadenAsync(odoo.Bauen(), [frueher, passend, spaeter, andererKunde]);

        // Das neue Projekt gehört zur Bestellung kurz davor — nicht zur alten, nicht zur späteren
        var p = Assert.Single(ergebnis[1304]);
        Assert.Equal(73, p.Id);
        Assert.Equal(ProjektBezug.Kunde, p.Bezug);
        // Das alte Projekt entstand vor allen Aufträgen der Liste: es bleibt ohne Zuordnung
        Assert.Equal(new[] { 1304 }, ergebnis.Keys.ToArray());
    }

    [Fact]
    public async Task ProjektKurzVorDemAuftragPasstNurMitGewonnenerVerkaufschance()
    {
        // Verkaufschance am 01.11. gewonnen (Projekt entsteht), Auftrag erst am 12.11. bestätigt
        var odoo = new Odoo();
        odoo.AuftragFelder["opportunity_id"] = Feld("many2one", "crm.lead");
        odoo.Auftraege.Add(new() { ["id"] = 1307, ["opportunity_id"] = new object[] { 41, "Chance" } });
        odoo.Chancen.Add(new() { ["id"] = 41, ["date_closed"] = "2026-11-01 10:00:00" });
        odoo.Projekte.Add(Projekt(75, "Projekt", 5, "2026-11-01"));
        var auftraege = new[] { Auftrag(1307, 5, new DateOnly(2026, 11, 12)), Auftrag(1308, 5, new DateOnly(2026, 11, 12)) };

        var ergebnis = await AuftragsProjekte.LadenAsync(odoo.Bauen(), auftraege);

        Assert.Equal(ProjektBezug.Kunde, Assert.Single(ergebnis[1307]).Bezug);
        Assert.False(ergebnis.ContainsKey(1308));               // ohne Verkaufschance liegt das Projekt zu weit davor
    }

    [Fact]
    public async Task SichereSpurSchlaegtDieVermutung()
    {
        var odoo = new Odoo();
        odoo.AuftragFelder["project_ids"] = Feld("many2many", "project.project", suchbar: false);
        odoo.Auftraege.Add(new() { ["id"] = 1309, ["project_ids"] = new[] { 76 } });
        odoo.Projekte.Add(Projekt(76, "Verknüpftes Projekt", 5, "2026-11-10"));
        odoo.Projekte.Add(Projekt(77, "Anderes Projekt desselben Kunden", 5, "2026-11-11"));

        var ergebnis = await AuftragsProjekte.LadenAsync(odoo.Bauen(), [Auftrag(1309, 5, new DateOnly(2026, 11, 10))]);

        Assert.Equal(76, Assert.Single(ergebnis[1309]).Id);
    }

    [Fact]
    public async Task OhneVerknuepfungUndOhneKundeAmProjektWirdNichtGesucht()
    {
        var odoo = new Odoo();
        odoo.ProjektFelder.Remove("partner_id");
        // Ein nicht suchbares Verweisfeld darf nicht in die Suche geraten
        odoo.ProjektFelder["sale_order_id"] = Feld("many2one", "sale.order", suchbar: false);
        var attrappe = odoo.Bauen();

        var ergebnis = await AuftragsProjekte.LadenAsync(attrappe, [Auftrag(1310, 5, new DateOnly(2026, 11, 10))]);

        Assert.Empty(ergebnis);
        Assert.False(attrappe.Gerufen("project.project", "search_read"));
    }

    [Fact]
    public async Task OhneAuftraegeWirdOdooNichtGefragt()
    {
        var attrappe = new Odoo().Bauen();

        Assert.Empty(await AuftragsProjekte.LadenAsync(attrappe, []));
        Assert.Empty(attrappe.Aufrufe);
    }

    [Theory]
    [InlineData("on_track", "im Plan")]
    [InlineData("done", "erledigt")]
    [InlineData("to_define", null)]
    [InlineData(null, null)]
    public void ProjektstatusInWorten(string? status, string? erwartet) =>
        Assert.Equal(erwartet, AuftragsProjekte.StatusText(status));
}
