using System.Text.Json;
using Rausoft.Erp.Domain.Customers;
using static Rausoft.Erp.OdooImport.OdooJson;

namespace Rausoft.Erp.OdooImport;

/// <summary>Eintrag einer Auswahlliste aus Odoo (Phase, Stichwort, Benutzer, Aktivitätsart, Quelle, Medium).</summary>
public record OdooEintrag(int Id, string Name);

/// <summary>Verkaufschance in Odoo, die zur Firma oder zum Kontakt der Anfrage gehört.</summary>
public record OdooLeadInfo(int Id, string Name, string? Phase, bool Offen, string? Zustaendig, int? PartnerId);

/// <summary>Kontakt in Odoo mit der E-Mail-Adresse der Anfrage.</summary>
public record OdooKontakt(int Id, string Name, string? Email, int? FirmaId, string? FirmaName, string? Vorname, bool HatAnrede);

/// <summary>
/// Alles, was vor dem Schreiben aus Odoo gelesen wurde: vorhandener Kontakt, mögliche
/// Firmen, offene Verkaufschancen und die Auswahllisten. Daraus entsteht die Vorschau,
/// und gegen genau diese Lage wird die Wahl des Mitarbeiters geprüft.
/// </summary>
public class OdooAnlageLage
{
    public required OdooPerson Person { get; init; }
    public required string Email { get; init; }
    public OdooKontakt? Kontakt { get; init; }
    /// <summary>Wählbare Firmen, beste zuerst; steht die Firma schon fest (Fortsetzung), nur diese.</summary>
    public List<OdooFirmaBewertung> Firmen { get; init; } = [];
    public int? FirmaVorschlag { get; init; }
    public Dictionary<int, List<OdooLeadInfo>> LeadsJeFirma { get; init; } = [];
    public List<OdooLeadInfo> LeadsZumKontakt { get; init; } = [];
    public List<OdooEintrag> Phasen { get; init; } = [];
    public List<OdooEintrag> Stichwoerter { get; init; } = [];
    public List<OdooEintrag> Benutzer { get; init; } = [];
    public List<OdooEintrag> Aktivitaetsarten { get; init; } = [];
    public List<OdooEintrag> Quellen { get; init; } = [];
    public List<OdooEintrag> Medien { get; init; } = [];
    /// <summary>Nummern der Anreden „Herr“ und „Frau“ in dieser Odoo-Datenbank; leer, wenn es das Feld nicht gibt.</summary>
    public Dictionary<Anrede, int> AnredeIds { get; init; } = [];
    /// <summary>Vorhandene Felder der Verkaufschance mit ihrem Typ — geschrieben wird nur, was es gibt.</summary>
    public Dictionary<string, string> LeadFelder { get; init; } = [];
    public HashSet<string> PartnerFelder { get; init; } = [];
    /// <summary>
    /// Das Kennzeichen „ist eine Firma“ (is_company) lässt sich schreiben. Ohne USt-ID setzt Odoo
    /// es nicht von selbst — eine neue Firma bekäme sonst die Art „Person“. Führt eine
    /// Odoo-Version das Feld nur noch berechnet, wird es nicht mitgeschickt.
    /// </summary>
    public bool FirmenkennzeichenSetzbar { get; init; } = true;
    /// <summary>Nummer des Modells „crm.lead“ für die Aktivität; fehlt sie, legt Odoo die Aktivität über die Verkaufschance an.</summary>
    public int? LeadModellId { get; init; }
    public List<string> Hinweise { get; init; } = [];

    /// <summary>Offene Verkaufschancen, die bei dieser Firma ergänzt werden könnten.</summary>
    public List<OdooLeadInfo> OffeneLeads(int? firmaId) =>
        (firmaId is { } id ? LeadsJeFirma.GetValueOrDefault(id) ?? [] : [])
        .Concat(LeadsZumKontakt).Where(l => l.Offen).DistinctBy(l => l.Id).ToList();
}

/// <summary>Was der Mitarbeiter in der Vorschau gewählt hat.</summary>
/// <param name="FirmaId">Vorhandene Firma; <c>null</c> = neue Firma anlegen (bzw. ohne Firma, wenn kein Name bekannt ist).</param>
/// <param name="Anrede"><c>null</c> = nicht gewählt.</param>
/// <param name="LeadId">Offene Verkaufschance, die ergänzt wird; <c>null</c> = neue anlegen.</param>
public record OdooAnlageWahl(
    int? FirmaId,
    Anrede? Anrede,
    bool HauptansprechpartnerUmstellen,
    int? LeadId,
    int? PhaseId,
    IReadOnlyList<int> StichwortIds,
    int? BenutzerId,
    int? AktivitaetsartId,
    int? QuelleId,
    int? MediumId,
    /// <summary>Eine neue Firma von Odoo über die E-Mail-Domäne anreichern lassen (Adresse, USt-ID …).</summary>
    bool FirmaAnreichern = false);

/// <summary>Ein Schritt in Odoo ist fehlgeschlagen. Die Ursache steht in <see cref="Exception.InnerException"/>.</summary>
public class OdooSchrittFehler(string schritt, Exception ursache) : Exception(schritt, ursache)
{
    public string Schritt { get; } = schritt;
}

/// <summary>Feste Meldungen zu Odoo-Fehlern — Odoos eigene Antwort geht weder in die Oberfläche noch ins Log.</summary>
public static class OdooFehlerText
{
    public static string Fuer(Exception ex) => ex switch
    {
        OdooSchrittFehler { InnerException: { } innen } => Fuer(innen),
        OdooFehlerException { Art: { } art } when art.Contains("Access", StringComparison.OrdinalIgnoreCase) =>
            "Odoo hat den Zugriff abgelehnt — dem API-Benutzer fehlt das Recht dafür.",
        OdooFehlerException { Art: { } art } when art.Contains("ValidationError") || art.Contains("UserError") || art.Contains("MissingError") =>
            "Odoo hat die Angaben abgelehnt.",
        OdooFehlerException => "Odoo meldet einen Fehler.",
        InvalidOperationException when ex.Message.StartsWith("Odoo-Anmeldung fehlgeschlagen") =>
            "Odoo hat die Anmeldung abgelehnt — Datenbank, Benutzer oder API-Key stimmen nicht.",
        HttpRequestException or TimeoutException or TaskCanceledException or JsonException =>
            "Odoo antwortet nicht oder nicht verständlich.",
        _ => "Unerwarteter Fehler.",
    };

