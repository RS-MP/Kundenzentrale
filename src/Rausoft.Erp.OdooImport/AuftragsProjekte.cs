using System.Globalization;
using System.Text.Json;

namespace Rausoft.Erp.OdooImport;

/// <summary>Woran die Kundenzentrale erkannt hat, dass ein Odoo-Projekt zu einem Auftrag gehört.</summary>
public enum ProjektBezug
{
    /// <summary>Odoo verknüpft das Projekt selbst mit dem Auftrag oder einer seiner Positionen.</summary>
    Auftrag,

    /// <summary>Das Projekt hängt an der Verkaufschance, aus der der Auftrag entstand.</summary>
    Verkaufschance,

    /// <summary>Nur vermutet: Projekt desselben Kunden, angelegt um den Auftrag herum.</summary>
    Kunde,
}

/// <summary>Ein Odoo-Projekt zu einem bestätigten Auftrag — Stand, nicht Inhalt.</summary>
/// <param name="AufgabenErledigt">Leer, wenn diese Odoo-Version den Zustand einer Aufgabe nicht preisgibt.</param>
/// <param name="LetzteBewegung">Jüngste Änderung am Projekt oder an einer seiner Aufgaben.</param>
/// <param name="Abgeschlossen">In Odoo archiviert oder als „Erledigt“ gemeldet.</param>
public sealed record OdooProjekt(
    int Id, string Name, ProjektBezug Bezug, string? Phase, string? Status, string? Leitung,
    DateOnly? AngelegtAm, int Aufgaben, int? AufgabenErledigt, DateOnly? LetzteBewegung, bool Abgeschlossen);

/// <summary>
/// Odoo-Projekte zu bestätigten Aufträgen (ANFORDERUNGEN.md, „Offene Angebote und Aufträge“,
/// MP 26.11.2026): Bei rausoft legt Odoo das Projekt mit seinen Aufgaben selbst an, sobald die
/// Verkaufschance als gewonnen bestätigt wird. Ein bestätigter Auftrag ohne Projekt heißt
/// also: Niemand hat angefangen. Nur lesend.
///
/// Wie das Projekt am Auftrag hängt, legt die Odoo-Einrichtung fest (Standard: über die
/// Auftragsposition; bei einer eigenen Automatisierung an der Verkaufschance evtl. nur über
/// den Kunden). Deshalb fragt der Leser zuerst, welche Verknüpfungsfelder es gibt, und nimmt
/// die sicherste Spur, die er findet: Auftrag → Verkaufschance → Kunde und Zeitpunkt. Die
/// letzte ist eine Vermutung und wird so gekennzeichnet. Keine festen Odoo-Nummern.
/// </summary>
public static class AuftragsProjekte
{
    public const int MaxProjekte = 500;
    public const int MaxAufgaben = 5000;

    /// <summary>
    /// Für die Vermutung über den Kunden: So viele Tage darf das Projekt vor dem Auftrag (bzw.
    /// vor dem Gewinn der Verkaufschance) angelegt sein. Ältere Projekte gehören zu einer
    /// früheren Bestellung desselben Kunden.
    /// </summary>
    public const int ToleranzTage = 3;

    private sealed record Feld(string Typ, string? Relation, bool Suchbar);

