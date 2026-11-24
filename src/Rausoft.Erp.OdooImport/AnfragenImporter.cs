using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Customers;
using Rausoft.Erp.Domain.Shared;
using static Rausoft.Erp.OdooImport.OdooJson;

namespace Rausoft.Erp.OdooImport;

/// <summary>
/// Eine Verkaufschance aus Odoo (crm.lead) mit allem, was für die Übernahme als
/// Website-Anfrage gebraucht wird. Kontaktangaben sind bereits aus Verkaufschance,
/// Ansprechpartner-Feldern und Kontakt zusammengeführt.
/// </summary>
public record AnfrageLead(
    int Id,
    DateTime AngelegtUtc,
    string? Name,
    bool Aktiv,
    /// <summary>Wert des Felds „Elementor-Formularname“ (x_studio_form_source).</summary>
    string? Formular,
    string? Firma,
    string? Kontakt,
    string? Vorname,
    string? Email,
    string? Telefon,
    string? Anliegen,
    string? AnliegenDetail,
    bool Datenschutz,
    /// <summary>Seite, auf der das Formular abgeschickt wurde — daraus nur der Name der Website.</summary>
    string? SeitenUrl,
    /// <summary>Herkunftsfelder mit Inhalt; Schlüssel wie in der Mail der Website (gclid, utm_source …).</summary>
    IReadOnlyDictionary<string, string> Herkunft,
    /// <summary>Die Stufe der Verkaufschance gilt in Odoo als gewonnen (crm.stage.is_won).</summary>
    bool StufeGewonnen,
    /// <summary>Feld won_status neuerer Odoo-Versionen: won, lost oder pending.</summary>
    string? WonStatus,
    string? VerlustGrund);

public enum UebernahmeArt
{
    /// <summary>Noch nicht in der Kundenzentrale — wird angelegt.</summary>
    Neu,
    /// <summary>Von Hand erfasste Anfrage zur selben E-Mail-Adresse — wird mit der Verkaufschance verknüpft.</summary>
    Verknuepft,
    /// <summary>Schon übernommen, der Ausgang hat sich in Odoo geändert.</summary>
    Aktualisiert,
    Unveraendert,
}

public record UebernahmeZeile(Anfrage Anfrage, UebernahmeArt Art);

/// <summary>Ein zusätzliches eigenes Feld an der Verkaufschance, für den Bericht.</summary>
public record ZusatzFeld(string Name, string Bezeichnung, int Gefuellt, string Verwendung, IReadOnlyList<string> Werte);

/// <summary>
/// Regeln für die Übernahme früherer Website-Anfragen aus Odoo (Entscheidung MP 24.11.2026):
/// übernommen wird, was sicher als Website-Anfrage erkennbar ist (Formular oder Herkunft
/// gefüllt), mit derselben lesbaren Herkunft wie bei den von Hand eingefügten Anfragen und
/// zusätzlich dem Ausgang laut Odoo. Klick-Kennungen nur, solange sie jünger als
/// <see cref="AnfrageHerkunft.KlickKennungTage"/> Tage sind.
/// </summary>
public static partial class AnfragenUebernahme
{
    /// <summary>So viele Tage vor dem Anlegen der Verkaufschance darf eine Hand-Anfrage liegen, um als dieselbe zu gelten.</summary>
    public const int HandAnfrageTageVorher = 14;

    public const string FormularFeld = "x_studio_form_source";
    public const string SeitenUrlFeld = "x_studio_page_url";

    /// <summary>Odoo-Feld → Name des Herkunftsfelds, wie die Website ihn in der Mail schickt.</summary>
    public static readonly IReadOnlyDictionary<string, string> HerkunftsFelder = new Dictionary<string, string>
    {
        ["x_studio_gclid"] = "gclid",
        ["x_studio_gbraid"] = "gbraid",
        ["x_studio_wbraid"] = "wbraid",
        ["x_studio_msclkid"] = "msclkid",
        ["x_studio_gad_source"] = "gad_source",
        ["x_studio_gad_campaignid"] = "gad_campaignid",
        ["x_studio_utm_source"] = "utm_source",
        ["x_studio_utm_medium"] = "utm_medium",
        ["x_studio_utm_campaign"] = "utm_campaign",
        ["x_studio_utm_term"] = "utm_term",
        ["x_studio_utm_content"] = "utm_content",
        ["x_studio_doc_referrer"] = "doc_referrer",
        ["x_studio_landing_page"] = "landing_page",
        ["x_studio_first_touch_at"] = "first_touch_at",
        ["x_studio_last_page_before_form"] = "last_page_before_form",
    };

