using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Wiki;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Wiki: Neuerungen je Thema mit Lese-/Bestätigungsprotokoll und Meldungen
/// („etwas fehlt oder ist falsch") an die Zuständigen. Lesen und melden darf
/// jeder Mitarbeiter; anlegen/erledigen die Redaktion des Themas bzw.
/// Verantwortlicher und Vertretung.
/// </summary>
[ApiController]
[Route("api/wiki")]
public class WikiNeuerungenController(ErpDbContext db, Dienste.WikiBerechtigung berechtigung) : ControllerBase
{
    // --- Neuerungen ------------------------------------------------------------

    [HttpGet("themen/{thema}/neuerungen")]
    public async Task<ActionResult<List<WikiNeuerungEintrag>>> Neuerungen(string thema, CancellationToken ct)
    {
        if (!WikiThema.IstBekannt(thema))
            return NotFound();
        var eigeneId = await berechtigung.EigeneIdAsync(ct);
        var zustaendig = await berechtigung.IstZustaendigAsync(thema, ct);

        var neuerungen = await db.WikiNeuerungen.AsNoTracking()
            .Include(n => n.ErstelltVon).Include(n => n.Lesungen).Include(n => n.PreislistenVersion)
            .Where(n => n.Thema == thema)
            .OrderByDescending(n => n.ErstelltAm)
            .ToListAsync(ct);
        var leser = await db.Mitarbeiter.AsNoTracking()
            .Where(m => m.Aktiv).OrderBy(m => m.Anzeigename)
            .Select(m => new { m.Id, m.Anzeigename }).ToListAsync(ct);

        return Ok(neuerungen.Select(n =>
        {
            var meine = n.Lesungen.FirstOrDefault(l => l.MitarbeiterId == eigeneId);
            var aktiv = n.Lesungen.Where(l => leser.Any(m => m.Id == l.MitarbeiterId)).ToList();
            return new WikiNeuerungEintrag(
                n.Id, n.Thema, n.Titel, n.Text, n.PreislistenVersionId is null ? null : n.PreislistenVersion?.Version,
                n.BestaetigungErforderlich, n.ErstelltAm, n.ErstelltVon?.Anzeigename,
                meine?.GesehenAm, meine?.BestaetigtAm,
                leser.Count, aktiv.Count, aktiv.Count(l => l.BestaetigtAm != null),
                zustaendig
                    ? leser.Select(m =>
                    {
                        var l = n.Lesungen.FirstOrDefault(x => x.MitarbeiterId == m.Id);
                        return new WikiLesung(m.Anzeigename, l?.GesehenAm, l?.BestaetigtAm);
                    }).ToList()
                    : null);
        }).ToList());
    }

    [HttpPost("themen/{thema}/neuerungen")]
    public async Task<ActionResult<int>> NeuerungAnlegen(string thema, WikiNeuerungSpeichern daten, CancellationToken ct)
    {
        if (!WikiThema.IstBekannt(thema))
            return NotFound();
        if (!await berechtigung.DarfBearbeitenAsync(thema, ct))
            return StatusCode(StatusCodes.Status403Forbidden, "Neuerungen erfassen dürfen nur die Redaktion des Themas, Compliance und Admin.");
        if (Pruefen(daten) is { } fehler)
            return BadRequest(fehler);

        var n = new WikiNeuerung
        {
            Thema = thema,
            Titel = daten.Titel.Trim(),
            Text = daten.Text.Trim(),
            BestaetigungErforderlich = daten.BestaetigungErforderlich,
            ErstelltAm = DateTime.UtcNow,
            ErstelltVonId = await berechtigung.EigeneIdAsync(ct),
        };
        db.WikiNeuerungen.Add(n);
        await db.SaveChangesAsync(ct);
        return Ok(n.Id);
    }

