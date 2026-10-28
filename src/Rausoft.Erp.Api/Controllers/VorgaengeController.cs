using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Domain.Vorgaenge;
using Rausoft.Erp.Dtos;
using Rausoft.Erp.Api.Dienste;
using static Rausoft.Erp.Api.Dienste.VorgangsHilfen;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Vorgänge: Bestellung → Teilaufgaben je Bereich (ANFORDERUNGEN.md, „Vorgänge").
/// Übersicht, Anlegen mit Anpassung im Einzelfall, Arbeit an den Schritten.
/// Jede Änderung schreibt einen Eintrag in den Verlauf des Kunden (Strang des
/// Vorgangs, Verweis auf den Vorgang). Zugriff: jeder aktive Mitarbeiter.
/// </summary>
[ApiController]
[Route("api/vorgaenge")]
public partial class VorgaengeController(ErpDbContext db, IAktuellerBenutzer benutzer) : ControllerBase
{
    private const int MaxManuelleSchritte = 20;
    private const int AbgeschlosseneTage = 90;

    private static DateOnly Heute => DateOnly.FromDateTime(DateTime.Now);

    // --- Übersicht -----------------------------------------------------------------

    [HttpGet]
    public async Task<ActionResult<VorgangUebersicht>> Uebersicht(
        [FromQuery] bool abgeschlossene = false, [FromQuery] int? kundeId = null, CancellationToken ct = default)
    {
        var grenze = DateTime.UtcNow.AddDays(-AbgeschlosseneTage);
        var abfrage = db.Vorgaenge.AsNoTracking();
        // Kundenakte: alle Vorgänge des Kunden; Übersicht: laufende und auf Wunsch die zuletzt abgeschlossenen
        abfrage = kundeId is { } k
            ? abfrage.Where(v => v.KundeId == k)
            : abfrage.Where(v => v.AbgeschlossenAm == null || (abgeschlossene && v.AbgeschlossenAm >= grenze));
        var vorgaenge = await abfrage
            .Include(v => v.Kunde)
            .Include(v => v.Vorgangstyp)
            .Include(v => v.Schritte).ThenInclude(s => s.ZustaendigBereich)
            .Include(v => v.Schritte).ThenInclude(s => s.Zustaendig)
            .AsSplitQuery()
            .ToListAsync(ct);

        var ich = await IchAsync(db, benutzer, ct);
        var meinBereich = ich is null ? null
            : await db.Mitarbeiter.Where(m => m.Id == ich).Select(m => m.BereichId).FirstOrDefaultAsync(ct);
        var heute = Heute;

        var zeilen = vorgaenge.Select(v =>
        {
            Verknuepfen(v.Schritte);
            var status = VorgangsLogik.Status(v.Schritte, heute);
            var gezaehlt = v.Schritte.Where(s => s.Status != AufgabenStatus.Entfallen).ToList();
            var aktiv = v.Schritte.Where(VorgangsLogik.Aktiv)
                .OrderBy(s => VorgangsLogik.Ueberfaellig(s, heute) ? 0 : VorgangsLogik.WartetAufKunden(s, heute) ? 2 : 1)
                .ThenBy(s => VorgangsLogik.FaelligAm(s) ?? DateOnly.MaxValue)
                .ThenBy(s => s.Reihenfolge)
                .ToList();
            var n = aktiv.FirstOrDefault();
            var ichDran = aktiv.Any(s => (ich is not null && s.ZustaendigId == ich)
                                         || (s.ZustaendigId is null && meinBereich is not null && s.ZustaendigBereichId == meinBereich));
            return (Status: status, Zeile: new VorgangZeile(
                v.Id, v.KundeId, v.Kunde!.Firmenname, v.Kunde.Kundennummer, v.Vorgangstyp!.Name, v.Titel, v.OdooAuftrag,
                v.Hinweis, v.Start, StatusText(status),
                gezaehlt.Count(s => s.Status == AufgabenStatus.Erledigt), gezaehlt.Count,
                n?.Titel, n?.ZustaendigBereich?.Name, n?.Zustaendig?.Anzeigename,
                n is null ? null : VorgangsLogik.FaelligAm(n), n is not null && VorgangsLogik.Ueberfaellig(n, heute),
                aktiv.Where(s => s.ZustaendigBereichId is not null).Select(s => s.ZustaendigBereichId!.Value).Distinct().ToList(),
                ichDran, v.AbgeschlossenAm));
        })
            .OrderBy(x => Prioritaet(x.Status))
            .ThenByDescending(x => x.Zeile.AbgeschlossenAm)
            .ThenBy(x => x.Zeile.Start)
            .Select(x => x.Zeile)
            .ToList();

        return Ok(new VorgangUebersicht(zeilen, await BereicheAsync(ct), ich, meinBereich,
            await DarfTypenPflegenAsync(db, ich, ct)));
    }

    /// <summary>Abgleich mit Odoo auf Knopfdruck (läuft sonst stündlich): Strang nachziehen, Rechnungen abhaken.</summary>
    [HttpPost("abgleich")]
    public async Task<ActionResult<VorgangsAbgleichErgebnis>> Abgleichen(
        [FromServices] VorgangsAbgleichDienst abgleich, CancellationToken ct) =>
        Ok(await abgleich.AbgleichenAsync(ct));

    // --- Einzelansicht -------------------------------------------------------------

    [HttpGet("{id:int}")]
    public async Task<ActionResult<VorgangDetail>> Detail(int id, CancellationToken ct)
    {
        var v = await db.Vorgaenge.AsNoTracking()
            .Include(x => x.Kunde)
            .Include(x => x.Vorgangstyp)
            .Include(x => x.ErstelltVon)
            .Include(x => x.Schritte).ThenInclude(s => s.ZustaendigBereich)
            .Include(x => x.Schritte).ThenInclude(s => s.Zustaendig)
            .Include(x => x.Schritte).ThenInclude(s => s.ErledigtVon)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (v is null)
            return NotFound();
        Verknuepfen(v.Schritte);
        var heute = Heute;

        var verlauf = await db.Aktivitaeten.AsNoTracking()
            .Where(a => a.VerweisTyp == AktivitaetsVerweisTyp.Vorgang && a.VerweisId == id)
            .OrderByDescending(a => a.Zeitpunkt).ThenByDescending(a => a.Id)
            .Take(200)
            .Select(a => new VorgangVerlaufEintrag(a.Zeitpunkt, a.Mitarbeiter != null ? a.Mitarbeiter.Anzeigename : a.AutorName, a.Betreff, a.Inhalt))
            .ToListAsync(ct);
        var mitarbeiter = await db.Mitarbeiter.AsNoTracking().Where(m => m.Aktiv).OrderBy(m => m.Anzeigename)
            .Select(m => new AuswahlEintrag(m.Id, m.Anzeigename, m.Bereich != null ? m.Bereich.Name : null)).ToListAsync(ct);

        return Ok(new VorgangDetail(
            v.Id, v.KundeId, v.Kunde!.Firmenname, v.Kunde.Kundennummer, v.VorgangstypId, v.Vorgangstyp!.Name, v.VorgangstypStand,
            v.Titel, v.OdooAuftrag, v.Hinweis, v.Start, StatusText(VorgangsLogik.Status(v.Schritte, heute)),
            v.ErstelltAm, v.ErstelltVon?.Anzeigename, v.AbgeschlossenAm, v.StrangName,
            v.Schritte.OrderBy(s => s.Reihenfolge).Select(s => SchrittDto(s, heute)).ToList(),
            verlauf, await BereicheAsync(ct), mitarbeiter, await IchAsync(db, benutzer, ct)));
    }

    private static VorgangSchritt SchrittDto(Aufgabe s, DateOnly heute) => new(
        s.Id, s.Reihenfolge ?? 0, s.Titel, s.ZustaendigBereichId, s.ZustaendigBereich?.Name, s.ZustaendigId, s.Zustaendig?.Anzeigename,
        SchrittStatus(s, heute), VorgangsLogik.FaelligAm(s), VorgangsLogik.Ueberfaellig(s, heute), s.Wiedervorlage,
        s.WartetAufId, s.WartetAuf?.Titel, s.Herkunft == SchrittHerkunft.Manuell, s.EntfallenGrund, s.Pflichtfeld, s.PflichtfeldWert,
        s.Benachrichtigung.ToString(), s.AutoAbhaken.ToString(), s.FristTage ?? 0, s.ErledigtAm, s.ErledigtVon?.Anzeigename);

    // --- Anlegen -------------------------------------------------------------------

    [HttpPost]
    public async Task<ActionResult<int>> Anlegen(VorgangAnlegen anlage, CancellationToken ct)
    {
        var kunde = await db.Kunden.AsNoTracking().FirstOrDefaultAsync(k => k.Id == anlage.KundeId, ct);
        if (kunde is null)
            return BadRequest("Bitte einen Kunden wählen.");
        var typ = await db.Vorgangstypen.AsNoTracking().Include(t => t.Schritte)
            .FirstOrDefaultAsync(t => t.Id == anlage.VorgangstypId, ct);
        if (typ is null || !typ.Aktiv)
            return BadRequest("Der Vorgangstyp existiert nicht oder ist deaktiviert.");

        if (!OdooNummerPruefen(anlage.OdooAuftrag, out var odoo))
            return BadRequest("Die Odoo-Auftragsnummer darf nur Buchstaben, Ziffern, „/“ und „-“ enthalten (höchstens 30 Zeichen), z. B. S01241.");
        if (odoo is not null)
        {
            var laufend = await db.Vorgaenge.AsNoTracking()
                .Where(v => v.OdooAuftrag == odoo && v.AbgeschlossenAm == null)
                .Select(v => new { v.Id, v.Titel }).FirstOrDefaultAsync(ct);
            if (laufend is not null)
                return BadRequest($"Zum Auftrag {odoo} läuft schon der Vorgang „{laufend.Titel}“.");
        }
        var hinweis = string.IsNullOrWhiteSpace(anlage.Hinweis) ? null : anlage.Hinweis.Trim();
        if (hinweis is { Length: > 1000 })
            return BadRequest("Der Hinweis darf höchstens 1.000 Zeichen lang sein.");
        var start = anlage.Start ?? Heute;
        if (Math.Abs(start.DayNumber - Heute.DayNumber) > 366)
            return BadRequest("Der Start muss innerhalb eines Jahres um heute liegen.");

        var manuelle = anlage.ManuelleSchritte ?? [];
        if (manuelle.Count > MaxManuelleSchritte)
            return BadRequest($"Höchstens {MaxManuelleSchritte} manuelle Schritte.");
        var vorlagenNr = typ.Schritte.Select(s => s.Nr).ToHashSet();
        foreach (var m in manuelle)
        {
            var fehler = await SchrittPruefenAsync(m.Titel, m.BereichId, m.FristTage, m.ZustaendigId, ct);
            if (fehler is not null)
                return BadRequest(fehler);
            if (m.WartetAufNr is { } w && !vorlagenNr.Contains(w))
                return BadRequest($"„{m.Titel.Trim()}“ wartet auf einen Schritt, den die Vorlage nicht hat.");
        }

        var schritte = VorgangsLogik.SchritteErzeugen(
            typ.Schritte,
            (anlage.EntfernteSchritte ?? []).ToHashSet(),
            anlage.ErfuellteBedingungen ?? [],
            manuelle.Select(m => new ManuellerSchritt(m.Titel, m.BereichId, m.FristTage, m.WartetAufNr, m.ZustaendigId)),
            start);
        if (schritte.All(s => s.Status == AufgabenStatus.Entfallen))
            return BadRequest("Mindestens ein Schritt muss bleiben.");

        var ich = await IchAsync(db, benutzer, ct);
        var jetzt = DateTime.UtcNow;
        var strang = await OdooStrangAsync(kunde.Id, odoo, ct);
        var titel = Kuerzen(odoo is null ? typ.Name : $"{typ.Name} {odoo}", 200);
        var vorgang = new Vorgang
        {
            KundeId = kunde.Id,
            VorgangstypId = typ.Id,
            VorgangstypStand = typ.Stand,
            Titel = titel,
            OdooAuftrag = odoo,
            Hinweis = hinweis,
            Start = start,
            ErstelltAm = jetzt,
            ErstelltVonId = ich,
            StrangSchluessel = strang?.Schluessel ?? "vorgang:neu",
            StrangName = Kuerzen(strang?.Name ?? titel, 300),
        };
        foreach (var s in schritte)
        {
            s.KundeId = kunde.Id;
            s.Art = "Vorgang";
            s.ErstelltAm = jetzt;
            s.ErstelltVonId = ich;
            vorgang.Schritte.Add(s);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Vorgaenge.Add(vorgang);
        await db.SaveChangesAsync(ct);
        if (strang is null)
            vorgang.StrangSchluessel = $"vorgang:{vorgang.Id}";
        foreach (var s in vorgang.Schritte)
        {
            s.StrangSchluessel = vorgang.StrangSchluessel;
            s.Bezug = vorgang.StrangName;
        }
        Verlauf(vorgang, ich, $"Vorgang angelegt: {titel}", AnlageText(vorgang, typ), AktivitaetsStufe.Meilenstein);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(vorgang.Id);
    }

    private static string? AnlageText(Vorgang v, Vorgangstyp typ)
    {
        var text = new StringBuilder();
        if (v.Hinweis is not null)
            text.AppendLine($"Hinweis: {v.Hinweis}");
        foreach (var s in v.Schritte.Where(s => s.Status == AufgabenStatus.Entfallen))
            text.AppendLine(s.EntfallenGrund == VorgangsLogik.GrundEntfernt
                ? $"Schritt von Hand entfernt: {s.Titel}"
                : $"Schritt entfällt (Bedingung nicht erfüllt): {s.Titel}");
        foreach (var s in v.Schritte.Where(s => s.Herkunft == SchrittHerkunft.Manuell))
            text.AppendLine($"Schritt von Hand hinzugefügt: {s.Titel}");
        text.Append($"Vorlage „{typ.Name}“, Stand {typ.Stand}");
        return text.ToString();
    }

    // --- Hinweis und Schritte am laufenden Vorgang ---------------------------------

    [HttpPut("{id:int}/hinweis")]
    public async Task<IActionResult> HinweisSetzen(int id, HinweisSetzen setzen, CancellationToken ct)
    {
        var v = await db.Vorgaenge.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (v is null)
            return NotFound();
        var neu = string.IsNullOrWhiteSpace(setzen.Text) ? null : setzen.Text.Trim();
        if (neu is { Length: > 1000 })
            return BadRequest("Der Hinweis darf höchstens 1.000 Zeichen lang sein.");
        if (neu == v.Hinweis)
            return NoContent();
        var ich = await IchAsync(db, benutzer, ct);
        Verlauf(v, ich, neu is null ? "Hinweis zum Vorgang entfernt" : v.Hinweis is null ? "Hinweis zum Vorgang gesetzt" : "Hinweis zum Vorgang geändert",
            neu is null ? $"Bisher: {v.Hinweis}" : neu, AktivitaetsStufe.Wesentlich);
        v.Hinweis = neu;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:int}/schritte")]
    public async Task<ActionResult<int>> SchrittHinzufuegen(int id, SchrittHinzufuegen neu, CancellationToken ct)
    {
        var v = await VorgangLadenAsync(id, ct);
        if (v is null)
            return NotFound();
        var fehler = await SchrittPruefenAsync(neu.Titel, neu.BereichId, neu.FristTage, neu.ZustaendigId, ct);
        if (fehler is not null)
            return BadRequest(fehler);
        if (v.Schritte.Count >= VorgangsLogik.MaxSchritte + MaxManuelleSchritte)
            return BadRequest("Der Vorgang hat schon die höchste Anzahl an Schritten.");
        Aufgabe? vorgaenger = null;
        if (neu.WartetAufId is { } w)
        {
            vorgaenger = v.Schritte.FirstOrDefault(s => s.Id == w);
            if (vorgaenger is null || vorgaenger.Status == AufgabenStatus.Entfallen)
                return BadRequest("Der Schritt, auf den gewartet werden soll, gehört nicht zum Vorgang oder ist entfallen.");
        }
        var ich = await IchAsync(db, benutzer, ct);
        var schritt = new Aufgabe
        {
            KundeId = v.KundeId,
            VorgangId = v.Id,
            Titel = neu.Titel.Trim(),
            Art = "Vorgang",
            ZustaendigBereichId = neu.BereichId,
            ZustaendigId = neu.ZustaendigId,
            FristTage = neu.FristTage,
            Herkunft = SchrittHerkunft.Manuell,
            WartetAuf = vorgaenger,
            Reihenfolge = (v.Schritte.Max(s => s.Reihenfolge) ?? 0) + 1,
            Bezug = v.StrangName,
            StrangSchluessel = v.StrangSchluessel,
            ErstelltAm = DateTime.UtcNow,
            ErstelltVonId = ich,
        };
        v.Schritte.Add(schritt);
        VorgangsLogik.FristenSetzen(v.Schritte, Heute);
        Verlauf(v, ich, $"Schritt von Hand hinzugefügt: {schritt.Titel}",
            vorgaenger is null ? null : $"Wartet auf „{vorgaenger.Titel}“");
        Abschluss(v, ich);
        await db.SaveChangesAsync(ct);
        return Ok(schritt.Id);
    }

    [HttpDelete("schritte/{schrittId:int}")]
    public async Task<IActionResult> SchrittEntfernen(int schrittId, CancellationToken ct)
    {
        var (v, s) = await SchrittLadenAsync(schrittId, ct);
        if (v is null || s is null)
            return NotFound();
        if (VorgangsLogik.FertigOderWeg(s))
            return BadRequest("Erledigte oder schon entfallene Schritte lassen sich nicht entfernen.");
        var ich = await IchAsync(db, benutzer, ct);
        var freigegeben = VorgangsLogik.Entfernen(s, v.Schritte, Heute);
        Verlauf(v, ich, $"Schritt von Hand entfernt: {s.Titel}");
        Freigabe(v, ich, freigegeben);
        Abschluss(v, ich);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Schritt selbst übernehmen: liegt dann bei mir, Status „in Arbeit".</summary>
    [HttpPost("schritte/{schrittId:int}/uebernehmen")]
    public async Task<IActionResult> Uebernehmen(int schrittId, CancellationToken ct)
    {
        var (v, s) = await SchrittLadenAsync(schrittId, ct);
        if (v is null || s is null)
            return NotFound();
        if (!VorgangsLogik.Aktiv(s))
            return BadRequest("Der Schritt ist noch nicht dran oder schon fertig.");
        var ich = await IchAsync(db, benutzer, ct);
        if (ich is null)
            return BadRequest("Kein Mitarbeiter-Datensatz zum angemeldeten Konto.");
        s.ZustaendigId = ich;
        s.Status = AufgabenStatus.InArbeit;
        s.Wiedervorlage = null;
        Verlauf(v, ich, $"Schritt übernommen: {s.Titel}");
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Einer Person zuweisen oder (ohne Person) zurück in den Pool des Bereichs legen.</summary>
    [HttpPost("schritte/{schrittId:int}/zuweisen")]
    public async Task<IActionResult> Zuweisen(int schrittId, SchrittZuweisen zuweisen, CancellationToken ct)
    {
        var (v, s) = await SchrittLadenAsync(schrittId, ct);
        if (v is null || s is null)
            return NotFound();
        if (VorgangsLogik.FertigOderWeg(s))
            return BadRequest("Der Schritt ist schon erledigt oder entfallen.");
        string? name = null;
        if (zuweisen.ZustaendigId is { } m)
        {
            name = await db.Mitarbeiter.Where(x => x.Id == m && x.Aktiv).Select(x => x.Anzeigename).FirstOrDefaultAsync(ct);
            if (name is null)
                return BadRequest("Der gewählte Mitarbeiter existiert nicht oder ist deaktiviert.");
        }
        if (s.ZustaendigId == zuweisen.ZustaendigId)
            return NoContent();
        var ich = await IchAsync(db, benutzer, ct);
        s.ZustaendigId = zuweisen.ZustaendigId;
        if (zuweisen.ZustaendigId is null && s.Status == AufgabenStatus.InArbeit)
            s.Status = AufgabenStatus.Offen;
        Verlauf(v, ich, name is null ? $"Schritt zurück in den Pool: {s.Titel}" : $"Schritt an {name} übergeben: {s.Titel}");
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("schritte/{schrittId:int}/erledigt")]
    public async Task<IActionResult> Erledigt(int schrittId, SchrittErledigen erledigen, CancellationToken ct)
    {
        var (v, s) = await SchrittLadenAsync(schrittId, ct);
        if (v is null || s is null)
            return NotFound();
        if (VorgangsLogik.FertigOderWeg(s))
            return BadRequest("Der Schritt ist schon erledigt oder entfallen.");
        if (VorgangsLogik.Gesperrt(s))
            return BadRequest($"Der Schritt wartet noch auf „{s.WartetAuf!.Titel}“.");
        var wert = erledigen.Wert?.Trim();
        if (s.Pflichtfeld is not null && string.IsNullOrEmpty(wert))
            return BadRequest($"Bitte „{s.Pflichtfeld}“ eintragen.");
        if (wert is { Length: > 300 } || erledigen.Notiz is { Length: > 5000 })
            return BadRequest("Wert höchstens 300, Notiz höchstens 5.000 Zeichen.");
        var ich = await IchAsync(db, benutzer, ct);
        if (s.Pflichtfeld is not null)
            s.PflichtfeldWert = wert;
        s.ZustaendigId ??= ich;
        var freigegeben = VorgangsLogik.Erledigen(s, v.Schritte, Heute, DateTime.UtcNow, ich);
        var inhalt = string.Join("\n", new[]
        {
            s.Pflichtfeld is not null ? $"{s.Pflichtfeld}: {wert}" : null,
            string.IsNullOrWhiteSpace(erledigen.Notiz) ? null : erledigen.Notiz.Trim(),
        }.Where(t => t is not null));
        Verlauf(v, ich, $"Schritt erledigt: {s.Titel}", inhalt.Length == 0 ? null : inhalt, AktivitaetsStufe.Wesentlich);
        Freigabe(v, ich, freigegeben);
        Abschluss(v, ich);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>„Wartet auf Kunden" mit Wiedervorlage — am Stichtag ist der Schritt wieder offen.</summary>
    [HttpPost("schritte/{schrittId:int}/wartet")]
    public async Task<IActionResult> Wartet(int schrittId, SchrittWarten warten, CancellationToken ct)
    {
        var (v, s) = await SchrittLadenAsync(schrittId, ct);
        if (v is null || s is null)
            return NotFound();
        if (!VorgangsLogik.Aktiv(s))
            return BadRequest("Der Schritt ist noch nicht dran oder schon fertig.");
        if (warten.Wiedervorlage <= Heute || warten.Wiedervorlage > Heute.AddDays(VorgangsLogik.MaxFristTage))
            return BadRequest("Die Wiedervorlage muss nach heute und innerhalb eines Jahres liegen.");
        if (warten.Notiz is { Length: > 5000 })
            return BadRequest("Die Notiz darf höchstens 5.000 Zeichen lang sein.");
        var ich = await IchAsync(db, benutzer, ct);
        s.Status = AufgabenStatus.WartetAufKunden;
        s.Wiedervorlage = warten.Wiedervorlage;
        s.ZustaendigId ??= ich;
        Verlauf(v, ich, $"Wartet auf Kunden bis {warten.Wiedervorlage:dd.MM.yyyy}: {s.Titel}",
            string.IsNullOrWhiteSpace(warten.Notiz) ? null : warten.Notiz.Trim());
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Wieder öffnen: erledigten Schritt zurücknehmen, Warten beenden oder entfallenen Schritt zurückholen.</summary>
    [HttpPost("schritte/{schrittId:int}/offen")]
    public async Task<IActionResult> WiederOffen(int schrittId, CancellationToken ct)
    {
        var (v, s) = await SchrittLadenAsync(schrittId, ct);
        if (v is null || s is null)
            return NotFound();
        var ich = await IchAsync(db, benutzer, ct);
        var text = s.Status switch
        {
            AufgabenStatus.Erledigt => $"Schritt wieder geöffnet: {s.Titel}",
            AufgabenStatus.Entfallen => $"Schritt zurückgeholt: {s.Titel}",
            AufgabenStatus.WartetAufKunden => $"Warten auf Kunden beendet: {s.Titel}",
            _ => null,
        };
        if (text is null)
            return NoContent();
        s.Status = s.ZustaendigId is null ? AufgabenStatus.Offen : AufgabenStatus.InArbeit;
        s.Wiedervorlage = null;
        s.EntfallenGrund = null;
        s.ErledigtAm = null;
        s.ErledigtVonId = null;
        VorgangsLogik.FristenSetzen(v.Schritte, Heute);
        Verlauf(v, ich, text);
        Abschluss(v, ich);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("schritte/{schrittId:int}/notiz")]
    public async Task<IActionResult> Notiz(int schrittId, SchrittNotiz notiz, CancellationToken ct)
    {
        var (v, s) = await SchrittLadenAsync(schrittId, ct);
        if (v is null || s is null)
            return NotFound();
        var text = notiz.Text?.Trim() ?? "";
        if (text.Length is 0 or > 5000)
            return BadRequest("Die Notiz braucht einen Text (höchstens 5.000 Zeichen).");
        Verlauf(v, await IchAsync(db, benutzer, ct), $"Notiz zu „{s.Titel}“", text, AktivitaetsStufe.Wesentlich, AktivitaetsTyp.Notiz);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // --- Hilfen --------------------------------------------------------------------

    private async Task<Vorgang?> VorgangLadenAsync(int id, CancellationToken ct)
    {
        var v = await db.Vorgaenge
            .Include(x => x.Schritte).ThenInclude(s => s.ZustaendigBereich)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (v is not null)
            Verknuepfen(v.Schritte);
        return v;
    }

    private async Task<(Vorgang? V, Aufgabe? S)> SchrittLadenAsync(int schrittId, CancellationToken ct)
    {
        var vorgangId = await db.Aufgaben.Where(a => a.Id == schrittId).Select(a => a.VorgangId).FirstOrDefaultAsync(ct);
        if (vorgangId is null)
            return (null, null);
        var v = await VorgangLadenAsync(vorgangId.Value, ct);
        return (v, v?.Schritte.FirstOrDefault(s => s.Id == schrittId));
    }

    private async Task<string?> SchrittPruefenAsync(string? titel, int bereichId, int fristTage, int? zustaendigId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(titel) || titel.Trim().Length > 300)
            return "Der Schritt braucht einen Titel (höchstens 300 Zeichen).";
        if (fristTage is < 0 or > VorgangsLogik.MaxFristTage)
            return $"Die Frist muss zwischen 0 und {VorgangsLogik.MaxFristTage} Tagen liegen.";
        if (!await db.Bereiche.AnyAsync(b => b.Id == bereichId && b.Aktiv, ct))
            return "Der gewählte Bereich existiert nicht.";
        if (zustaendigId is { } m && !await db.Mitarbeiter.AnyAsync(x => x.Id == m && x.Aktiv, ct))
            return "Der gewählte Mitarbeiter existiert nicht oder ist deaktiviert.";
        return null;
    }

    private Task<List<AuswahlEintrag>> BereicheAsync(CancellationToken ct) =>
        db.Bereiche.AsNoTracking().Where(b => b.Aktiv).OrderBy(b => b.Name)
            .Select(b => new AuswahlEintrag(b.Id, b.Name, b.Leiter != null ? b.Leiter.Anzeigename : null)).ToListAsync(ct);

    /// <summary>Odoo-Auftragsnummer normalisieren (Großbuchstaben, ohne Leerzeichen); leer ist erlaubt.</summary>
    internal static bool OdooNummerPruefen(string? eingabe, out string? nummer)
    {
        nummer = string.IsNullOrWhiteSpace(eingabe) ? null : eingabe.Trim().ToUpperInvariant();
        return nummer is null || OdooNummerMuster().IsMatch(nummer);
    }

    [GeneratedRegex("^[A-Z0-9/-]{1,30}$")]
    private static partial Regex OdooNummerMuster();

    private Task<(string Schluessel, string Name)?> OdooStrangAsync(int kundeId, string? auftrag, CancellationToken ct) =>
        VorgangsHilfen.OdooStrangAsync(db, kundeId, auftrag, ct);

    private void Verlauf(Vorgang v, int? ich, string betreff, string? inhalt = null,
        AktivitaetsStufe stufe = AktivitaetsStufe.Systemmeldung, AktivitaetsTyp typ = AktivitaetsTyp.Aufgabe) =>
        VorgangsProtokoll.Verlauf(db, v, ich, betreff, inhalt, stufe, typ);

    private void Freigabe(Vorgang v, int? ich, List<Aufgabe> freigegeben) =>
        VorgangsProtokoll.Freigabe(db, v, ich, freigegeben);

    private void Abschluss(Vorgang v, int? ich) => VorgangsProtokoll.Abschluss(db, v, ich);
}