    private static readonly (string Stichwort, string Name)[] Formulare =
    [
        ("Infomappe", "Infomappe"), ("Testversion", "Testversion"), ("Checkliste", "Checkliste"),
        // „Contact From“ ist der (verschriebene) Name des Kontaktformulars in Elementor
        ("Contact", "Kontakt"), ("Kontakt", "Kontakt"),
    ];

    [GeneratedRegex(@"[\p{Cc}\p{Cf}]+")]
    private static partial Regex Steuerzeichen();

    /// <summary>
    /// Die Werte stammen aus einem öffentlichen Formular: Steuerzeichen raus, Länge wie
    /// beim Einfügen von Hand begrenzt.
    /// </summary>
    public static string? Sauber(string? wert)
    {
        if (string.IsNullOrWhiteSpace(wert))
            return null;
        var text = Steuerzeichen().Replace(wert, " ").Trim();
        if (text.Length > WebsiteAnfrageLeser.MaxFeldLaenge)
            text = text[..WebsiteAnfrageLeser.MaxFeldLaenge];
        return text.Length == 0 ? null : text;
    }

    /// <summary>Sicher als Website-Anfrage erkennbar: Formular, Seite oder ein Herkunftsfeld ist gefüllt.</summary>
    public static bool IstWebsiteAnfrage(AnfrageLead l) =>
        l.Formular is not null || l.SeitenUrl is not null || l.Herkunft.Count > 0;

    /// <summary>
    /// Einheitlicher Formularname: „Infomappe_LP1“ → „Infomappe“, „Testversion 3.0“ →
    /// „Testversion“, „Contact From“ → „Kontakt“. Ohne Formularfeld hilft der Name der
    /// Verkaufschance („Infomappe - Firma“).
    /// </summary>
    public static string FormularName(string? feld, string? leadName)
    {
        foreach (var quelle in (string?[])[feld, leadName])
        {
            if (quelle is null)
                continue;
            foreach (var (stichwort, name) in Formulare)
                if (quelle.Contains(stichwort, StringComparison.OrdinalIgnoreCase))
                    return name;
            // Ein gefülltes Formularfeld mit unbekanntem Wert gilt, wie es ist — außer „Unbekanntes Formular“
            if (ReferenceEquals(quelle, feld) && !quelle.Contains("unbekannt", StringComparison.OrdinalIgnoreCase))
                return quelle.Length <= 100 ? quelle : quelle[..100];
        }
        return WebsiteAnfrageLeser.FormularUnbekannt;
    }

    /// <summary>
    /// Ausgang laut Odoo: gewonnen über die Stufe, verloren = archiviert (so markiert Odoo
    /// verlorene Verkaufschancen); neuere Versionen sagen es im Feld won_status direkt.
    /// </summary>
    public static AnfrageErgebnis Ergebnis(AnfrageLead l) => l.WonStatus?.ToLowerInvariant() switch
    {
        "won" => AnfrageErgebnis.Gewonnen,
        "lost" => AnfrageErgebnis.Verloren,
        _ => l.StufeGewonnen ? AnfrageErgebnis.Gewonnen
            : !l.Aktiv ? AnfrageErgebnis.Verloren
            : AnfrageErgebnis.Offen,
    };

    /// <summary>„Max Muster &lt;max@firma.example&gt;“ → „max@firma.example“; Ungültiges wird verworfen.</summary>
    public static string? EmailAdresse(string? roh)
    {
        var erste = roh?.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return erste is not null && MailAddress.TryCreate(erste, out var adresse) && adresse.Host.Contains('.')
            ? adresse.Address
            : null;
    }