    /// <summary>Kurzbezeichnung fürs Log: Name der Odoo-Ausnahme bzw. der .NET-Typ — nie die Meldung.</summary>
    public static string Art(Exception ex) => ex switch
    {
        OdooSchrittFehler { InnerException: { } innen } => Art(innen),
        OdooFehlerException { Art: { } art } => art,
        _ => ex.GetType().Name,
    };
}

/// <summary>
/// Legt eine gespeicherte Website-Anfrage in Odoo an: Firma, Kontakt, Hauptansprechpartner,
/// Verkaufschance und Aktivität — so wie es bisher ein Mitarbeiter von Hand tat (Zuordnung
/// aus dem n8n-Workflow, mit den Entscheidungen von MP vom 24.11.2026: Vorschau und
/// Bestätigung, Anrede nur als Vorschlag, vorhandener Hauptansprechpartner bleibt, Phase,
/// Stichwörter und Zuweisung wählbar, keine festen Odoo-Nummern im Programm).
///
/// Zuerst <see cref="LageAsync"/> (nur lesen), dann <see cref="AusfuehrenAsync"/>. Jeder
/// Schritt trägt sein Ergebnis in den Stand ein und speichert ihn, bevor der nächste
/// beginnt — ein abgebrochener Lauf setzt dort wieder auf und legt nichts doppelt an.
/// </summary>
public class AnfrageOdooAnleger(IOdooAufruf odoo, string basisUrl)
{
    private static readonly TimeSpan Wartezeit = TimeSpan.FromSeconds(30);
    private const string HauptansprechpartnerFeld = "x_studio_hauptansprechpartner";
    private const string AnredeFeld = "x_studio_anrede";
    private const string VornameFeld = "x_studio_vorname";
    private const string HauptansprechpartnerStichwort = "Hauptansprechpartner";

    private readonly string _basis = basisUrl.TrimEnd('/');

    public string KontaktLink(int id) => $"{_basis}/odoo/contacts/{id}";
    public string LeadLink(int id) => LeadLink(_basis, id);
    public static string LeadLink(string basisUrl, int id) => $"{basisUrl.TrimEnd('/')}/odoo/crm.lead/{id}";

    // ---------- Lesen ----------

