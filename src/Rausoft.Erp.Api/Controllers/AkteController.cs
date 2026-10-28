using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Api.Dienste;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Customers;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Kopf der Kundenakte (Entscheidung 23.10.2026, Mockup akte-ueberblick.html):
/// „Was lief zuletzt?" mit Wer-mit-wem, offene Aufgaben, Vereinbarungen und das
/// manuelle Erfassen von Notizen, Telefonaten und Terminen mit mehreren Teilnehmern.
/// Zugriff: jeder aktive Mitarbeiter (Fallback-Policy).
/// </summary>
[ApiController]
[Route("api/kunden")]
public class AkteController(ErpDbContext db, IAktuellerBenutzer benutzer) : ControllerBase
{
    private const int MaxTage = 3650;

    // --- Überblick -------------------------------------------------------------------

    [HttpGet("{id:int}/ueberblick")]
    public async Task<ActionResult<AkteUeberblick>> Ueberblick(int id, [FromQuery] int tage = 30, CancellationToken ct = default)
    {
        if (!await db.Kunden.AnyAsync(k => k.Id == id, ct))
            return NotFound();
        tage = Math.Clamp(tage, 1, MaxTage);
        var seit = DateTime.UtcNow.Date.AddDays(-tage);

        // Kontakt = Kommunikation mit Beteiligten auf beiden Seiten (Notiz, Mail, Anruf, Termin, Aufgabe)
        var roh = await db.Aktivitaeten.AsNoTracking()
            .Where(a => a.KundeId == id && a.Zeitpunkt >= seit && a.Stufe == AktivitaetsStufe.Wesentlich
                        && (a.Typ == AktivitaetsTyp.EMail || a.Typ == AktivitaetsTyp.Notiz || a.Typ == AktivitaetsTyp.Anruf
                            || a.Typ == AktivitaetsTyp.Termin || a.Typ == AktivitaetsTyp.Aufgabe))
            .OrderByDescending(a => a.Zeitpunkt)
            .Select(a => new
            {
                a.Zeitpunkt, a.Typ, a.Richtung, a.AutorName,
                Autor = a.Mitarbeiter == null ? null : new TeilnehmerRoh(TeilnehmerSeite.Rausoft, a.MitarbeiterId, a.Mitarbeiter.Anzeigename,
                    a.Mitarbeiter.Bereich != null ? a.Mitarbeiter.Bereich.Name : null, null, null, null, null, null, null),
                Ap = a.Ansprechpartner == null ? null : new TeilnehmerRoh(TeilnehmerSeite.Kunde, null, null, null, a.AnsprechpartnerId,
                    a.Ansprechpartner.Anrede, a.Ansprechpartner.Vorname, a.Ansprechpartner.Nachname, a.Ansprechpartner.Position, null),
                Teilnehmer = a.Teilnehmer.Select(t => new TeilnehmerRoh(t.Seite, t.MitarbeiterId,
                    t.Mitarbeiter != null ? t.Mitarbeiter.Anzeigename : null,
                    t.Mitarbeiter != null && t.Mitarbeiter.Bereich != null ? t.Mitarbeiter.Bereich.Name : null,
                    t.AnsprechpartnerId,
                    t.Ansprechpartner != null ? t.Ansprechpartner.Anrede : null,
                    t.Ansprechpartner != null ? t.Ansprechpartner.Vorname : null,
                    t.Ansprechpartner != null ? t.Ansprechpartner.Nachname : null,
                    t.Ansprechpartner != null ? t.Ansprechpartner.Position : null,
                    t.Name)).ToList(),
            })
            .ToListAsync(ct);

        var rausoft = new Dictionary<string, AktePerson>();
        var kunde = new Dictionary<string, AktePerson>();
        var zellen = new Dictionary<(string, string), (int Anzahl, DateTime Zuletzt)>();
        LetzterKontakt? letzter = null;
        var kontakte = 0;
        foreach (var a in roh)
        {
            var (r, k) = AkteTeilnehmer.Seiten(a.Autor, a.Ap, a.AutorName, a.Richtung, a.Teilnehmer);
            if (r.Count == 0 || k.Count == 0)
                continue;
            kontakte++;
            letzter ??= new LetzterKontakt(a.Zeitpunkt, ArtText(a.Typ), r, k);
            foreach (var p in r) rausoft.TryAdd(p.Schluessel, p);
            foreach (var p in k) kunde.TryAdd(p.Schluessel, p);
            foreach (var pr in r)
                foreach (var pk in k)
                {
                    var schluessel = (pr.Schluessel, pk.Schluessel);
                    zellen[schluessel] = zellen.TryGetValue(schluessel, out var z)
                        ? (z.Anzahl + 1, z.Zuletzt > a.Zeitpunkt ? z.Zuletzt : a.Zeitpunkt)
                        : (1, a.Zeitpunkt);
                }
        }

        var heute = DateOnly.FromDateTime(DateTime.Now);
        // Wartet auf Kunden: maßgeblich ist die Wiedervorlage statt der Frist
        var offeneAufgaben = await db.Aufgaben.AsNoTracking()
            .Where(t => t.KundeId == id)
            .Where(Aufgabe.IstOffen)
            .Select(t => t.Status == AufgabenStatus.WartetAufKunden ? t.Wiedervorlage : t.Faellig).ToListAsync(ct);
        var vereinbarungenOffen = await db.Vereinbarungen.CountAsync(v => v.KundeId == id && !v.Erledigt, ct);

        // Spalten/Zeilen nach Aktivität sortiert (wer am häufigsten beteiligt ist, zuerst)
        int Gewicht(string schluessel, bool istRausoft) =>
            zellen.Where(z => (istRausoft ? z.Key.Item1 : z.Key.Item2) == schluessel).Sum(z => z.Value.Anzahl);
        return Ok(new AkteUeberblick(
            tage, kontakte, letzter,
            rausoft.Values.OrderByDescending(p => Gewicht(p.Schluessel, true)).ToList(),
            kunde.Values.OrderByDescending(p => Gewicht(p.Schluessel, false)).ToList(),
            zellen.Select(z => new GespraechsZelle(z.Key.Item1, z.Key.Item2, z.Value.Anzahl, z.Value.Zuletzt)).ToList(),
            offeneAufgaben.Count,
            offeneAufgaben.Count(f => f is not null && f < heute),
            vereinbarungenOffen));
    }