    [HttpPut("themen/{thema}/neuerungen/{id:int}")]
    public async Task<IActionResult> NeuerungAendern(string thema, int id, WikiNeuerungSpeichern daten, CancellationToken ct)
    {
        if (!await berechtigung.DarfBearbeitenAsync(thema, ct))
            return StatusCode(StatusCodes.Status403Forbidden, "Neuerungen ändern dürfen nur die Redaktion des Themas, Compliance und Admin.");
        if (Pruefen(daten) is { } fehler)
            return BadRequest(fehler);
        var n = await db.WikiNeuerungen.FirstOrDefaultAsync(x => x.Id == id && x.Thema == thema, ct);
        if (n is null)
            return NotFound();
        n.Titel = daten.Titel.Trim();
        n.Text = daten.Text.Trim();
        n.BestaetigungErforderlich = daten.BestaetigungErforderlich;
        n.GeaendertAm = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("themen/{thema}/neuerungen/{id:int}")]
    public async Task<IActionResult> NeuerungLoeschen(string thema, int id, CancellationToken ct)
    {
        if (!await berechtigung.DarfBearbeitenAsync(thema, ct))
            return StatusCode(StatusCodes.Status403Forbidden, "Neuerungen löschen dürfen nur die Redaktion des Themas, Compliance und Admin.");
        var n = await db.WikiNeuerungen.FirstOrDefaultAsync(x => x.Id == id && x.Thema == thema, ct);
        if (n is null)
            return NotFound();
        db.WikiNeuerungen.Remove(n);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Der angemeldete Benutzer hat die Neuerung angezeigt bekommen (erstes Mal wird festgehalten).</summary>
    [HttpPost("themen/{thema}/neuerungen/{id:int}/gesehen")]
    public Task<IActionResult> Gesehen(string thema, int id, CancellationToken ct) => LesungAsync(thema, id, bestaetigen: false, ct);

    /// <summary>Der angemeldete Benutzer bestätigt ausdrücklich, die Neuerung gelesen zu haben.</summary>
    [HttpPost("themen/{thema}/neuerungen/{id:int}/bestaetigt")]
    public Task<IActionResult> Bestaetigt(string thema, int id, CancellationToken ct) => LesungAsync(thema, id, bestaetigen: true, ct);

    private async Task<IActionResult> LesungAsync(string thema, int id, bool bestaetigen, CancellationToken ct)
    {
        var eigeneId = await berechtigung.EigeneIdAsync(ct);
        if (eigeneId is null)
            return StatusCode(StatusCodes.Status403Forbidden, "Kein Mitarbeiter-Datensatz für dieses Konto.");
        if (!await db.WikiNeuerungen.AnyAsync(n => n.Id == id && n.Thema == thema, ct))
            return NotFound();

        var lesung = await db.WikiNeuerungLesungen.FirstOrDefaultAsync(l => l.NeuerungId == id && l.MitarbeiterId == eigeneId, ct);
        if (lesung is null)
        {
            lesung = new WikiNeuerungLesung { NeuerungId = id, MitarbeiterId = eigeneId.Value, GesehenAm = DateTime.UtcNow };
            db.WikiNeuerungLesungen.Add(lesung);
        }
        if (bestaetigen && lesung.BestaetigtAm is null)
            lesung.BestaetigtAm = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static string? Pruefen(WikiNeuerungSpeichern d)
    {
        if (string.IsNullOrWhiteSpace(d.Titel))
            return "Bitte einen Titel angeben.";
        if (d.Titel.Trim().Length > 200)
            return "Der Titel ist zu lang (höchstens 200 Zeichen).";
        if (string.IsNullOrWhiteSpace(d.Text))
            return "Bitte beschreiben, was sich geändert hat.";
        return null;
    }

    // --- Meldungen -------------------------------------------------------------

    [HttpGet("themen/{thema}/meldungen")]
    public async Task<ActionResult<WikiMeldungenAntwort>> Meldungen(string thema, CancellationToken ct)
    {
        if (!WikiThema.IstBekannt(thema))
            return NotFound();
        var eintraege = await db.WikiMeldungen.AsNoTracking()
            .Include(m => m.ErstelltVon).Include(m => m.ErledigtVon)
            .Where(m => m.Thema == thema)
            .OrderBy(m => m.ErledigtAm != null).ThenByDescending(m => m.ErstelltAm)
            .Select(m => new WikiMeldungEintrag(m.Id, m.Thema, m.Bezug, m.Text, m.ErstelltAm,
                m.ErstelltVon != null ? m.ErstelltVon.Anzeigename : null,
                m.ErledigtAm, m.ErledigtVon != null ? m.ErledigtVon.Anzeigename : null, m.Antwort))
            .ToListAsync(ct);
        var info = await db.WikiThemaInfos.AsNoTracking()
            .Include(i => i.Verantwortlich).Include(i => i.Vertreter)
            .FirstOrDefaultAsync(i => i.Thema == thema, ct);
        return Ok(new WikiMeldungenAntwort(eintraege, await berechtigung.IstZustaendigAsync(thema, ct),
            info?.Verantwortlich?.Anzeigename, info?.Vertreter?.Anzeigename));
    }

    /// <summary>Jeder Mitarbeiter darf melden — die Zuständigen sehen die Meldung sofort in Navigation und Themenseite.</summary>
    [HttpPost("themen/{thema}/meldungen")]
    public async Task<ActionResult<int>> Melden(string thema, WikiMeldungSpeichern daten, CancellationToken ct)
    {
        if (!WikiThema.IstBekannt(thema))
            return NotFound();
        if (string.IsNullOrWhiteSpace(daten.Text))
            return BadRequest("Bitte beschreiben, was fehlt oder falsch ist.");
        var eigeneId = await berechtigung.EigeneIdAsync(ct);
        if (eigeneId is null)
            return StatusCode(StatusCodes.Status403Forbidden, "Kein Mitarbeiter-Datensatz für dieses Konto.");

        var m = new WikiMeldung
        {
            Thema = thema,
            Bezug = string.IsNullOrWhiteSpace(daten.Bezug) ? null : daten.Bezug.Trim()[..Math.Min(200, daten.Bezug.Trim().Length)],
            Text = daten.Text.Trim(),
            ErstelltAm = DateTime.UtcNow,
            ErstelltVonId = eigeneId,
        };
        db.WikiMeldungen.Add(m);
        await db.SaveChangesAsync(ct);
        return Ok(m.Id);
    }

    [HttpPut("themen/{thema}/meldungen/{id:int}/erledigt")]
    public async Task<IActionResult> Erledigen(string thema, int id, WikiMeldungErledigen daten, CancellationToken ct)
    {
        if (!await berechtigung.IstZustaendigAsync(thema, ct))
            return StatusCode(StatusCodes.Status403Forbidden, "Meldungen erledigen dürfen Verantwortlicher, Vertretung und die Redaktion des Themas.");
        var m = await db.WikiMeldungen.FirstOrDefaultAsync(x => x.Id == id && x.Thema == thema, ct);
        if (m is null)
            return NotFound();
        m.ErledigtAm = DateTime.UtcNow;
        m.ErledigtVonId = await berechtigung.EigeneIdAsync(ct);
        m.Antwort = string.IsNullOrWhiteSpace(daten.Antwort) ? null : daten.Antwort.Trim();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Wieder öffnen, falls eine Meldung zu früh erledigt wurde.</summary>
    [HttpPut("themen/{thema}/meldungen/{id:int}/offen")]
    public async Task<IActionResult> WiederOeffnen(string thema, int id, CancellationToken ct)
    {
        if (!await berechtigung.IstZustaendigAsync(thema, ct))
            return StatusCode(StatusCodes.Status403Forbidden, "Meldungen bearbeiten dürfen Verantwortlicher, Vertretung und die Redaktion des Themas.");
        var m = await db.WikiMeldungen.FirstOrDefaultAsync(x => x.Id == id && x.Thema == thema, ct);
        if (m is null)
            return NotFound();
        m.ErledigtAm = null;
        m.ErledigtVonId = null;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // --- Hinweise für die Navigation -------------------------------------------

    /// <summary>Was wartet auf den angemeldeten Benutzer: unbestätigte Pflicht-Neuerungen und offene Meldungen seiner Themen.</summary>
    [HttpGet("hinweise")]
    public async Task<ActionResult<WikiHinweise>> Hinweise(CancellationToken ct)
    {
        var eigeneId = await berechtigung.EigeneIdAsync(ct);
        if (eigeneId is null)
            return Ok(new WikiHinweise(0, 0));

        var unbestaetigt = await db.WikiNeuerungen.AsNoTracking()
            .CountAsync(n => n.BestaetigungErforderlich
                             && !n.Lesungen.Any(l => l.MitarbeiterId == eigeneId && l.BestaetigtAm != null), ct);
        var themen = await berechtigung.ZustaendigeThemenAsync(ct);
        var offen = themen.Count == 0 ? 0 : await db.WikiMeldungen.AsNoTracking()
            .CountAsync(m => m.ErledigtAm == null && themen.Contains(m.Thema), ct);
        return Ok(new WikiHinweise(unbestaetigt, offen));
    }
}