    public async Task<OdooAnlageLage> LageAsync(Anfrage a, AnfrageOdooAnlage? stand)
    {
        var email = OdooAnlageRegeln.EmailFuerOdoo(a.Email)
            ?? throw new InvalidOperationException("Anfrage ohne gültige E-Mail-Adresse.");
        var person = OdooPersonenRegeln.Aus(a.Vorname, a.Nachname, email);
        var hinweise = new List<string>(person.Hinweise);

        // Der erste Aufruf meldet an; danach laufen die unabhängigen Abfragen nebeneinander
        var leadFelder = FeldTypen(await LesenAsync("Felder der Verkaufschance", "crm.lead", "fields_get", [],
            new() { ["attributes"] = new[] { "type" } }));

        var partnerFelderAufruf = LesenAsync("Felder der Kontakte", "res.partner", "fields_get", [],
            new() { ["attributes"] = new[] { "type", "relation", "readonly" } });
        var phasenAufruf = SuchenAsync("Phasen", "crm.stage", [], ["id", "name", "is_won"], 100, "sequence, id");
        var stichwoerterAufruf = SuchenAsync("Stichwörter", "crm.tag", [], ["id", "name"], 200, "name");
        var benutzerAufruf = SuchenAsync("Benutzer", "res.users", [Bedingung("share", "=", false)], ["id", "name"], 200, "name");
        var artenAufruf = SuchenAsync("Aktivitätsarten", "mail.activity.type",
            ["|", Bedingung("res_model", "=", false), Bedingung("res_model", "=", "crm.lead")], ["id", "name"], 200, "sequence, id");
        var quellenAufruf = SuchenAsync("Quellen", "utm.source", [], ["id", "name"], 300, "id");
        var medienAufruf = SuchenAsync("Medien", "utm.medium", [], ["id", "name"], 200, "id");
        var kontakteAufruf = SuchenAsync("Kontakte", "res.partner", KontaktBedingungen(email),
            ["id", "name", "email", "parent_id", "is_company", "child_ids"], 80, "id");
        var modellAufruf = LeadModellAsync();
        await Task.WhenAll(partnerFelderAufruf, phasenAufruf, stichwoerterAufruf, benutzerAufruf, artenAufruf,
            quellenAufruf, medienAufruf, kontakteAufruf, modellAufruf);

        var partnerRoh = await partnerFelderAufruf;
        var partnerFelder = FeldTypen(partnerRoh).Keys.ToHashSet();
        var phasenRoh = await phasenAufruf;
        var gewonnen = phasenRoh.Where(p => Wahr(p, "is_won")).Select(p => p.GetProperty("id").GetInt32()).ToHashSet();

        // ---- Kontakt mit genau dieser E-Mail-Adresse ----
        var kontakte = await kontakteAufruf;
        bool WieFirma(JsonElement k) => Wahr(k, "is_company") || AnzahlIds(k, "child_ids") > 0;
        var genau = kontakte.Where(k => string.Equals(Text(k, "email"), email, StringComparison.OrdinalIgnoreCase) && !WieFirma(k)).ToList();
        var kontaktRoh = genau.Where(k => stand?.KontaktId is { } fest && k.GetProperty("id").GetInt32() == fest)
            .Concat(genau.Where(k => Many2OneId(k, "parent_id") is not null)).Concat(genau)
            .Cast<JsonElement?>().FirstOrDefault();
        if (genau.Count > 1)
            hinweise.Add($"Die E-Mail-Adresse gibt es {genau.Count}× in Odoo (Nr. {string.Join(", ", genau.Select(k => k.GetProperty("id").GetInt32()))}) — bitte dort auf Dubletten prüfen.");

        OdooKontakt? kontakt = null;
        if (kontaktRoh is { } kr)
        {
            // Vorname und Anrede nur lesen, wenn es die Felder in dieser Datenbank gibt
            var id = kr.GetProperty("id").GetInt32();
            var zusatz = new[] { VornameFeld, AnredeFeld }.Where(partnerFelder.Contains).ToArray();
            var mehr = zusatz.Length == 0 ? (JsonElement?)null
                : (await SuchenAsync("Kontakt", "res.partner", [Bedingung("id", "=", id)], ["id", .. zusatz], 1)).Cast<JsonElement?>().FirstOrDefault();
            kontakt = new OdooKontakt(id, Text(kr, "name") ?? email, Text(kr, "email"),
                Many2OneId(kr, "parent_id"), Many2OneLabel(kr, "parent_id"),
                mehr is { } m1 ? Text(m1, VornameFeld) : null,
                mehr is { } m2 && Many2OneId(m2, AnredeFeld) is not null);
        }

        // ---- Anreden dieser Datenbank ----
        var anredeIds = new Dictionary<Anrede, int>();
        if (partnerRoh.ValueKind == JsonValueKind.Object && partnerRoh.TryGetProperty(AnredeFeld, out var anredeFeld)
            && Text(anredeFeld, "relation") is { } anredeModell)
        {
            foreach (var t in await SuchenAsync("Anreden", anredeModell, [], ["id", "display_name"], 50))
                if (OdooPersonenRegeln.Falten(Text(t, "display_name")) is var name && name is "herr" or "frau")
                    anredeIds.TryAdd(name == "herr" ? Anrede.Herr : Anrede.Frau, t.GetProperty("id").GetInt32());
        }
        if (anredeIds.Count < 2)
            hinweise.Add("In dieser Odoo-Datenbank ließ sich die Anrede (Herr/Frau) nicht zuordnen — sie wird nicht geschrieben.");

        // ---- Firmen ----
        var domaenenFirmen = kontakte.Select(k => Many2OneId(k, "parent_id")).OfType<int>().ToHashSet();
        var firmenFelder = new List<string> { "id", "name", "email", "website", "is_company", "child_ids" };
        if (partnerFelder.Contains(HauptansprechpartnerFeld))
            firmenFelder.Add(HauptansprechpartnerFeld);
        else
            hinweise.Add("Das Feld „Hauptansprechpartner“ gibt es in dieser Odoo-Datenbank nicht — es wird nicht gesetzt.");
        var firmenRoh = await SuchenAsync("Firmen", "res.partner",
            FirmenBedingungen(a.Firma, email, new[] { stand?.FirmaId, kontakt?.FirmaId }.OfType<int>().Distinct().ToArray()),
            firmenFelder.ToArray(), 30, "id asc");
        var firmen = firmenRoh.Where(f => f.GetProperty("id").GetInt32() != kontakt?.Id)
            .Select(f => new OdooFirma(f.GetProperty("id").GetInt32(), Text(f, "name") ?? "(ohne Namen)", Text(f, "email"),
                Text(f, "website"), Wahr(f, "is_company"), AnzahlIds(f, "child_ids"),
                Many2OneId(f, HauptansprechpartnerFeld), Many2OneLabel(f, HauptansprechpartnerFeld)))
            .ToList();
        var bewertet = OdooFirmenRegeln.Bewerten(firmen, a.Firma, email, kontakt?.FirmaId, domaenenFirmen);

        List<OdooFirmaBewertung> waehlbar;
        int? vorschlag;
        if (stand?.FirmaId is { } festeFirma && bewertet.FirstOrDefault(b => b.Firma.Id == festeFirma) is { } fest)
        {
            // Fortsetzung: die Firma steht aus dem ersten Lauf fest
            (waehlbar, vorschlag) = ([fest], festeFirma);
        }
        else
        {
            waehlbar = OdooFirmenRegeln.Kandidaten(bewertet);
            vorschlag = OdooFirmenRegeln.Vorschlag(bewertet)?.Firma.Id;
            if (OdooFirmenRegeln.MehrfachHinweis(bewertet) is { } mehrfach)
                hinweise.Add(mehrfach);
        }
        if (kontakt?.FirmaId is { } kontaktFirma && vorschlag is { } v && v != kontaktFirma)
            hinweise.Add($"Der Kontakt gehört in Odoo zur Firma „{kontakt.FirmaName}“ — die Zuordnung des Kontakts bleibt unverändert.");

        // ---- Verkaufschancen der wählbaren Firmen und des Kontakts ----
        string[] leadFelderLesen = ["id", "name", "type", "stage_id", "partner_id", "user_id"];
        OdooLeadInfo Lead(JsonElement l) => new(l.GetProperty("id").GetInt32(), Text(l, "name") ?? "(ohne Namen)",
            Many2OneLabel(l, "stage_id"), Many2OneId(l, "stage_id") is not { } stufe || !gewonnen.Contains(stufe),
            Many2OneLabel(l, "user_id"), Many2OneId(l, "partner_id"));
        var jeFirmaAufrufe = waehlbar.ToDictionary(b => b.Firma.Id, b => SuchenAsync("Verkaufschancen", "crm.lead",
            [Bedingung("partner_id", "child_of", new[] { b.Firma.Id })], leadFelderLesen, 20, "write_date desc"));
        var mailBedingung = Bedingung("email_from", "=ilike", OdooAnlageRegeln.IlikeMaske(email));
        var zumKontaktAufruf = SuchenAsync("Verkaufschancen", "crm.lead",
            kontakt is null ? [mailBedingung] : ["|", Bedingung("partner_id", "=", kontakt.Id), mailBedingung],
            leadFelderLesen, 20, "write_date desc");
        await Task.WhenAll(jeFirmaAufrufe.Values.Append(zumKontaktAufruf));
        var leadsJeFirma = new Dictionary<int, List<OdooLeadInfo>>();
        foreach (var (firmaId, aufruf) in jeFirmaAufrufe)
            leadsJeFirma[firmaId] = (await aufruf).Select(Lead).ToList();
        var leadsZumKontakt = (await zumKontaktAufruf).Select(Lead).ToList();

        return new OdooAnlageLage
        {
            Person = person,
            Email = email,
            Kontakt = kontakt,
            Firmen = waehlbar,
            FirmaVorschlag = vorschlag,
            LeadsJeFirma = leadsJeFirma,
            LeadsZumKontakt = leadsZumKontakt,
            Phasen = Eintraege(phasenRoh),
            Stichwoerter = Eintraege(await stichwoerterAufruf),
            Benutzer = Eintraege(await benutzerAufruf),
            Aktivitaetsarten = Eintraege(await artenAufruf),
            Quellen = Eintraege(await quellenAufruf),
            Medien = Eintraege(await medienAufruf),
            AnredeIds = anredeIds.Count == 2 ? anredeIds : [],
            LeadFelder = leadFelder,
            PartnerFelder = partnerFelder,
            FirmenkennzeichenSetzbar = partnerRoh.ValueKind == JsonValueKind.Object
                                       && partnerRoh.TryGetProperty("is_company", out var kennzeichen)
                                       && !Wahr(kennzeichen, "readonly"),
            LeadModellId = await modellAufruf,
            Hinweise = hinweise,
        };
    }