    private static string ArtText(AktivitaetsTyp typ) => typ switch
    {
        AktivitaetsTyp.EMail => "E-Mail",
        AktivitaetsTyp.Anruf => "Telefonat",
        AktivitaetsTyp.Termin => "Termin",
        AktivitaetsTyp.Aufgabe => "Aufgabe",
        _ => "Notiz",
    };

    // --- Aufgaben ----------------------------------------------------------------------

    /// <summary>
    /// Offene Aufgaben des Kunden und die der letzten 7 Tage erledigten (zum Nachvollziehen).
    /// Vorgangsschritte erscheinen erst, wenn sie dran sind (nicht gesperrt), entfallene nie.
    /// </summary>
    [HttpGet("{id:int}/aufgaben")]
    public async Task<ActionResult<List<AufgabeEintrag>>> Aufgaben(int id, CancellationToken ct)
    {
        var grenze = DateTime.UtcNow.AddDays(-7);
        var liste = await db.Aufgaben.AsNoTracking()
            .Where(a => a.KundeId == id && a.Status != AufgabenStatus.Entfallen
                        && (a.Status != AufgabenStatus.Erledigt || a.ErledigtAm >= grenze))
            .Where(a => a.WartetAufId == null
                        || a.WartetAuf!.Status == AufgabenStatus.Erledigt || a.WartetAuf.Status == AufgabenStatus.Entfallen)
            .Select(a => new
            {
                a.Id, a.Titel, a.Art, a.Beschreibung, a.Status, a.Bezug, a.StrangSchluessel, a.Quelle, a.ErledigtAm, a.VorgangId,
                Faellig = a.Status == AufgabenStatus.WartetAufKunden ? a.Wiedervorlage : a.Faellig,
                Person = a.Zustaendig != null ? a.Zustaendig.Anzeigename : null,
                PersonBereich = a.Zustaendig != null && a.Zustaendig.Bereich != null ? a.Zustaendig.Bereich.Name : null,
                Pool = a.ZustaendigBereich != null ? a.ZustaendigBereich.Name : null,
                a.ZustaendigName,
            })
            .ToListAsync(ct);
        return Ok(liste
            .OrderBy(a => a.Status == AufgabenStatus.Erledigt)
            .ThenBy(a => a.Faellig ?? DateOnly.MaxValue)
            .Select(a => new AufgabeEintrag(a.Id, a.Titel, a.Art, a.Beschreibung, a.Faellig,
                a.Person ?? (a.Pool is not null ? $"Pool {a.Pool}" : a.ZustaendigName),
                a.Person is not null ? a.PersonBereich : a.Pool,
                a.Person is null && a.Pool is not null,
                a.Status == AufgabenStatus.Erledigt, a.Bezug, a.StrangSchluessel,
                a.Quelle == AktivitaetsQuelle.OdooImport ? "Odoo" : "Kundenzentrale", a.ErledigtAm,
                a.VorgangId, a.Status == AufgabenStatus.WartetAufKunden))
            .ToList());
    }