    /// <summary>Vor- und Nachname aus dem Vornamen-Feld und dem vollen Kontaktnamen.</summary>
    public static (string? Vorname, string? Nachname) Namen(string? vorname, string? kontakt)
    {
        if (kontakt is null)
            return (vorname, null);
        if (vorname is not null)
        {
            if (kontakt.Equals(vorname, StringComparison.OrdinalIgnoreCase))
                return (vorname, null);
            return kontakt.StartsWith(vorname + " ", StringComparison.OrdinalIgnoreCase)
                ? (vorname, kontakt[(vorname.Length + 1)..].Trim())
                : (vorname, kontakt);
        }
        var teile = kontakt.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return teile.Length switch
        {
            0 => (null, null),
            1 => (null, teile[0]),
            _ => (string.Join(' ', teile[..^1]), teile[^1]),
        };
    }

    public static string? Website(string? seitenUrl)
    {
        if (!Uri.TryCreate(seitenUrl, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
            return null;
        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.") ? host[4..] : host;
    }

    /// <summary>Baut die Anfrage aus der Verkaufschance — über denselben Weg wie beim Einfügen einer Mail.</summary>
    public static Anfrage Aus(AnfrageLead l, DateTime jetztUtc)
    {
        var (vorname, nachname) = Namen(l.Vorname, l.Kontakt);
        var formular = FormularName(l.Formular, l.Name);
        // Ein Anliegen, das nur das Formular wiederholt („infomappe“), ist keine Nachricht
        var anliegen = l.Anliegen is { } a && !formular.Contains(a, StringComparison.OrdinalIgnoreCase)
                       && !a.Contains(formular, StringComparison.OrdinalIgnoreCase) ? a : null;
        var gelesen = new WebsiteAnfrage
        {
            Formular = formular,
            Website = Website(l.SeitenUrl),
            Firma = l.Firma,
            Vorname = vorname,
            Nachname = nachname,
            EMail = EmailAdresse(l.Email),
            Telefon = l.Telefon,
            Nachricht = anliegen is not null && l.AnliegenDetail is not null
                ? $"{anliegen}: {l.AnliegenDetail}"
                : l.AnliegenDetail ?? anliegen,
            DatenschutzBestaetigt = l.Datenschutz,
            Herkunft = l.Herkunft.Select(h => new WebsiteAnfrageFeld(h.Key, h.Value)).ToList(),
        };

        var anfrage = AnfrageHerkunft.Uebernehmen(gelesen, jetztUtc);
        anfrage.EingegangenAm = l.AngelegtUtc;
        anfrage.OdooLeadId = l.Id;
        anfrage.Ergebnis = Ergebnis(l);
        anfrage.VerlustGrund = VerlustGrund(l, anfrage.Ergebnis);
        // Abgelaufene Klick-Kennungen gar nicht erst übernehmen; „über Anzeige“ bleibt bekannt
        if (l.AngelegtUtc < jetztUtc.AddDays(-AnfrageHerkunft.KlickKennungTage))
            anfrage.Gclid = anfrage.Gbraid = anfrage.Wbraid = anfrage.Msclkid = null;
        return anfrage;
    }

    public static string? VerlustGrund(AnfrageLead l, AnfrageErgebnis ergebnis) =>
        ergebnis == AnfrageErgebnis.Verloren && l.VerlustGrund is { } grund
            ? (grund.Length <= 200 ? grund : grund[..200])
            : null;

    /// <summary>
    /// Von Hand erfasste Anfrage, die zu dieser Verkaufschance gehört: gleiche E-Mail-Adresse,
    /// noch ohne Odoo-Verknüpfung, eingegangen bis zu <see cref="HandAnfrageTageVorher"/> Tage
    /// vor dem Anlegen in Odoo (der Mitarbeiter legt die Verkaufschance nach der Mail an).
    /// </summary>
    public static Anfrage? PassendeHandAnfrage(IEnumerable<Anfrage> handAnfragen, string? email, DateTime angelegtUtc)
    {
        if (email is null)
            return null;
        return handAnfragen
            .Where(a => a.OdooLeadId is null
                        && string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase)
                        && a.EingegangenAm >= angelegtUtc.AddDays(-HandAnfrageTageVorher)
                        && a.EingegangenAm <= angelegtUtc.AddDays(1))
            .OrderBy(a => Math.Abs((a.EingegangenAm - angelegtUtc).Ticks))
            .FirstOrDefault();
    }

    /// <summary>Bericht des Laufs — nur Zahlen, Formular-, Kanal- und Feldnamen, keine Personen.</summary>
    public static string Bericht(
        IReadOnlyCollection<UebernahmeZeile> zeilen, int gelesen, IReadOnlyCollection<ZusatzFeld> zusatzFelder,
        bool probelauf, DateTime heute)
    {
        var kultur = CultureInfo.GetCultureInfo("de-DE");
        var b = new StringBuilder();
        void Zeile(string text = "") => b.Append(text).Append('\n');
        int Anzahl(UebernahmeArt art) => zeilen.Count(z => z.Art == art);

        Zeile(probelauf
            ? "=== WEBSITE-ANFRAGEN AUS ODOO ÜBERNEHMEN (PROBELAUF — es wird nichts gespeichert) ==="
            : "=== WEBSITE-ANFRAGEN AUS ODOO ÜBERNEHMEN ===");
        Zeile($"Stand {heute.ToString("dd.MM.yyyy HH:mm", kultur)} · Verkaufschancen gelesen: {gelesen} · davon Website-Anfragen: {zeilen.Count}");
        Zeile();
        if (zeilen.Count == 0)
        {
            Zeile("Keine Verkaufschance mit Formular oder Herkunft gefunden.");
            return b.ToString();
        }

        Zeile(probelauf ? "Das würde der Lauf tun" : "Das hat der Lauf getan");
        Zeile($"  {(probelauf ? "neu anlegen" : "neu angelegt"),-44}{Anzahl(UebernahmeArt.Neu),5}");
        Zeile($"  {(probelauf ? "mit Hand-Anfrage verknüpfen" : "mit Hand-Anfrage verknüpft"),-44}{Anzahl(UebernahmeArt.Verknuepft),5}");
        Zeile($"  {(probelauf ? "schon übernommen, Ausgang aktualisieren" : "schon übernommen, Ausgang aktualisiert"),-44}{Anzahl(UebernahmeArt.Aktualisiert),5}");
        Zeile($"  {"schon übernommen, unverändert",-44}{Anzahl(UebernahmeArt.Unveraendert),5}");
        Zeile();

        var anfragen = zeilen.Select(z => z.Anfrage).ToList();
        var neue = zeilen.Where(z => z.Art == UebernahmeArt.Neu).Select(z => z.Anfrage).ToList();
        Zeile("Alle Website-Anfragen aus Odoo");
        Zeile($"  Zeitraum: {Ortszeit.VonUtc(anfragen.Min(a => a.EingegangenAm)):dd.MM.yyyy} bis {Ortszeit.VonUtc(anfragen.Max(a => a.EingegangenAm)):dd.MM.yyyy}");
        Zeile($"  einem Kunden zugeordnet: {anfragen.Count(a => a.KundeId is not null)} · ohne Kunde: {anfragen.Count(a => a.KundeId is null)}" +
              " (lassen sich unter „Anfragen“ nachträglich zuordnen)");
        Zeile($"  mit E-Mail-Adresse: {anfragen.Count(a => a.Email is not null)} · mit Firma: {anfragen.Count(a => a.Firma is not null)}" +
              $" · mit Ansprechpartner: {anfragen.Count(a => a.Kontaktname() is not null)}");
        Zeile($"  neue Anfragen mit Klick-Kennung (jünger als {AnfrageHerkunft.KlickKennungTage} Tage): {neue.Count(a => a.HatKlickKennung())}");
        Zeile();

        Zeile("Formulare");
        foreach (var g in anfragen.GroupBy(a => a.Formular).OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
            Zeile($"  {g.Count(),6}  {g.Key}");
        Zeile();

        Zeile("Kanal und Ausgang laut Odoo");
        Zeile($"  {"Kanal",-34}{"gesamt",8}{"offen",8}{"gewonnen",10}{"verloren",10}");
        void Ausgang(string titel, IReadOnlyCollection<Anfrage> gruppe) =>
            Zeile($"  {(titel.Length > 33 ? titel[..32] + "…" : titel),-34}{gruppe.Count,8}" +
                  $"{gruppe.Count(a => a.Ergebnis == AnfrageErgebnis.Offen),8}" +
                  $"{gruppe.Count(a => a.Ergebnis == AnfrageErgebnis.Gewonnen),10}" +
                  $"{gruppe.Count(a => a.Ergebnis == AnfrageErgebnis.Verloren),10}");
        foreach (var g in anfragen.GroupBy(a => AnfrageHerkunft.Kanal(a)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
            Ausgang(g.Key, g.ToList());
        Ausgang("Summe", anfragen);
        Zeile();

        var gruende = anfragen.Where(a => a.Ergebnis == AnfrageErgebnis.Verloren)
            .GroupBy(a => a.VerlustGrund ?? "(kein Grund gepflegt)")
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();
        if (gruende.Count > 0)
        {
            Zeile("Verlustgründe");
            foreach (var g in gruende.Take(15))
                Zeile($"  {g.Count(),6}  {g.Key}");
            Zeile();
        }

        if (zusatzFelder.Count > 0)
        {
            Zeile("Weitere eigene Felder in Odoo (gefüllt bei so vielen Website-Anfragen)");
            foreach (var f in zusatzFelder.OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                Zeile($"  {f.Name,-44}{f.Gefuellt,5}  {f.Bezeichnung} — {f.Verwendung}");
                foreach (var wert in f.Werte)
                    Zeile($"      {wert}");
            }
        }
        return b.ToString();
    }
}

/// <summary>
/// Modus „anfragen-import“: übernimmt die als Website-Anfrage erkennbaren Verkaufschancen
/// aus Odoo (nur lesen) in die Tabelle der Anfragen. Wiederholbar über die Nummer der
/// Verkaufschance: schon Übernommenes wird nicht doppelt angelegt, nur der Ausgang
/// (offen/gewonnen/verloren) wird nachgezogen. Mit <c>dryRun</c> wird nichts gespeichert.
/// </summary>
public class AnfragenImporter(OdooClient odoo, bool dryRun, string? verbindung = null, TextWriter? ausgabe = null)
{
    private const int Seitengroesse = 500;
    private readonly TextWriter _aus = ausgabe ?? Console.Out;

    /// <summary>Eigene Felder, deren Inhalt der Lauf nutzt bzw. für den Bericht zählt: Feld → Verwendung.</summary>
    private static readonly Dictionary<string, string> ZusatzFelder = new()
    {
        ["x_studio_vorname"] = "übernommen als Vorname",
        ["x_studio_ap_name"] = "übernommen, wenn der Kontaktname fehlt",
        ["x_studio_ap_e_mail"] = "übernommen, wenn die E-Mail-Adresse fehlt",
        ["x_studio_ap_terlefon"] = "übernommen, wenn die Telefonnummer fehlt",
        ["x_studio_intent"] = "übernommen in die Nachricht",
        ["x_studio_intent_detail"] = "übernommen in die Nachricht",
        ["x_studio_stimmt_den_datenschutzerklrung_zu"] = "übernommen als Datenschutz-Zustimmung",
        ["x_studio_msclkid"] = $"übernommen als Klick-Kennung ({AnfrageHerkunft.KlickKennungTage} Tage)",
        ["x_studio_channel"] = "nicht übernommen (nur gezählt)",
    };

    /// <summary>Felder mit Auswahlwerten, deren häufigste Werte der Bericht nennt (keine Freitexte).</summary>
    private static readonly string[] WerteZeigen = ["x_studio_channel", "x_studio_intent"];

    public async Task AusfuehrenAsync()
    {
        var jetztUtc = DateTime.UtcNow;

        // Welche Felder gibt es überhaupt? Ein unbekanntes Feld in der Abfrage wäre ein Odoo-Fehler.
        var felder = await odoo.AusfuehrenAsync("crm.lead", "fields_get", [],
            new Dictionary<string, object> { ["attributes"] = new[] { "string", "type" } });
        var alle = felder.ValueKind == JsonValueKind.Object
            ? felder.EnumerateObject().ToDictionary(f => f.Name, f => Text(f.Value, "string") ?? f.Name)
            : [];

        string[] gewuenscht =
        [
            "create_date", "active", "name", "partner_id", "partner_name", "contact_name", "email_from", "phone", "mobile",
            "stage_id", "won_status", "lost_reason_id",
            AnfragenUebernahme.FormularFeld, AnfragenUebernahme.SeitenUrlFeld,
            .. AnfragenUebernahme.HerkunftsFelder.Keys, .. ZusatzFelder.Keys,
        ];
        var lesen = gewuenscht.Where(alle.ContainsKey).Distinct().ToArray();

        var leads = new List<JsonElement>();
        for (var versatz = 0; ; versatz += Seitengroesse)
        {
            var seite = await odoo.AusfuehrenAsync("crm.lead", "search_read", [Array.Empty<object>()],
                new Dictionary<string, object>
                {
                    ["fields"] = lesen,
                    ["limit"] = Seitengroesse,
                    ["offset"] = versatz,
                    ["order"] = "id asc",
                    // verlorene Verkaufschancen sind in Odoo archiviert — sie gehören dazu
                    ["context"] = new Dictionary<string, object> { ["active_test"] = false },
                });
            var anzahl = seite.GetArrayLength();
            leads.AddRange(seite.EnumerateArray());
            if (anzahl < Seitengroesse)
                break;
        }

        var gewonneneStufen = await GewonneneStufenAsync();
        var partner = await PartnerAsync(leads.Select(l => Many2OneId(l, "partner_id")).OfType<int>().Distinct().ToList());

        // Verkaufschance → Rohdaten; nur was sicher eine Website-Anfrage ist
        var kandidaten = new List<(AnfrageLead Lead, JsonElement Roh, PartnerDaten? Partner)>();
        foreach (var roh in leads)
        {
            if (ZeitpunktUtc(roh, "create_date") is not { } angelegt)
                continue;
            var kontakt = Many2OneId(roh, "partner_id") is { } pid ? partner.GetValueOrDefault(pid) : null;
            var lead = LeadAus(roh, angelegt, kontakt, gewonneneStufen);
            if (AnfragenUebernahme.IstWebsiteAnfrage(lead))
                kandidaten.Add((lead, roh, kontakt));
        }

        var options = new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(ImportUmgebung.Verbindung(verbindung)).Options;
        await using var db = new ErpDbContext(options);

        // Kunde über die Firma des Odoo-Kontakts, ersatzweise über den Ansprechpartner
        var firmenIds = kandidaten.Select(k => k.Partner?.FirmaId).OfType<int>().Distinct().ToList();
        var kundeJeFirma = await db.Kunden.AsNoTracking()
            .Where(k => k.OdooId != null && firmenIds.Contains(k.OdooId.Value))
            .Select(k => new { OdooId = k.OdooId!.Value, k.Id })
            .ToDictionaryAsync(k => k.OdooId, k => k.Id);
        var personenIds = kandidaten.Select(k => k.Partner?.Id).OfType<int>().Distinct().ToList();
        var kundeJePerson = (await db.Ansprechpartner.AsNoTracking()
                .Where(a => a.OdooId != null && personenIds.Contains(a.OdooId.Value))
                .Select(a => new { OdooId = a.OdooId!.Value, a.KundeId })
                .ToListAsync())
            .GroupBy(a => a.OdooId).ToDictionary(g => g.Key, g => g.First().KundeId);

        var vorhandene = await db.Anfragen.ToListAsync();
        var jeLead = vorhandene.Where(a => a.OdooLeadId is not null).ToDictionary(a => a.OdooLeadId!.Value);
        var handAnfragen = vorhandene.Where(a => a.OdooLeadId is null).ToList();

        var zeilen = new List<UebernahmeZeile>();
        var neue = new List<Anfrage>();
        foreach (var (lead, _, kontakt) in kandidaten)
        {
            var ergebnis = AnfragenUebernahme.Ergebnis(lead);
            var grund = AnfragenUebernahme.VerlustGrund(lead, ergebnis);

            if (jeLead.TryGetValue(lead.Id, out var bekannt))
            {
                var geaendert = bekannt.Ergebnis != ergebnis || bekannt.VerlustGrund != grund;
                (bekannt.Ergebnis, bekannt.VerlustGrund) = (ergebnis, grund);
                zeilen.Add(new(bekannt, geaendert ? UebernahmeArt.Aktualisiert : UebernahmeArt.Unveraendert));
                continue;
            }

            var email = AnfragenUebernahme.EmailAdresse(lead.Email);
            if (AnfragenUebernahme.PassendeHandAnfrage(handAnfragen, email, lead.AngelegtUtc) is { } hand)
            {
                (hand.OdooLeadId, hand.Ergebnis, hand.VerlustGrund) = (lead.Id, ergebnis, grund);
                zeilen.Add(new(hand, UebernahmeArt.Verknuepft));
                continue;
            }

            var anfrage = AnfragenUebernahme.Aus(lead, jetztUtc);
            anfrage.KundeId = kontakt is null ? null
                : kontakt.FirmaId is { } firma && kundeJeFirma.TryGetValue(firma, out var ueberFirma) ? ueberFirma
                : kundeJePerson.TryGetValue(kontakt.Id, out var ueberPerson) ? ueberPerson
                : null;
            neue.Add(anfrage);
            zeilen.Add(new(anfrage, UebernahmeArt.Neu));
        }

        if (!dryRun)
        {
            var namen = AnfrageHerkunft.KampagnenNamen(await db.Einstellungen.AsNoTracking()
                .Where(e => e.Schluessel == EinstellungsSchluessel.AnfragenKampagnen)
                .Select(e => e.Wert).FirstOrDefaultAsync());
            await using var transaktion = await db.Database.BeginTransactionAsync();
            db.Anfragen.AddRange(neue);
            await db.SaveChangesAsync();
            // Der Verlaufseintrag braucht die Id der Anfrage — deshalb in zwei Schritten
            db.Aktivitaeten.AddRange(neue.Where(a => a.KundeId is not null).Select(a => AnfrageHerkunft.VerlaufsEintrag(a, namen)));
            await db.SaveChangesAsync();
            await transaktion.CommitAsync();
        }

        var zusatz = ZusatzFelder.Where(f => alle.ContainsKey(f.Key))
            .Select(f => new ZusatzFeld(f.Key, alle[f.Key], kandidaten.Count(k => Wert(k.Roh, f.Key) is not null), f.Value,
                WerteZeigen.Contains(f.Key)
                    ? kandidaten.Select(k => Wert(k.Roh, f.Key)).OfType<string>()
                        .GroupBy(w => w, StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Take(10)
                        .Select(g => $"{g.Count(),4}  {(g.Key.Length > 60 ? g.Key[..60] + "…" : g.Key)}").ToList()
                    : []))
            .ToList();
        _aus.Write(AnfragenUebernahme.Bericht(zeilen, leads.Count, zusatz, dryRun, DateTime.Now));
    }

    private sealed record PartnerDaten(int Id, string? Name, bool IstFirma, int? FirmaId, string? FirmaName, string? Email, string? Telefon);

    private async Task<HashSet<int>> GewonneneStufenAsync()
    {
        try
        {
            var stufen = await odoo.AusfuehrenAsync("crm.stage", "search_read", [Array.Empty<object>()],
                new Dictionary<string, object> { ["fields"] = new[] { "id", "is_won" } });
            return stufen.EnumerateArray().Where(s => Wahr(s, "is_won")).Select(s => s.GetProperty("id").GetInt32()).ToHashSet();
        }
        catch (InvalidOperationException)
        {
            // Ohne das Feld bleibt nur „offen“ oder „verloren“
            return [];
        }
    }

    private async Task<Dictionary<int, PartnerDaten>> PartnerAsync(List<int> ids)
    {
        var ergebnis = new Dictionary<int, PartnerDaten>();
        foreach (var teil in ids.Chunk(Seitengroesse))
        {
            var seite = await odoo.AusfuehrenAsync("res.partner", "search_read",
                [new object[] { new object[] { "id", "in", teil } }],
                new Dictionary<string, object>
                {
                    ["fields"] = new[] { "id", "name", "is_company", "commercial_partner_id", "email", "phone" },
                    ["context"] = new Dictionary<string, object> { ["active_test"] = false },
                });
            foreach (var p in seite.EnumerateArray())
            {
                var id = p.GetProperty("id").GetInt32();
                var istFirma = Wahr(p, "is_company");
                var firmaId = Many2OneId(p, "commercial_partner_id") ?? (istFirma ? id : null);
                // Eine Privatperson ohne Firma ist ihr eigener „commercial partner“ — das ist keine Firma
                var firmaName = istFirma ? Text(p, "name") : firmaId != id ? Many2OneLabel(p, "commercial_partner_id") : null;
                ergebnis[id] = new PartnerDaten(id, Text(p, "name"), istFirma, firmaId, firmaName, Text(p, "email"), Text(p, "phone"));
            }
        }
        return ergebnis;
    }

    private static AnfrageLead LeadAus(JsonElement roh, DateTime angelegtUtc, PartnerDaten? kontakt, HashSet<int> gewonneneStufen)
    {
        string? Feld(string name) => AnfragenUebernahme.Sauber(Wert(roh, name));

        var herkunft = new Dictionary<string, string>();
        foreach (var (odooFeld, name) in AnfragenUebernahme.HerkunftsFelder)
            if (Feld(odooFeld) is { } wert)
                herkunft[name] = wert;

        return new AnfrageLead(
            roh.GetProperty("id").GetInt32(),
            angelegtUtc,
            Feld("name"),
            Wahr(roh, "active"),
            Feld(AnfragenUebernahme.FormularFeld),
            Feld("partner_name") ?? AnfragenUebernahme.Sauber(kontakt?.FirmaName),
            Feld("contact_name") ?? Feld("x_studio_ap_name")
                ?? (kontakt is { IstFirma: false } ? AnfragenUebernahme.Sauber(kontakt.Name) : null),
            Feld("x_studio_vorname"),
            Feld("email_from") ?? Feld("x_studio_ap_e_mail") ?? AnfragenUebernahme.Sauber(kontakt?.Email),
            Feld("phone") ?? Feld("mobile") ?? Feld("x_studio_ap_terlefon") ?? AnfragenUebernahme.Sauber(kontakt?.Telefon),
            Feld("x_studio_intent"),
            Feld("x_studio_intent_detail"),
            Zugestimmt(roh, "x_studio_stimmt_den_datenschutzerklrung_zu"),
            Feld(AnfragenUebernahme.SeitenUrlFeld),
            herkunft,
            Many2OneId(roh, "stage_id") is { } stufe && gewonneneStufen.Contains(stufe),
            Feld("won_status"),
            AnfragenUebernahme.Sauber(Many2OneLabel(roh, "lost_reason_id")));
    }

    /// <summary>
    /// Feldwert als Text, gleich welchen Typ das Feld in Odoo hat (Text, Auswahl, Zahl,
    /// Verweis); leere Felder liefert Odoo als <c>false</c>.
    /// </summary>
    private static string? Wert(JsonElement lead, string feld)
    {
        if (!lead.TryGetProperty(feld, out var wert))
            return null;
        return wert.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(wert.GetString()) ? null : wert.GetString()!.Trim(),
            JsonValueKind.Number => wert.GetRawText(),
            JsonValueKind.Array when wert.GetArrayLength() > 1 && wert[1].ValueKind == JsonValueKind.String => wert[1].GetString(),
            JsonValueKind.True => "ja",
            _ => null,
        };
    }

    private static bool Zugestimmt(JsonElement lead, string feld) =>
        Wert(lead, feld) is { } wert
        && !wert.StartsWith('n') && !wert.StartsWith('N')
        && wert is not ("0" or "false" or "False" or "off");
}