    private static List<OdooEintrag> Eintraege(IEnumerable<JsonElement> zeilen) =>
        zeilen.Select(z => new OdooEintrag(z.GetProperty("id").GetInt32(), Text(z, "name") ?? "(ohne Namen)")).ToList();

    private static Dictionary<string, string> FeldTypen(JsonElement felder) =>
        felder.ValueKind == JsonValueKind.Object
            ? felder.EnumerateObject().ToDictionary(f => f.Name, f => Text(f.Value, "type") ?? "")
            : [];

    private static object[] Bedingung(string feld, string vergleich, object wert) => [feld, vergleich, wert];

    /// <summary>Kontakte mit genau dieser Adresse und alle Kontakte derselben Firmen-Domäne.</summary>
    public static object[] KontaktBedingungen(string email)
    {
        var genau = Bedingung("email", "=ilike", OdooAnlageRegeln.IlikeMaske(email));
        if (OdooFirmenRegeln.FirmenDomaene(email) is not { } basis)
            return [genau];
        var maske = OdooAnlageRegeln.IlikeMaske(basis);
        return ["|", "|", genau, Bedingung("email", "=ilike", "%@" + maske), Bedingung("email", "=ilike", "%." + maske)];
    }

    /// <summary>Firmenkandidaten: Hauptkontakte mit dieser Domäne, mit Ansprechpartnern dieser Domäne oder ähnlichem Namen.</summary>
    public static object[] FirmenBedingungen(string? firmenname, string email, IReadOnlyCollection<int> festeIds)
    {
        var oder = new List<object> { Bedingung("child_ids.email", "=ilike", OdooAnlageRegeln.IlikeMaske(email)) };
        if (OdooFirmenRegeln.FirmenDomaene(email) is { } basis)
        {
            var maske = OdooAnlageRegeln.IlikeMaske(basis);
            oder.Add(Bedingung("email", "=ilike", "%@" + maske));
            oder.Add(Bedingung("email", "=ilike", "%." + maske));
            oder.Add(Bedingung("child_ids.email", "=ilike", "%@" + maske));
            oder.Add(Bedingung("child_ids.email", "=ilike", "%." + maske));
            oder.Add(Bedingung("website", "ilike", maske));
        }
        if (OdooFirmenRegeln.SuchWort(firmenname) is { } wort)
            oder.Add(Bedingung("name", "ilike", OdooAnlageRegeln.IlikeMaske(wort)));
        if (festeIds.Count > 0)
            oder.Add(Bedingung("id", "in", festeIds.ToArray()));
        return [Bedingung("parent_id", "=", false), .. Enumerable.Repeat<object>("|", oder.Count - 1), .. oder];
    }

    private async Task<int?> LeadModellAsync()
    {
        try
        {
            var zeilen = await odoo.AusfuehrenAsync("ir.model", "search_read",
                [new object[] { Bedingung("model", "=", "crm.lead") }],
                new() { ["fields"] = new[] { "id" }, ["limit"] = 1 }).WaitAsync(Wartezeit);
            return zeilen.ValueKind == JsonValueKind.Array && zeilen.GetArrayLength() > 0 ? zeilen[0].GetProperty("id").GetInt32() : null;
        }
        catch (OdooFehlerException)
        {
            // Kein Leserecht auf die Modell-Liste: die Aktivität legt dann Odoo selbst an der Verkaufschance an
            return null;
        }
    }

    private async Task<JsonElement> LesenAsync(string was, string modell, string methode, object[] args, Dictionary<string, object> kwargs)
    {
        try
        {
            return await odoo.AusfuehrenAsync(modell, methode, args, kwargs).WaitAsync(Wartezeit);
        }
        catch (Exception ex) when (ex is not OdooSchrittFehler)
        {
            throw new OdooSchrittFehler($"{was} lesen", ex);
        }
    }

    private async Task<List<JsonElement>> SuchenAsync(string was, string modell, object[] bedingungen, string[] felder, int limit, string? reihenfolge = null)
    {
        var kwargs = new Dictionary<string, object> { ["fields"] = felder, ["limit"] = limit };
        if (reihenfolge is not null)
            kwargs["order"] = reihenfolge;
        var ergebnis = await LesenAsync(was, modell, "search_read", [bedingungen], kwargs);
        return ergebnis.ValueKind == JsonValueKind.Array ? ergebnis.EnumerateArray().ToList() : [];
    }