    [HttpPost("{id:int}/aufgaben")]
    public async Task<ActionResult<int>> AufgabeAnlegen(int id, AufgabeAnlegen anlage, CancellationToken ct)
    {
        if (!await db.Kunden.AnyAsync(k => k.Id == id, ct))
            return NotFound();
        var titel = anlage.Titel?.Trim() ?? "";
        if (titel.Length is 0 or > 300)
            return BadRequest("Die Aufgabe braucht einen Titel (höchstens 300 Zeichen).");
        if (anlage.ZustaendigId is null == anlage.ZustaendigBereichId is null)
            return BadRequest("Bitte genau eine zuständige Person oder einen Bereichs-Pool wählen.");
        if (anlage.ZustaendigId is { } m && !await db.Mitarbeiter.AnyAsync(x => x.Id == m && x.Aktiv, ct))
            return BadRequest("Der gewählte Mitarbeiter existiert nicht oder ist deaktiviert.");
        if (anlage.ZustaendigBereichId is { } b && !await db.Bereiche.AnyAsync(x => x.Id == b && x.Aktiv, ct))
            return BadRequest("Der gewählte Bereich existiert nicht.");
        var vorgang = await VorgangPruefenAsync(id, anlage.Vorgang, ct);

        var aufgabe = new Aufgabe
        {
            KundeId = id,
            Titel = titel,
            Art = string.IsNullOrWhiteSpace(anlage.Art) ? "To-do" : Kuerzen(anlage.Art.Trim(), 50),
            Faellig = anlage.Faellig,
            ZustaendigId = anlage.ZustaendigId,
            ZustaendigBereichId = anlage.ZustaendigBereichId,
            Bezug = vorgang?.Name,
            StrangSchluessel = vorgang?.Schluessel,
            ErstelltAm = DateTime.UtcNow,
            ErstelltVonId = await AktuellerMitarbeiterId(ct),
        };
        db.Aufgaben.Add(aufgabe);
        await db.SaveChangesAsync(ct);
        return Ok(aufgabe.Id);
    }

