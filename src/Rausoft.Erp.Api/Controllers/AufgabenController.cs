using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Domain.Vorgaenge;
using Rausoft.Erp.Dtos;
using static Rausoft.Erp.Api.Dienste.VorgangsHilfen;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Meine Aufgaben / mein Bereich (ANFORDERUNGEN.md, Aufgabenverwaltung): was bei mir liegt,
/// was unbesetzt im Pool meines Bereichs liegt, auf Wunsch der ganze Bereich. Vorgangsschritte
/// sind gewöhnliche Aufgaben mit Link auf den Vorgang und dessen Hinweis.
/// Zugriff: jeder aktive Mitarbeiter.
/// </summary>
[ApiController]
[Route("api/aufgaben")]
public class AufgabenController(ErpDbContext db, IAktuellerBenutzer benutzer) : ControllerBase
{
    private const int MaxZeilen = 500;

    private static DateOnly Heute => DateOnly.FromDateTime(DateTime.Now);

    /// <param name="sicht">"meine" = bei mir + unbesetzt im Pool; "bereich" = alles Offene im Bereich.</param>
    /// <param name="bereichId">Anderer Bereich als der eigene (z. B. Vertretung); leer = eigener.</param>
    [HttpGet]
    public async Task<ActionResult<AufgabenUebersicht>> Liste(
        [FromQuery] string sicht = "meine", [FromQuery] int? bereichId = null, CancellationToken ct = default)
    {
        var ich = await IchAsync(db, benutzer, ct);
        var meinBereich = ich is null ? null
            : await db.Mitarbeiter.Where(m => m.Id == ich).Select(m => m.BereichId).FirstOrDefaultAsync(ct);
        var bereich = bereichId ?? meinBereich;

        var abfrage = db.Aufgaben.AsNoTracking().Where(Aufgabe.IstOffen);
        abfrage = sicht == "bereich"
            ? abfrage.Where(a => bereich != null
                                 && (a.ZustaendigBereichId == bereich
                                     || (a.ZustaendigBereichId == null && a.Zustaendig != null && a.Zustaendig.BereichId == bereich)))
            : abfrage.Where(a => (ich != null && a.ZustaendigId == ich)
                                 || (a.ZustaendigId == null && bereich != null && a.ZustaendigBereichId == bereich));

        var roh = await abfrage
            .OrderBy(a => a.Faellig == null).ThenBy(a => a.Faellig)
            .Take(MaxZeilen)
            .Select(a => new
            {
                a.Id, a.Titel, a.Art, a.Beschreibung, a.KundeId, a.Faellig, a.Wiedervorlage, a.Status,
                a.ZustaendigId, a.ZustaendigBereichId, a.VorgangId, a.Pflichtfeld, a.Bezug, a.Quelle, a.ZustaendigName,
                Kunde = a.Kunde != null ? a.Kunde.Firmenname : null,
                Kundennummer = a.Kunde != null ? a.Kunde.Kundennummer : null,
                Zustaendig = a.Zustaendig != null ? a.Zustaendig.Anzeigename : null,
                PersonBereichId = a.Zustaendig != null ? a.Zustaendig.BereichId : null,
                PersonBereich = a.Zustaendig != null && a.Zustaendig.Bereich != null ? a.Zustaendig.Bereich.Name : null,
                Pool = a.ZustaendigBereich != null ? a.ZustaendigBereich.Name : null,
                Vorgang = a.Vorgang != null ? a.Vorgang.Titel : null,
                Hinweis = a.Vorgang != null ? a.Vorgang.Hinweis : null,
            })
            .ToListAsync(ct);

        var heute = Heute;
        var zeilen = roh.Select(a =>
        {
            var wartet = a.Status == AufgabenStatus.WartetAufKunden;
            var datum = wartet ? a.Wiedervorlage ?? a.Faellig : a.Faellig;
            var wartetNoch = wartet && (a.Wiedervorlage is null || a.Wiedervorlage > heute);
            return new AufgabeZeile(
                a.Id, a.Titel, a.Art, a.Beschreibung, a.KundeId, a.Kunde, a.Kundennummer,
                datum, AufgabenGruppen.Gruppe(datum, wartetNoch, heute),
                a.Status switch
                {
                    AufgabenStatus.InArbeit => "inarbeit",
                    AufgabenStatus.WartetAufKunden => wartetNoch ? "wartet" : "wiedervorlage",
                    AufgabenStatus.Rueckfrage => "rueckfrage",
                    AufgabenStatus.Zurueckgestellt => "zurueckgestellt",
                    _ => "offen",
                },
                a.ZustaendigId, a.Zustaendig ?? a.ZustaendigName,
                a.ZustaendigBereichId ?? a.PersonBereichId, a.Pool ?? a.PersonBereich,
                a.ZustaendigId is null && a.ZustaendigBereichId is not null,
                a.VorgangId, a.Vorgang, a.Hinweis, a.Pflichtfeld, a.Bezug,
                a.Quelle == AktivitaetsQuelle.OdooImport ? "Odoo" : "Kundenzentrale");
        })
            .OrderBy(z => AufgabenGruppen.Reihenfolge(z.Gruppe))
            .ThenBy(z => z.Faellig ?? DateOnly.MaxValue)
            .ThenBy(z => z.Kunde)
            .ToList();

        var bereiche = await db.Bereiche.AsNoTracking().Where(b => b.Aktiv).OrderBy(b => b.Name)
            .Select(b => new AuswahlEintrag(b.Id, b.Name, null)).ToListAsync(ct);
        return Ok(new AufgabenUebersicht(zeilen, bereiche, ich, meinBereich, bereich));
    }