    // ---------- Prüfen der Wahl ----------

    /// <summary>
    /// Prüft die Wahl gegen die gelesene Lage: Jede Nummer muss aus den Listen stammen, die
    /// Odoo eben geliefert hat — der Browser kann so keine beliebigen Datensätze ansprechen.
    /// Liefert den Fehlertext oder <c>null</c>.
    /// </summary>
    public static string? WahlFehler(Anfrage a, OdooAnlageLage lage, OdooAnlageWahl wahl, AnfrageOdooAnlage stand)
    {
        var firmaId = stand.FirmaId ?? wahl.FirmaId;
        if (stand.FirmaId is null && wahl.FirmaId is { } gewaehlt && lage.Firmen.All(f => f.Firma.Id != gewaehlt))
            return "Die gewählte Firma gehört nicht (mehr) zu den Treffern in Odoo — bitte die Vorschau neu laden.";
        if (stand.KontaktId is null && lage.Kontakt is not { HatAnrede: true } && wahl.Anrede is null)
            return "Bitte die Anrede wählen („keine Anrede“ ist möglich).";
        if (wahl.AktivitaetsartId is not { } art || lage.Aktivitaetsarten.All(x => x.Id != art))
            return "Bitte die Art der Aktivität wählen.";
        if (lage.Benutzer.Count > 0 && (wahl.BenutzerId is not { } benutzer || lage.Benutzer.All(x => x.Id != benutzer)))
            return "Bitte wählen, wem Verkaufschance und Aktivität zugewiesen werden.";
        if (stand.LeadId is null && wahl.LeadId is { } lead)
        {
            if (lage.OffeneLeads(firmaId).All(l => l.Id != lead))
                return "Die gewählte Verkaufschance ist in Odoo nicht (mehr) offen — bitte die Vorschau neu laden.";
        }
        else if (stand.LeadId is null)
        {
            if (wahl.PhaseId is { } phase && lage.Phasen.All(x => x.Id != phase))
                return "Die gewählte Phase gibt es in Odoo nicht (mehr).";
            if (wahl.StichwortIds.Any(s => lage.Stichwoerter.All(x => x.Id != s)))
                return "Ein gewähltes Stichwort gibt es in Odoo nicht (mehr).";
            if (wahl.QuelleId is { } quelle && lage.Quellen.All(x => x.Id != quelle))
                return "Die gewählte Quelle gibt es in Odoo nicht (mehr).";
            if (wahl.MediumId is { } medium && lage.Medien.All(x => x.Id != medium))
                return "Das gewählte Medium gibt es in Odoo nicht (mehr).";
        }
        return null;
    }

    // ---------- Schreiben ----------