    /// <summary>
    /// Erledigen bzw. wieder öffnen. Beim Erledigen entsteht ein Verlaufseintrag „Aufgabe erledigt".
    /// Aus Odoo übernommene Aufgaben werden nur hier erledigt — Odoo selbst bleibt unverändert.
    /// </summary>
    [HttpPut("aufgaben/{aufgabeId:int}/erledigt")]
    public async Task<IActionResult> AufgabeErledigt(int aufgabeId, ErledigtSetzen setzen, CancellationToken ct)
    {
        var aufgabe = await db.Aufgaben.FirstOrDefaultAsync(a => a.Id == aufgabeId, ct);
        if (aufgabe is null)
            return NotFound();
        if (aufgabe.VorgangId is not null)
            return BadRequest("Vorgangsschritte werden im Vorgang erledigt (Pflichtfelder, Weitergabe an den nächsten Bereich).");
        var ich = await AktuellerMitarbeiterId(ct);
        var warErledigt = aufgabe.Status == AufgabenStatus.Erledigt;
        if (setzen.Erledigt && !warErledigt)
        {
            aufgabe.Status = AufgabenStatus.Erledigt;
            aufgabe.ErledigtAm = DateTime.UtcNow;
            aufgabe.ErledigtVonId = ich;
            if (aufgabe.KundeId is { } kundeId)
            {
                var eintrag = new Aktivitaet
                {
                    KundeId = kundeId,
                    Typ = AktivitaetsTyp.Aufgabe,
                    Zeitpunkt = DateTime.UtcNow,
                    MitarbeiterId = ich,
                    Richtung = AktivitaetsRichtung.Intern,
                    Betreff = Kuerzen($"Aufgabe erledigt: {aufgabe.Titel}", 300),
                    Inhalt = aufgabe.Beschreibung,
                    Bezug = aufgabe.Bezug,
                    StrangSchluessel = aufgabe.StrangSchluessel,
                    StrangName = aufgabe.Bezug,
                };
                if (ich is not null)
                    eintrag.Teilnehmer.Add(new AktivitaetTeilnehmer { Seite = TeilnehmerSeite.Rausoft, MitarbeiterId = ich });
                db.Aktivitaeten.Add(eintrag);
            }
        }
        else if (!setzen.Erledigt && warErledigt)
        {
            aufgabe.Status = AufgabenStatus.Offen;
            aufgabe.ErledigtAm = null;
            aufgabe.ErledigtVonId = null;
        }
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // --- Vereinbarungen ---------------------------------------------------------------

    [HttpGet("{id:int}/vereinbarungen")]
    public async Task<ActionResult<List<VereinbarungEintrag>>> Vereinbarungen(int id, CancellationToken ct)
    {
        var roh = await db.Vereinbarungen.AsNoTracking()
            .Where(v => v.KundeId == id)
            .OrderBy(v => v.Erledigt).ThenByDescending(v => v.ErstelltAm)
            .Select(v => new
            {
                v.Id, v.Text, v.Erledigt, v.ErstelltAm, v.AktivitaetId,
                ErstelltVon = v.ErstelltVon != null ? v.ErstelltVon.Anzeigename : null,
                Quelle = v.Aktivitaet == null ? null : new
                {
                    v.Aktivitaet.Typ, v.Aktivitaet.Zeitpunkt, v.Aktivitaet.Betreff, v.Aktivitaet.Richtung, v.Aktivitaet.AutorName,
                    Autor = v.Aktivitaet.Mitarbeiter == null ? null : new TeilnehmerRoh(TeilnehmerSeite.Rausoft, v.Aktivitaet.MitarbeiterId,
                        v.Aktivitaet.Mitarbeiter.Anzeigename, v.Aktivitaet.Mitarbeiter.Bereich != null ? v.Aktivitaet.Mitarbeiter.Bereich.Name : null,
                        null, null, null, null, null, null),
                    Ap = v.Aktivitaet.Ansprechpartner == null ? null : new TeilnehmerRoh(TeilnehmerSeite.Kunde, null, null, null,
                        v.Aktivitaet.AnsprechpartnerId, v.Aktivitaet.Ansprechpartner.Anrede, v.Aktivitaet.Ansprechpartner.Vorname,
                        v.Aktivitaet.Ansprechpartner.Nachname, v.Aktivitaet.Ansprechpartner.Position, null),
                    Teilnehmer = v.Aktivitaet.Teilnehmer.Select(t => new TeilnehmerRoh(t.Seite, t.MitarbeiterId,
                        t.Mitarbeiter != null ? t.Mitarbeiter.Anzeigename : null,
                        t.Mitarbeiter != null && t.Mitarbeiter.Bereich != null ? t.Mitarbeiter.Bereich.Name : null,
                        t.AnsprechpartnerId,
                        t.Ansprechpartner != null ? t.Ansprechpartner.Anrede : null,
                        t.Ansprechpartner != null ? t.Ansprechpartner.Vorname : null,
                        t.Ansprechpartner != null ? t.Ansprechpartner.Nachname : null,
                        t.Ansprechpartner != null ? t.Ansprechpartner.Position : null,
                        t.Name)).ToList(),
                },
            })
            .ToListAsync(ct);
        return Ok(roh.Select(v =>
        {
            var (r, k) = v.Quelle is null
                ? ([], [])
                : AkteTeilnehmer.Seiten(v.Quelle.Autor, v.Quelle.Ap, v.Quelle.AutorName, v.Quelle.Richtung, v.Quelle.Teilnehmer);
            return new VereinbarungEintrag(v.Id, v.Text, v.Erledigt, v.ErstelltAm, v.ErstelltVon, v.AktivitaetId,
                v.Quelle is null ? null : ArtText(v.Quelle.Typ), v.Quelle?.Zeitpunkt, v.Quelle?.Betreff, r, k);
        }).ToList());
    }

    [HttpPost("{id:int}/vereinbarungen")]
    public async Task<ActionResult<int>> VereinbarungAnlegen(int id, VereinbarungAnlegen anlage, CancellationToken ct)
    {
        if (!await db.Kunden.AnyAsync(k => k.Id == id, ct))
            return NotFound();
        var text = anlage.Text?.Trim() ?? "";
        if (text.Length is 0 or > 500)
            return BadRequest("Die Vereinbarung braucht einen Text (höchstens 500 Zeichen).");
        if (anlage.AktivitaetId is { } aid && !await db.Aktivitaeten.AnyAsync(a => a.Id == aid && a.KundeId == id, ct))
            return BadRequest("Der Verlaufseintrag gehört nicht zu diesem Kunden.");
        var v = new Vereinbarung
        {
            KundeId = id, Text = text, AktivitaetId = anlage.AktivitaetId,
            ErstelltAm = DateTime.UtcNow, ErstelltVonId = await AktuellerMitarbeiterId(ct),
        };
        db.Vereinbarungen.Add(v);
        await db.SaveChangesAsync(ct);
        return Ok(v.Id);
    }

    [HttpPut("vereinbarungen/{vereinbarungId:int}/erledigt")]
    public async Task<IActionResult> VereinbarungErledigt(int vereinbarungId, ErledigtSetzen setzen, CancellationToken ct)
    {
        var v = await db.Vereinbarungen.FirstOrDefaultAsync(x => x.Id == vereinbarungId, ct);
        if (v is null)
            return NotFound();
        if (v.Erledigt != setzen.Erledigt)
        {
            v.Erledigt = setzen.Erledigt;
            v.ErledigtAm = setzen.Erledigt ? DateTime.UtcNow : null;
            v.ErledigtVonId = setzen.Erledigt ? await AktuellerMitarbeiterId(ct) : null;
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    // --- Manuell erfassen ---------------------------------------------------------------

    [HttpGet("{id:int}/erfassung")]
    public async Task<ActionResult<ErfassungsAuswahl>> Erfassung(int id, CancellationToken ct)
    {
        if (!await db.Kunden.AnyAsync(k => k.Id == id, ct))
            return NotFound();
        var mitarbeiter = await db.Mitarbeiter.AsNoTracking().Where(m => m.Aktiv).OrderBy(m => m.Anzeigename)
            .Select(m => new AuswahlEintrag(m.Id, m.Anzeigename, m.Bereich != null ? m.Bereich.Name : null)).ToListAsync(ct);
        var ansprechpartner = (await db.Ansprechpartner.AsNoTracking().Where(a => a.KundeId == id && a.Aktiv)
                .Select(a => new { a.Id, a.Anrede, a.Vorname, a.Nachname, a.Position }).ToListAsync(ct))
            .Select(a => new AuswahlEintrag(a.Id, AkteTeilnehmer.AnsprechpartnerName(a.Anrede, a.Vorname, a.Nachname),
                string.IsNullOrWhiteSpace(a.Position) ? null : a.Position))
            .OrderBy(a => a.Name).ToList();
        var bereiche = await db.Bereiche.AsNoTracking().Where(b => b.Aktiv).OrderBy(b => b.Name)
            .Select(b => new AuswahlEintrag(b.Id, b.Name, null)).ToListAsync(ct);
        return Ok(new ErfassungsAuswahl(mitarbeiter, ansprechpartner, bereiche, await VorgaengeAsync(id, ct),
            await AktuellerMitarbeiterId(ct)));
    }

    [HttpPost("{id:int}/eintraege")]
    public async Task<ActionResult<int>> EintragAnlegen(int id, EintragAnlegen anlage, CancellationToken ct)
    {
        if (!await db.Kunden.AnyAsync(k => k.Id == id, ct))
            return NotFound();
        if (!Enum.TryParse<AktivitaetsTyp>(anlage.Typ, out var typ)
            || typ is not (AktivitaetsTyp.Notiz or AktivitaetsTyp.Anruf or AktivitaetsTyp.Termin))
            return BadRequest("Art muss Notiz, Anruf oder Termin sein.");
        var betreff = anlage.Betreff?.Trim() ?? "";
        if (betreff.Length is 0 or > 300)
            return BadRequest("Der Eintrag braucht einen Betreff (höchstens 300 Zeichen).");
        if (anlage.Inhalt is { Length: > 20000 })
            return BadRequest("Der Inhalt ist zu lang (höchstens 20.000 Zeichen).");
        var vereinbarungen = (anlage.Vereinbarungen ?? []).Select(v => v?.Trim() ?? "").Where(v => v.Length > 0).ToList();
        if (vereinbarungen.Count > 20 || vereinbarungen.Any(v => v.Length > 500))
            return BadRequest("Höchstens 20 Vereinbarungen mit je 500 Zeichen.");

        var mitarbeiterIds = (anlage.MitarbeiterIds ?? []).Distinct().ToList();
        if (mitarbeiterIds.Count > 0
            && await db.Mitarbeiter.CountAsync(m => mitarbeiterIds.Contains(m.Id), ct) != mitarbeiterIds.Count)
            return BadRequest("Unbekannter Mitarbeiter in der Auswahl.");
        var ansprechpartnerIds = (anlage.AnsprechpartnerIds ?? []).Distinct().ToList();
        if (ansprechpartnerIds.Count > 0
            && await db.Ansprechpartner.CountAsync(a => ansprechpartnerIds.Contains(a.Id) && a.KundeId == id, ct) != ansprechpartnerIds.Count)
            return BadRequest("Ein gewählter Ansprechpartner gehört nicht zu diesem Kunden.");
        var vorgang = await VorgangPruefenAsync(id, anlage.Vorgang, ct);

        var ich = await AktuellerMitarbeiterId(ct);
        if (mitarbeiterIds.Count == 0 && ich is not null)
            mitarbeiterIds.Add(ich.Value);
        var zeitpunkt = DateTime.SpecifyKind(anlage.Zeitpunkt, DateTimeKind.Local).ToUniversalTime();
        if (zeitpunkt > DateTime.UtcNow.AddDays(1))
            return BadRequest("Der Zeitpunkt liegt in der Zukunft — für Geplantes bitte eine Aufgabe anlegen.");

        var eintrag = new Aktivitaet
        {
            KundeId = id,
            Typ = typ,
            Zeitpunkt = zeitpunkt,
            Richtung = typ == AktivitaetsTyp.Anruf && anlage.Richtung == "Eingehend" ? AktivitaetsRichtung.Eingehend
                : typ == AktivitaetsTyp.Anruf ? AktivitaetsRichtung.Ausgehend : AktivitaetsRichtung.Intern,
            MitarbeiterId = Autor(ich, mitarbeiterIds),
            AnsprechpartnerId = ansprechpartnerIds.Count == 1 ? ansprechpartnerIds[0] : null,
            Betreff = betreff,
            Inhalt = string.IsNullOrWhiteSpace(anlage.Inhalt) ? null : anlage.Inhalt.Trim(),
            StrangSchluessel = vorgang?.Schluessel,
            StrangName = vorgang?.Name,
            Bezug = vorgang?.Name,
        };
        eintrag.Teilnehmer.AddRange(mitarbeiterIds.Select(m => new AktivitaetTeilnehmer { Seite = TeilnehmerSeite.Rausoft, MitarbeiterId = m }));
        eintrag.Teilnehmer.AddRange(ansprechpartnerIds.Select(a => new AktivitaetTeilnehmer { Seite = TeilnehmerSeite.Kunde, AnsprechpartnerId = a }));
        db.Aktivitaeten.Add(eintrag);
        foreach (var text in vereinbarungen)
            db.Vereinbarungen.Add(new Vereinbarung
            {
                KundeId = id, Text = text, Aktivitaet = eintrag, ErstelltAm = DateTime.UtcNow, ErstelltVonId = ich,
            });
        await db.SaveChangesAsync(ct);
        return Ok(eintrag.Id);
    }

    // --- Hilfen ------------------------------------------------------------------------

    /// <summary>Autor eines manuellen Eintrags: ich, wenn ich dabei war, sonst der erste gewählte Mitarbeiter.</summary>
    private static int? Autor(int? ich, List<int> mitarbeiterIds) =>
        ich is not null && mitarbeiterIds.Contains(ich.Value) ? ich
        : mitarbeiterIds.Count > 0 ? mitarbeiterIds[0]
        : null;

    private static string Kuerzen(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    /// <summary>Vorgänge des Kunden (Stränge aus dem Verlauf), jüngste zuerst.</summary>
    private async Task<List<VorgangAuswahl>> VorgaengeAsync(int kundeId, CancellationToken ct) =>
        (await db.Aktivitaeten.AsNoTracking()
            .Where(a => a.KundeId == kundeId && a.StrangSchluessel != null && a.StrangName != null)
            .GroupBy(a => new { a.StrangSchluessel, a.StrangName })
            .Select(g => new { g.Key.StrangSchluessel, g.Key.StrangName, Zuletzt = g.Max(a => a.Zeitpunkt) })
            .ToListAsync(ct))
        .Where(g => !g.StrangSchluessel!.StartsWith("partner:"))
        .OrderByDescending(g => g.Zuletzt)
        .Select(g => new VorgangAuswahl(g.StrangSchluessel!, g.StrangName!))
        .ToList();

    private async Task<VorgangAuswahl?> VorgangPruefenAsync(int kundeId, string? schluessel, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(schluessel)
            ? null
            : (await VorgaengeAsync(kundeId, ct)).FirstOrDefault(v => v.Schluessel == schluessel);

    private Task<int?> AktuellerMitarbeiterId(CancellationToken ct) =>
        benutzer.EntraObjectId is null && benutzer.WindowsKonto is null
            ? Task.FromResult<int?>(null)
            : db.Mitarbeiter.Where(Mitarbeiter.KontoFilter(benutzer.EntraObjectId, benutzer.WindowsKonto))
                .Select(m => (int?)m.Id).FirstOrDefaultAsync(ct);
}