    /// <returns>Je Odoo-Auftrag (Id) die zugehörigen Projekte; Aufträge ohne Projekt fehlen.</returns>
    public static async Task<Dictionary<int, List<OdooProjekt>>> LadenAsync(IOdooAufruf odoo, IReadOnlyList<OdooVerkauf> auftraege)
    {
        var ergebnis = new Dictionary<int, List<OdooProjekt>>();
        if (auftraege.Count == 0)
            return ergebnis;
        var auftragIds = auftraege.Select(a => a.Id).Distinct().ToArray();

        // Welche Felder kennt diese Odoo-Einrichtung? Ein unbekanntes Feld in einer Abfrage wäre ein Odoo-Fehler.
        var felderAufrufe = new[] { "sale.order", "project.project", "project.task" }
            .Select(modell => odoo.AusfuehrenAsync(modell, "fields_get", [],
                new Dictionary<string, object> { ["attributes"] = new[] { "type", "relation", "searchable" } }))
            .ToArray();
        var felder = await Task.WhenAll(felderAufrufe);
        var auftragFelder = Felder(felder[0]);
        var projektFelder = Felder(felder[1]);
        var aufgabenFelder = Felder(felder[2]);

        // 1. Was der Auftrag selbst weiß: seine Projekte und seine Verkaufschance
        var projekteAmAuftrag = new Dictionary<int, HashSet<int>>();
        var chanceAmAuftrag = new Dictionary<int, int>();
        var hatProjekte = auftragFelder.GetValueOrDefault("project_ids")?.Relation == "project.project";
        var hatChance = auftragFelder.GetValueOrDefault("opportunity_id")?.Relation == "crm.lead";
        if (hatProjekte || hatChance)
        {
            var lesen = new List<string>();
            if (hatProjekte) lesen.Add("project_ids");
            if (hatChance) lesen.Add("opportunity_id");
            foreach (var a in Zeilen(await odoo.AusfuehrenAsync("sale.order", "read", [auftragIds],
                         new Dictionary<string, object> { ["fields"] = lesen.ToArray() })))
            {
                var id = a.GetProperty("id").GetInt32();
                if (OdooJson.Ids(a, "project_ids") is { Count: > 0 } ids)
                    projekteAmAuftrag[id] = [.. ids];
                if (OdooJson.Many2OneId(a, "opportunity_id") is { } chance)
                    chanceAmAuftrag[id] = chance;
            }
        }
        var chancenIds = chanceAmAuftrag.Values.Distinct().ToArray();
        var gewonnenAm = await GewonnenAmAsync(odoo, chancenIds);

        // 2. Projekte suchen — über jede Verknüpfung, die es in dieser Einrichtung gibt
        var zumAuftrag = VerknuepfungsFelder(projektFelder, "sale.order");
        var zurPosition = VerknuepfungsFelder(projektFelder, "sale.order.line");
        var zurChance = VerknuepfungsFelder(projektFelder, "crm.lead");
        var hatPartner = projektFelder.GetValueOrDefault("partner_id")?.Relation == "res.partner";
        var firmenIds = auftraege.Select(a => a.FirmaId).OfType<int>().Distinct().ToArray();
        var fruehestes = auftraege.Select(a => Bezugstag(a, chanceAmAuftrag, gewonnenAm)).OfType<DateOnly>()
            .DefaultIfEmpty(DateOnly.MinValue).Min();

        var bedingungen = new List<object[]>();
        var bekannte = projekteAmAuftrag.Values.SelectMany(p => p).Distinct().ToArray();
        if (bekannte.Length > 0)
            bedingungen.Add([Blatt("id", "in", bekannte)]);
        foreach (var f in zumAuftrag)
            bedingungen.Add([Blatt(f, "in", auftragIds)]);
        foreach (var f in zurPosition)
            bedingungen.Add([Blatt($"{f}.order_id", "in", auftragIds)]);
        if (chancenIds.Length > 0)
            foreach (var f in zurChance)
                bedingungen.Add([Blatt(f, "in", chancenIds)]);
        if (hatPartner && firmenIds.Length > 0 && fruehestes > DateOnly.MinValue)
            bedingungen.Add(
            [
                "&",
                Blatt("partner_id.commercial_partner_id", "in", firmenIds),
                Blatt("create_date", ">=", fruehestes.AddDays(-ToleranzTage - 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " 00:00:00"),
            ]);
        if (bedingungen.Count == 0)
            return ergebnis;

        var optionale = new[] { "stage_id", "user_id", "last_update_status", "active", "partner_id" }
            .Where(projektFelder.ContainsKey);
        var projekte = Zeilen(await odoo.AusfuehrenAsync("project.project", "search_read",
            [Enumerable.Repeat<object>("|", bedingungen.Count - 1).Concat(bedingungen.SelectMany(b => b)).ToArray()],
            new Dictionary<string, object>
            {
                ["fields"] = new[] { "id", "name", "create_date", "write_date" }
                    .Concat(optionale).Concat(zumAuftrag).Concat(zurPosition).Concat(zurChance).Distinct().ToArray(),
                ["order"] = "create_date desc, id desc",
                ["limit"] = MaxProjekte,
                // Abgeschlossene Projekte archiviert Odoo — sie zählen trotzdem als „gestartet“
                ["context"] = new Dictionary<string, object> { ["active_test"] = false },
            }));
        if (projekte.Count == 0)
            return ergebnis;

        var auftragDerPosition = await AuftragDerPositionAsync(odoo,
            projekte.SelectMany(p => zurPosition.Select(f => OdooJson.Many2OneId(p, f))).OfType<int>().Distinct().ToArray());
        var firmaDesPartners = hatPartner
            ? await OffeneVerkaeufe.FirmenAsync(odoo, projekte.Select(p => OdooJson.Many2OneId(p, "partner_id")).OfType<int>().Distinct().ToArray())
            : [];
        var aufgaben = await AufgabenAsync(odoo, aufgabenFelder, projekte.Select(p => p.GetProperty("id").GetInt32()).ToArray());

        void Zuordnen(int auftragId, JsonElement p, ProjektBezug bezug)
        {
            if (!ergebnis.TryGetValue(auftragId, out var liste))
                ergebnis[auftragId] = liste = [];
            liste.Add(Projekt(p, bezug, aufgaben.GetValueOrDefault(p.GetProperty("id").GetInt32())));
        }

        // 3. Sichere Spuren zuerst: Auftrag, dann Verkaufschance
        var bekannteAuftraege = auftragIds.ToHashSet();
        var offen = new List<JsonElement>();
        foreach (var p in projekte)
        {
            var id = p.GetProperty("id").GetInt32();
            var ueberAuftrag = projekteAmAuftrag.Where(a => a.Value.Contains(id)).Select(a => a.Key)
                .Concat(zumAuftrag.Select(f => OdooJson.Many2OneId(p, f)).OfType<int>())
                .Concat(zurPosition.Select(f => OdooJson.Many2OneId(p, f)).OfType<int>()
                    .Select(position => auftragDerPosition.GetValueOrDefault(position)))
                .Where(bekannteAuftraege.Contains).Distinct().ToList();
            if (ueberAuftrag.Count > 0)
            {
                foreach (var a in ueberAuftrag)
                    Zuordnen(a, p, ProjektBezug.Auftrag);
                continue;
            }
            var chancen = zurChance.Select(f => OdooJson.Many2OneId(p, f)).OfType<int>().ToHashSet();
            var ueberChance = chanceAmAuftrag.Where(c => chancen.Contains(c.Value)).Select(c => c.Key).ToList();
            if (ueberChance.Count > 0)
            {
                foreach (var a in ueberChance)
                    Zuordnen(a, p, ProjektBezug.Verkaufschance);
                continue;
            }
            offen.Add(p);
        }

        // 4. Vermutung über den Kunden — nur für Aufträge ohne sichere Spur, jedes Projekt
        //    höchstens einmal, und zwar am jüngsten Auftrag, der zeitlich davor liegt.
        var ohneSpur = auftraege.Where(a => !ergebnis.ContainsKey(a.Id) && a.FirmaId is not null).ToList();
        foreach (var p in offen)
        {
            if (OdooJson.Many2OneId(p, "partner_id") is not { } partner
                || OdooJson.Zeitpunkt(p, "create_date") is not { } angelegt)
                continue;
            var firma = firmaDesPartners.GetValueOrDefault(partner, partner);
            var tag = DateOnly.FromDateTime(angelegt);
            var treffer = ohneSpur
                .Where(a => a.FirmaId == firma)
                .Select(a => (Auftrag: a, Tag: Bezugstag(a, chanceAmAuftrag, gewonnenAm)))
                .Where(x => x.Tag is { } bezug && tag >= bezug.AddDays(-ToleranzTage))
                .OrderByDescending(x => x.Tag).ThenByDescending(x => x.Auftrag.Id)
                .Select(x => x.Auftrag)
                .FirstOrDefault();
            if (treffer is not null)
                Zuordnen(treffer.Id, p, ProjektBezug.Kunde);
        }
        return ergebnis;
    }

    /// <summary>
    /// Ab wann ein Projekt zu diesem Auftrag gehören kann: Auftragsdatum, oder der Tag, an dem
    /// die Verkaufschance gewonnen wurde, falls der früher liegt (dann entsteht das Projekt
    /// schon vor der Auftragsbestätigung).
    /// </summary>
    private static DateOnly? Bezugstag(OdooVerkauf a, Dictionary<int, int> chanceAmAuftrag, Dictionary<int, DateOnly> gewonnenAm)
    {
        DateOnly? gewonnen = chanceAmAuftrag.TryGetValue(a.Id, out var chance) && gewonnenAm.TryGetValue(chance, out var tag) ? tag : null;
        return (a.Datum, gewonnen) switch
        {
            ({ } d, { } g) => d < g ? d : g,
            ({ } d, null) => d,
            (null, var g) => g,
        };
    }

    private static OdooProjekt Projekt(JsonElement p, ProjektBezug bezug, (int Alle, int? Erledigt, DateTime? Zuletzt) aufgaben)
    {
        var status = OdooJson.Text(p, "last_update_status");
        var zuletzt = new[] { OdooJson.Zeitpunkt(p, "write_date"), aufgaben.Zuletzt }.Max();
        return new OdooProjekt(
            p.GetProperty("id").GetInt32(),
            OdooJson.Text(p, "name") ?? "(ohne Namen)",
            bezug,
            OdooJson.Many2OneLabel(p, "stage_id"),
            StatusText(status),
            OdooJson.Many2OneLabel(p, "user_id"),
            OdooJson.Zeitpunkt(p, "create_date") is { } angelegt ? DateOnly.FromDateTime(angelegt) : null,
            aufgaben.Alle, aufgaben.Erledigt,
            zuletzt is { } z ? DateOnly.FromDateTime(z) : null,
            // „active“ fehlt in der Antwort nur, wenn das Feld nicht existiert — dann gilt das Projekt als aktiv
            (p.TryGetProperty("active", out var aktiv) && aktiv.ValueKind == JsonValueKind.False) || status == "done");
    }

    /// <summary>Odoos Projektstatus (letzte Statusmeldung) in Worten; „noch nicht gesetzt“ ist kein Status.</summary>
    public static string? StatusText(string? status) => status switch
    {
        "on_track" => "im Plan",
        "at_risk" => "gefährdet",
        "off_track" => "nicht im Plan",
        "on_hold" => "pausiert",
        "done" => "erledigt",
        _ => null,
    };

    /// <summary>Je Projekt: Zahl der Aufgaben, davon erledigt oder abgebrochen, jüngste Änderung.</summary>
    private static async Task<Dictionary<int, (int Alle, int? Erledigt, DateTime? Zuletzt)>> AufgabenAsync(
        IOdooAufruf odoo, Dictionary<string, Feld> aufgabenFelder, int[] projektIds)
    {
        var jeProjekt = new Dictionary<int, (int Alle, int? Erledigt, DateTime? Zuletzt)>();
        if (projektIds.Length == 0 || !aufgabenFelder.ContainsKey("project_id"))
            return jeProjekt;
        // Seit Odoo 17 steht der Zustand in „state“ (1_done, 1_canceled = geschlossen), davor in „is_closed“
        var zustand = aufgabenFelder.ContainsKey("state") ? "state" : aufgabenFelder.ContainsKey("is_closed") ? "is_closed" : null;
        var lesen = new List<string> { "project_id", "write_date" };
        if (zustand is not null)
            lesen.Add(zustand);
        foreach (var t in Zeilen(await odoo.AusfuehrenAsync("project.task", "search_read",
                     [new object[] { Blatt("project_id", "in", projektIds) }],
                     new Dictionary<string, object> { ["fields"] = lesen.ToArray(), ["limit"] = MaxAufgaben })))
        {
            if (OdooJson.Many2OneId(t, "project_id") is not { } projekt)
                continue;
            var stand = jeProjekt.GetValueOrDefault(projekt, (0, zustand is null ? (int?)null : 0, null));
            var geschlossen = zustand switch
            {
                "state" => OdooJson.Text(t, "state")?.StartsWith("1_", StringComparison.Ordinal) == true,
                "is_closed" => OdooJson.Wahr(t, "is_closed"),
                _ => false,
            };
            var geaendert = OdooJson.Zeitpunkt(t, "write_date");
            jeProjekt[projekt] = (
                stand.Alle + 1,
                stand.Erledigt is { } e ? e + (geschlossen ? 1 : 0) : null,
                geaendert > stand.Zuletzt || stand.Zuletzt is null ? geaendert ?? stand.Zuletzt : stand.Zuletzt);
        }
        return jeProjekt;
    }

    /// <summary>Tag, an dem die Verkaufschance gewonnen wurde (date_closed); gesucht statt gelesen, damit Zugriffsregeln nicht stören.</summary>
    private static async Task<Dictionary<int, DateOnly>> GewonnenAmAsync(IOdooAufruf odoo, int[] chancenIds)
    {
        var tage = new Dictionary<int, DateOnly>();
        if (chancenIds.Length == 0)
            return tage;
        foreach (var c in Zeilen(await odoo.AusfuehrenAsync("crm.lead", "search_read",
                     [new object[] { Blatt("id", "in", chancenIds) }],
                     new Dictionary<string, object>
                     {
                         ["fields"] = new[] { "date_closed" },
                         ["context"] = new Dictionary<string, object> { ["active_test"] = false },
                     })))
            if (OdooJson.Zeitpunkt(c, "date_closed") is { } z)
                tage[c.GetProperty("id").GetInt32()] = DateOnly.FromDateTime(z);
        return tage;
    }

    private static async Task<Dictionary<int, int>> AuftragDerPositionAsync(IOdooAufruf odoo, int[] positionIds)
    {
        var auftraege = new Dictionary<int, int>();
        if (positionIds.Length == 0)
            return auftraege;
        foreach (var z in Zeilen(await odoo.AusfuehrenAsync("sale.order.line", "search_read",
                     [new object[] { Blatt("id", "in", positionIds) }],
                     new Dictionary<string, object> { ["fields"] = new[] { "order_id" } })))
            if (OdooJson.Many2OneId(z, "order_id") is { } auftrag)
                auftraege[z.GetProperty("id").GetInt32()] = auftrag;
        return auftraege;
    }

    private static object[] Blatt(string feld, string vergleich, object wert) => [feld, vergleich, wert];

    private static List<JsonElement> Zeilen(JsonElement antwort) =>
        antwort.ValueKind == JsonValueKind.Array ? antwort.EnumerateArray().ToList() : [];

    private static Dictionary<string, Feld> Felder(JsonElement antwort) =>
        antwort.ValueKind != JsonValueKind.Object
            ? []
            : antwort.EnumerateObject()
                .Where(f => f.Value.ValueKind == JsonValueKind.Object)
                .ToDictionary(f => f.Name, f => new Feld(
                    OdooJson.Text(f.Value, "type") ?? "",
                    OdooJson.Text(f.Value, "relation"),
                    // fehlt die Angabe, gilt das Feld als suchbar (gespeicherte Felder sind es immer)
                    !(f.Value.TryGetProperty("searchable", out var s) && s.ValueKind == JsonValueKind.False)));

    /// <summary>Suchbare Verweisfelder (many2one) des Projekts auf das genannte Modell, in fester Reihenfolge.</summary>
    private static List<string> VerknuepfungsFelder(Dictionary<string, Feld> felder, string modell) =>
        felder.Where(f => f.Value is { Typ: "many2one", Suchbar: true } && f.Value.Relation == modell)
            .Select(f => f.Key).Order(StringComparer.Ordinal).ToList();
}