    /// <summary>Abzeichen in der Navigation: was bei mir oder unbesetzt in meinem Pool bis heute fällig ist.</summary>
    [HttpGet("zaehler")]
    public async Task<ActionResult<AufgabenZaehler>> Zaehler(CancellationToken ct)
    {
        var ich = await IchAsync(db, benutzer, ct);
        if (ich is null)
            return Ok(new AufgabenZaehler(0, 0));
        var bereich = await db.Mitarbeiter.Where(m => m.Id == ich).Select(m => m.BereichId).FirstOrDefaultAsync(ct);
        var heute = Heute;
        var daten = await db.Aufgaben.AsNoTracking()
            .Where(Aufgabe.IstOffen)
            .Where(a => a.ZustaendigId == ich || (a.ZustaendigId == null && bereich != null && a.ZustaendigBereichId == bereich))
            .Select(a => a.Status == AufgabenStatus.WartetAufKunden ? a.Wiedervorlage : a.Faellig)
            .Where(d => d != null && d <= heute)
            .ToListAsync(ct);
        return Ok(new AufgabenZaehler(daten.Count, daten.Count(d => d < heute)));
    }

    /// <summary>Aufgabe (ohne Vorgang) selbst übernehmen — Vorgangsschritte laufen über api/vorgaenge.</summary>
    [HttpPost("{id:int}/uebernehmen")]
    public async Task<IActionResult> Uebernehmen(int id, CancellationToken ct)
    {
        var aufgabe = await db.Aufgaben.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (aufgabe is null)
            return NotFound();
        if (aufgabe.VorgangId is not null)
            return BadRequest("Vorgangsschritte bitte über den Vorgang übernehmen.");
        if (aufgabe.Status is AufgabenStatus.Erledigt or AufgabenStatus.Entfallen)
            return BadRequest("Die Aufgabe ist schon erledigt.");
        var ich = await IchAsync(db, benutzer, ct);
        if (ich is null)
            return BadRequest("Kein Mitarbeiter-Datensatz zum angemeldeten Konto.");
        aufgabe.ZustaendigId = ich;
        aufgabe.ZustaendigName = null;
        aufgabe.Status = AufgabenStatus.InArbeit;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