    /// <summary>
    /// Führt die offenen Schritte aus. <paramref name="speichern"/> sichert den Stand nach jedem
    /// Schritt. Liefert die Schritte als lesbare Zeilen. Bei einem Fehler: <see cref="OdooSchrittFehler"/>
    /// — der Stand enthält dann, was bis dahin in Odoo steht.
    /// </summary>
    public async Task<List<string>> AusfuehrenAsync(
        Anfrage a, OdooAnlageLage lage, OdooAnlageWahl wahl, AnfrageOdooAnlage stand,
        IReadOnlyDictionary<string, string> kampagnenNamen, DateOnly heute, DateTime jetztUtc, Func<Task> speichern)
    {
        if (WahlFehler(a, lage, wahl, stand) is { } fehler)
            throw new InvalidOperationException(fehler);
        var person = lage.Person;
        var protokoll = new List<string>();
        var hinweise = new List<string>(lage.Hinweise);
        int? anredeId = wahl.Anrede is { } anrede && lage.AnredeIds.TryGetValue(anrede, out var aid) ? aid : null;

        // 1. Firma
        var firma = lage.Firmen.FirstOrDefault(f => f.Firma.Id == (stand.FirmaId ?? wahl.FirmaId))?.Firma;
        if (stand.FirmaId is null)
        {
            if (firma is not null)
            {
                (stand.FirmaId, stand.FirmaNeu) = (firma.Id, false);
            }
            else if (!string.IsNullOrWhiteSpace(a.Firma))
            {
                var werte = new Dictionary<string, object?> { ["name"] = a.Firma.Trim() };
                if (lage.FirmenkennzeichenSetzbar)
                    werte["is_company"] = true;
                if (OdooFirmenRegeln.FirmenDomaene(lage.Email) is { } domaene)
                {
                    werte["website"] = $"https://www.{domaene}";
                    if (wahl.FirmaAnreichern)
                        hinweise.AddRange(await AnreichernAsync(domaene, werte, lage.PartnerFelder));
                }
                stand.FirmaId = await SchrittAsync("Firma anlegen", () => AnlegenAsync("res.partner", werte));
                stand.FirmaNeu = true;
            }
            await speichern();
        }
        var firmaName = firma?.Name ?? a.Firma?.Trim();
        protokoll.Add(stand.FirmaId is { } fid
            ? $"Firma „{firmaName}“ {(stand.FirmaNeu ? "angelegt" : "wiederverwendet")} (Nr. {fid})"
            : "Keine Firma — die Verkaufschance hängt am Kontakt");

        // 2. Kontakt
        if (stand.KontaktId is null)
        {
            if (lage.Kontakt is { } k)
            {
                // Vorhandenen Kontakt nicht überschreiben, nur Lücken füllen
                var werte = new Dictionary<string, object?>();
                if (k.Vorname is null && person.Vorname is not null && lage.PartnerFelder.Contains(VornameFeld))
                    werte[VornameFeld] = person.Vorname;
                if (!k.HatAnrede && anredeId is not null)
                    werte[AnredeFeld] = anredeId;
                if (k.FirmaId is null && stand.FirmaId is not null)
                    werte["parent_id"] = stand.FirmaId;
                if (k.Name.Contains('@') && person.Nachname is not null)
                    werte["name"] = person.NameFeld; // von Odoo aus einer Mail angelegter Kontakt
                if (werte.Count > 0)
                    await SchrittAsync("Kontakt ergänzen", () => SchreibenAsync("res.partner", k.Id, werte));
                (stand.KontaktId, stand.KontaktNeu) = (k.Id, false);
            }
            else
            {
                var werte = new Dictionary<string, object?>
                {
                    ["name"] = person.NameFeld,
                    ["email"] = lage.Email,
                    ["phone"] = (object?)a.Telefon ?? false,
                    ["parent_id"] = (object?)stand.FirmaId ?? false,
                    ["type"] = "contact",
                };
                if (person.Vorname is not null && lage.PartnerFelder.Contains(VornameFeld))
                    werte[VornameFeld] = person.Vorname;
                if (anredeId is not null)
                    werte[AnredeFeld] = anredeId;
                stand.KontaktId = await SchrittAsync("Kontakt anlegen", () => AnlegenAsync("res.partner", werte));
                stand.KontaktNeu = true;
            }
            await speichern();
        }
        var kontaktId = stand.KontaktId!.Value;
        protokoll.Add($"Ansprechpartner „{person.Anzeigename}“ {(stand.KontaktNeu ? "angelegt" : "wiederverwendet")} (Nr. {kontaktId})");

        // 3. Hauptansprechpartner: setzen, wenn die Firma keinen hat; umstellen nur auf ausdrücklichen Wunsch
        if (!stand.HauptansprechpartnerErledigt)
        {
            if (stand.FirmaId is { } firmaId && lage.PartnerFelder.Contains(HauptansprechpartnerFeld))
            {
                var bisher = firma?.HauptansprechpartnerId;
                if (bisher == kontaktId)
                {
                    protokoll.Add("Hauptansprechpartner war bereits diese Person");
                }
                else if (bisher is null || wahl.HauptansprechpartnerUmstellen)
                {
                    await SchrittAsync("Hauptansprechpartner setzen", () => SchreibenAsync("res.partner", firmaId,
                        new() { [HauptansprechpartnerFeld] = kontaktId }));
                    if (bisher is { } alt)
                        await SchrittAsync("Stichwort beim bisherigen Hauptansprechpartner entfernen", () => StichwortEntfernenAsync(alt));
                    protokoll.Add(bisher is null
                        ? "Als Hauptansprechpartner eingetragen"
                        : $"Hauptansprechpartner von „{firma!.HauptansprechpartnerName}“ auf diese Person umgestellt");
                }
                else
                {
                    protokoll.Add($"Hauptansprechpartner bleibt „{firma!.HauptansprechpartnerName}“");
                    hinweise.Add($"Die Firma hat bereits den Hauptansprechpartner „{firma.HauptansprechpartnerName}“ — er wurde nicht geändert.");
                }
            }
            stand.HauptansprechpartnerErledigt = true;
            await speichern();
        }

        // 4. Verkaufschance: neu anlegen oder eine offene ergänzen
        var beschreibung = OdooLeadRegeln.Beschreibung(a, kampagnenNamen);
        if (stand.LeadId is null)
        {
            if (wahl.LeadId is { } vorhandene)
            {
                var lead = lage.OffeneLeads(stand.FirmaId).First(l => l.Id == vorhandene);
                var notiz = $"<p><b>{OdooLeadRegeln.Html($"Website-Anfrage ({a.Formular})")}</b></p>"
                            + $"<p>{OdooLeadRegeln.Html(string.Join('\n', beschreibung.Prepend(KontaktZeile(a, person, lage.Email))))}</p>"
                            + HinweisListe(hinweise);
                await SchrittAsync("Notiz an der Verkaufschance", () => odoo.AusfuehrenAsync("crm.lead", "message_post",
                    [new[] { vorhandene }],
                    new() { ["body"] = notiz, ["body_is_html"] = true, ["message_type"] = "comment", ["subtype_xmlid"] = "mail.mt_note" })
                    .WaitAsync(Wartezeit));
                (stand.LeadId, stand.LeadNeu) = (vorhandene, false);
                await speichern();
                if (lead.PartnerId is null)
                    await SchrittAsync("Verkaufschance dem Kontakt zuordnen", () => SchreibenAsync("crm.lead", vorhandene,
                        new() { ["partner_id"] = stand.FirmaId ?? kontaktId }));
            }
            else
            {
                var werte = LeadWerte(a, lage, wahl, stand, kontaktId, beschreibung, hinweise);
                stand.LeadId = await SchrittAsync("Verkaufschance anlegen", () => AnlegenAsync("crm.lead", werte));
                stand.LeadNeu = true;
                await speichern();
            }
        }
        var leadId = stand.LeadId!.Value;
        protokoll.Add(stand.LeadNeu
            ? $"Verkaufschance „{OdooLeadRegeln.Titel(a, person)}“ angelegt (Nr. {leadId})"
            : $"Vorhandene Verkaufschance Nr. {leadId} um eine Notiz ergänzt");

        // 5. Aktivität — eine gleiche offene wird nicht verdoppelt
        if (!stand.AktivitaetErledigt)
        {
            var artId = wahl.AktivitaetsartId!.Value;
            var text = OdooLeadRegeln.AktivitaetsText(a.Formular);
            var offene = await SchrittAsync("Offene Aktivitäten prüfen", () => SuchenAsync("Aktivitäten", "mail.activity",
                [Bedingung("res_model", "=", "crm.lead"), Bedingung("res_id", "=", leadId), Bedingung("activity_type_id", "=", artId)],
                ["id", "summary"], 1));
            if (offene.Count > 0)
            {
                protokoll.Add("Eine offene Aktivität dieser Art bestand bereits — keine zweite angelegt");
            }
            else
            {
                var kopf = $"<p>{OdooLeadRegeln.Html($"{AnredeText(wahl.Anrede)} {person.Anzeigename}".Trim())}"
                           + $" · <a href=\"{KontaktLink(kontaktId)}\">Kontakt</a>"
                           + (stand.FirmaId is { } f ? $" · <a href=\"{KontaktLink(f)}\">Firma</a> ({(stand.FirmaNeu ? "neu" : "vorhanden")})" : "")
                           + "</p>";
                var notiz = kopf + (HinweisListe(hinweise) is { Length: > 0 } liste
                    ? liste
                    : "<p>In der Kundenzentrale geprüft und angelegt, keine offenen Punkte.</p>");
                var faellig = heute.ToString("yyyy-MM-dd");
                stand.AktivitaetId = await SchrittAsync("Aktivität anlegen", async () =>
                {
                    if (lage.LeadModellId is { } modellId)
                    {
                        var werte = new Dictionary<string, object?>
                        {
                            ["res_model_id"] = modellId, ["res_id"] = leadId, ["activity_type_id"] = artId,
                            ["summary"] = text, ["date_deadline"] = faellig, ["note"] = notiz,
                        };
                        if (wahl.BenutzerId is { } benutzer)
                            werte["user_id"] = benutzer;
                        return await AnlegenAsync("mail.activity", werte);
                    }
                    var kwargs = new Dictionary<string, object>
                    {
                        ["activity_type_id"] = artId, ["summary"] = text, ["date_deadline"] = faellig, ["note"] = notiz, ["automated"] = false,
                    };
                    if (wahl.BenutzerId is { } zugewiesen)
                        kwargs["user_id"] = zugewiesen;
                    return Nummer(await odoo.AusfuehrenAsync("crm.lead", "activity_schedule", [new[] { leadId }], kwargs).WaitAsync(Wartezeit));
                });
                protokoll.Add($"Aktivität „{text}“ angelegt, fällig {heute:dd.MM.yyyy}");
            }
            stand.AktivitaetErledigt = true;
            await speichern();
        }

        stand.Stand = OdooAnlageStand.Fertig;
        stand.AbgeschlossenAm = jetztUtc;
        stand.LetzterFehler = null;
        await speichern();
        return protokoll;
    }

    /// <summary>Werte der neuen Verkaufschance. Enthält nur Felder, die es in dieser Odoo-Datenbank gibt.</summary>
    public static Dictionary<string, object?> LeadWerte(
        Anfrage a, OdooAnlageLage lage, OdooAnlageWahl wahl, AnfrageOdooAnlage stand, int kontaktId,
        IReadOnlyList<string> beschreibung, IReadOnlyList<string> hinweise)
    {
        var person = lage.Person;
        var zeilen = new List<string>();
        if (hinweise.Count > 0)
        {
            zeilen.Add("Bitte prüfen:");
            zeilen.AddRange(hinweise.Select(h => "- " + h));
            zeilen.Add("");
        }
        var werte = new Dictionary<string, object?>
        {
            ["name"] = OdooLeadRegeln.Titel(a, person),
            ["type"] = "opportunity",
            ["contact_name"] = person.Anzeigename,
            // Firma, sonst die Person (ohne Firma)
            ["partner_id"] = stand.FirmaId ?? kontaktId,
        };
        if (!string.IsNullOrWhiteSpace(a.Firma))
            werte["partner_name"] = a.Firma.Trim();
        if (stand.FirmaId is null)
        {
            werte["email_from"] = lage.Email;
            if (a.Telefon is not null)
                werte["phone"] = a.Telefon;
        }
        else
        {
            // Odoo schreibt E-Mail und Telefon der Verkaufschance in den verknüpften Kontakt zurück. Sie
            // hängt an der Firma: Die Personendaten stehen deshalb am Ansprechpartner und in der
            // Beschreibung, die Firma behält ihre allgemeine Adresse.
            zeilen.Add(KontaktZeile(a, person, lage.Email));
        }
        zeilen.AddRange(beschreibung);
        werte["description"] = OdooLeadRegeln.Html(string.Join('\n', zeilen));

        foreach (var (feld, wert) in OdooLeadRegeln.TextFelder(a, person))
            if (lage.LeadFelder.ContainsKey(feld))
                werte[feld] = wert;
        if (lage.LeadFelder.TryGetValue(OdooLeadRegeln.ErsterBesuchFeld, out var typ)
            && OdooLeadRegeln.ErsterBesuch(a, typ) is { } besuch)
            werte[OdooLeadRegeln.ErsterBesuchFeld] = besuch;

        if (wahl.BenutzerId is { } benutzer)
            werte["user_id"] = benutzer;
        if (wahl.PhaseId is { } phase)
            werte["stage_id"] = phase;
        if (wahl.StichwortIds.Count > 0)
            werte["tag_ids"] = new object[] { new object[] { 6, 0, wahl.StichwortIds.Distinct().ToArray() } };
        if (wahl.QuelleId is { } quelle)
            werte["source_id"] = quelle;
        if (wahl.MediumId is { } medium)
            werte["medium_id"] = medium;
        return werte;
    }

    /// <summary>
    /// Lässt Odoo die neue Firma über ihre Domäne anreichern (wie bisher der Workflow) und
    /// übernimmt Adresse, USt-ID, Telefon, allgemeine E-Mail, Branche und Website in
    /// <paramref name="werte"/>. Über die USt-ID setzt Odoo auch das Firmenkennzeichen.
    /// Der Firmenname aus dem Formular bleibt. Liefert die Prüfhinweise; ein Fehler der
    /// Anreicherung hält das Anlegen nicht auf.
    /// </summary>
    private async Task<List<string>> AnreichernAsync(string domaene, Dictionary<string, object?> werte, HashSet<string> partnerFelder)
    {
        JsonElement r;
        try
        {
            r = await odoo.AusfuehrenAsync("res.partner", "enrich_by_domain", [domaene]).WaitAsync(Wartezeit);
        }
        catch (Exception ex) when (ex is OdooFehlerException or HttpRequestException or TimeoutException
                                       or TaskCanceledException or JsonException)
        {
            return [$"Die Anreicherung der Firma über {domaene} ist fehlgeschlagen — Adresse und USt-ID bitte in Odoo nachtragen."];
        }
        // Die Angaben kommen von einem fremden Dienst: nur Text, begrenzte Länge
        string? Wert(string feld) => Text(r, feld) is { } text ? (text.Length <= 200 ? text : text[..200]) : null;
        var fehler = r.ValueKind == JsonValueKind.Object && r.TryGetProperty("error", out var e)
                     && e.ValueKind is not (JsonValueKind.False or JsonValueKind.Null or JsonValueKind.Undefined);
        if (r.ValueKind != JsonValueKind.Object || fehler
            || (Wert("name") is null && Wert("vat") is null && Wert("street") is null && Wert("city") is null))
            return [$"Anreicherung der Firma über {domaene}: kein Treffer — Adresse und USt-ID bitte in Odoo nachtragen."];

        void Setzen(string feld, object? wert)
        {
            if (wert is not null && partnerFelder.Contains(feld))
                werte[feld] = wert;
        }
        // Die Adresse ganz aus der Anreicherung, keine Mischung einzelner Teile
        if (Wert("street") is not null || Wert("zip") is not null || Wert("city") is not null)
        {
            foreach (var feld in (string[])["street", "street2", "zip", "city"])
                Setzen(feld, (object?)Wert(feld) ?? false);
            Setzen("state_id", Verweis(r, "state_id"));
        }
        Setzen("country_id", Verweis(r, "country_id"));
        Setzen("industry_id", Verweis(r, "industry_id"));
        foreach (var feld in (string[])["vat", "phone", "email", "website"])
            Setzen(feld, Wert(feld));

        var hinweise = new List<string>();
        var name = Wert("name");
        if (name is not null && OdooFirmenRegeln.Streng(name) != OdooFirmenRegeln.Streng(werte["name"] as string))
            hinweise.Add($"Die Anreicherung nennt die Firma „{name}“ (Angabe im Formular: „{werte["name"]}“) — Name bitte prüfen.");
        hinweise.Add($"Firma über {domaene} angereichert: „{name ?? "?"}“{(Wert("city") is { } ort ? ", " + ort : "")}" +
                     $"{(Wert("vat") is { } ust ? ", USt-ID " + ust : "")} — bitte prüfen (nicht die Konzernmutter?).");
        return hinweise;
    }

    /// <summary>Nummer eines verknüpften Datensatzes — Odoo liefert sie als Zahl, als [Nummer, Name] oder als Objekt mit „id“.</summary>
    private static int? Verweis(JsonElement r, string feld)
    {
        if (!r.TryGetProperty(feld, out var wert))
            return null;
        return wert.ValueKind switch
        {
            JsonValueKind.Number => wert.GetInt32(),
            JsonValueKind.Array when wert.GetArrayLength() > 0 && wert[0].ValueKind == JsonValueKind.Number => wert[0].GetInt32(),
            JsonValueKind.Object when wert.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number => id.GetInt32(),
            _ => null,
        };
    }

    private static string KontaktZeile(Anfrage a, OdooPerson person, string email) =>
        $"Kontakt: {person.Anzeigename}, {email}{(a.Telefon is not null ? ", " + a.Telefon : "")}";

    private static string AnredeText(Anrede? anrede) => anrede switch
    {
        Anrede.Herr => "Herr",
        Anrede.Frau => "Frau",
        _ => "",
    };

    private static string HinweisListe(IReadOnlyCollection<string> hinweise) => hinweise.Count == 0
        ? ""
        : "<p><b>Bitte prüfen:</b></p><ul>" + string.Concat(hinweise.Select(h => $"<li>{OdooLeadRegeln.Html(h)}</li>")) + "</ul>";

    private async Task<bool> StichwortEntfernenAsync(int kontaktId)
    {
        // Odoos Regel setzt das Stichwort beim neuen Hauptansprechpartner, entfernt es beim alten aber nicht
        var stichwort = await SuchenAsync("Stichwort", "res.partner.category",
            [Bedingung("name", "=ilike", HauptansprechpartnerStichwort)], ["id"], 1);
        if (stichwort.Count == 0)
            return false;
        await SchreibenAsync("res.partner", kontaktId, new()
        {
            ["category_id"] = new object[] { new object[] { 3, stichwort[0].GetProperty("id").GetInt32() } },
        });
        return true;
    }

    private async Task<int> AnlegenAsync(string modell, Dictionary<string, object?> werte) =>
        Nummer(await odoo.AusfuehrenAsync(modell, "create", [werte]).WaitAsync(Wartezeit));

    private async Task<bool> SchreibenAsync(string modell, int id, Dictionary<string, object?> werte)
    {
        await odoo.AusfuehrenAsync(modell, "write", [new[] { id }, werte]).WaitAsync(Wartezeit);
        return true;
    }

    /// <summary>Odoo liefert die Nummer eines neuen Datensatzes als Zahl oder als Liste mit einer Zahl.</summary>
    private static int Nummer(JsonElement ergebnis) => ergebnis.ValueKind switch
    {
        JsonValueKind.Number => ergebnis.GetInt32(),
        JsonValueKind.Array when ergebnis.GetArrayLength() > 0 && ergebnis[0].ValueKind == JsonValueKind.Number => ergebnis[0].GetInt32(),
        _ => throw new JsonException("Odoo hat keine Nummer geliefert."),
    };

    private static async Task<T> SchrittAsync<T>(string schritt, Func<Task<T>> aufruf)
    {
        try
        {
            return await aufruf();
        }
        catch (Exception ex) when (ex is not OdooSchrittFehler)
        {
            throw new OdooSchrittFehler(schritt, ex);
        }
    }
}
