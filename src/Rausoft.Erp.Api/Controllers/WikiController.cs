using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Domain.Wiki;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Wiki (FEEDBACK Nr. 22): „wie funktioniert was bei uns und wie nennen wir es".
/// Erster Bereich: Sanktionslisten-Katalog. Lesen dürfen alle Mitarbeiter,
/// pflegen Compliance und Admin.
/// </summary>
[ApiController]
[Route("api/wiki")]
public class WikiController(ErpDbContext db, IAktuellerBenutzer benutzer, IAuthorizationService autorisierung)
    : ControllerBase
{
    /// <summary>Regionen in Anzeigereihenfolge — DE und EU zuerst, dann nach Bedeutung für unsere Kunden.</summary>
    internal static readonly (string Code, string Name)[] Regionen =
    [
        ("DE", "Deutschland"),
        ("EU", "Europäische Union"),
        ("UN", "Vereinte Nationen"),
        ("US", "USA"),
        ("GB", "Vereinigtes Königreich"),
        ("CH", "Schweiz"),
        ("AT", "Österreich"),
        ("FR", "Frankreich"),
        ("BE", "Belgien"),
        ("NL", "Niederlande"),
        ("PL", "Polen"),
        ("CZ", "Tschechien"),
        ("CA", "Kanada"),
        ("AU", "Australien"),
        ("JP", "Japan"),
        ("CN", "China"),
        ("SG", "Singapur"),
        ("INT", "Internationale Organisationen"),
    ];

    [HttpGet("sanktionslisten")]
    public async Task<ActionResult<SanktionslistenKatalog>> Sanktionslisten(CancellationToken ct)
    {
        var listen = await db.Sanktionslisten.AsNoTracking()
            .Include(s => s.Verantwortlich).Include(s => s.Vertreter).Include(s => s.GeaendertVon)
            .ToListAsync(ct);

        var reihenfolge = Regionen.Select((r, i) => (r.Code, i)).ToDictionary(x => x.Code, x => x.i);
        var eintraege = listen
            .OrderBy(s => reihenfolge.GetValueOrDefault(s.Region, int.MaxValue))
            .ThenByDescending(s => s.Standardumfang)
            .ThenBy(s => s.Kuerzel ?? s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(NachEintrag)
            .ToList();

        return Ok(new SanktionslistenKatalog(eintraege, await DarfBearbeitenAsync(),
            Regionen.Select(r => new RegionEintrag(r.Code, r.Name)).ToList()));
    }

    /// <summary>Aktive Benutzer für Verantwortlich/Vertreter.</summary>
    [HttpGet("personen")]
    public async Task<ActionResult<List<PersonAuswahl>>> Personen(CancellationToken ct) =>
        Ok(await db.Mitarbeiter.AsNoTracking()
            .Where(m => m.Aktiv)
            .OrderBy(m => m.Anzeigename)
            .Select(m => new PersonAuswahl(m.Id, m.Anzeigename))
            .ToListAsync(ct));

    [Authorize(Policy = "Compliance")]
    [HttpPost("sanktionslisten")]
    public async Task<ActionResult<SanktionslisteEintrag>> Anlegen(SanktionslisteSpeichern daten, CancellationToken ct)
    {
        var liste = new Sanktionsliste { Region = "", Name = "" };
        if (await UebernehmenAsync(liste, daten, ct) is { } fehler)
            return BadRequest(fehler);
        db.Sanktionslisten.Add(liste);
        await db.SaveChangesAsync(ct);
        return Ok(NachEintrag(liste));
    }

    [Authorize(Policy = "Compliance")]
    [HttpPut("sanktionslisten/{id:int}")]
    public async Task<IActionResult> Aendern(int id, SanktionslisteSpeichern daten, CancellationToken ct)
    {
        var liste = await db.Sanktionslisten.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (liste is null)
            return NotFound();
        if (await UebernehmenAsync(liste, daten, ct) is { } fehler)
            return BadRequest(fehler);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // --- Hilfen ----------------------------------------------------------------

    private async Task<string?> UebernehmenAsync(Sanktionsliste liste, SanktionslisteSpeichern d, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(d.Name))
            return "Der Name darf nicht leer sein.";
        if (!Regionen.Any(r => r.Code == d.Region))
            return $"Unbekannte Region '{d.Region}'.";
        if (!Enum.TryParse<SanktionslistenImport>(d.Import, out var import))
            return $"Unbekannte Importart '{d.Import}'.";
        if (!string.IsNullOrWhiteSpace(d.DownloadUrl)
            && !(Uri.TryCreate(d.DownloadUrl.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
            return "Der Download-Link muss eine http(s)-Adresse sein.";
        foreach (var personId in new[] { d.VerantwortlichId, d.VertreterId }.OfType<int>())
        {
            if (!await db.Mitarbeiter.AnyAsync(m => m.Id == personId, ct))
                return "Die gewählte Person existiert nicht.";
        }
        if (d.VerantwortlichId is not null && d.VerantwortlichId == d.VertreterId)
            return "Verantwortlich und Vertreter müssen verschiedene Personen sein.";
        var name = d.Name.Trim();
        if (await db.Sanktionslisten.AnyAsync(s => s.Id != liste.Id && s.Region == d.Region && s.Name == name, ct))
            return $"In der Region {d.Region} gibt es bereits eine Liste „{name}“.";

        liste.Region = d.Region;
        liste.Kuerzel = Leer(d.Kuerzel);
        liste.Name = name;
        liste.BezeichnungIntern = Leer(d.BezeichnungIntern);
        liste.Aliase = Leer(d.Aliase);
        liste.Beschreibung = Leer(d.Beschreibung);
        liste.Art = Leer(d.Art);
        liste.Herausgeber = Leer(d.Herausgeber);
        liste.Geltungsbereich = Leer(d.Geltungsbereich);
        liste.Standardumfang = d.Standardumfang;
        liste.DownloadUrl = Leer(d.DownloadUrl);
        liste.Import = import;
        liste.ImportHinweis = Leer(d.ImportHinweis);
        liste.VerantwortlichId = d.VerantwortlichId;
        liste.VertreterId = d.VertreterId;
        liste.Prozess = Leer(d.Prozess);
        liste.Aktiv = d.Aktiv;
        liste.GeaendertAm = DateTime.UtcNow;
        liste.GeaendertVonId = await db.Mitarbeiter
            .Where(Mitarbeiter.KontoFilter(benutzer.EntraObjectId, benutzer.WindowsKonto))
            .Select(m => (int?)m.Id).FirstOrDefaultAsync(ct);
        return null;
    }

    private static string? Leer(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private async Task<bool> DarfBearbeitenAsync() =>
        (await autorisierung.AuthorizeAsync(User, "Compliance")).Succeeded;

    private static SanktionslisteEintrag NachEintrag(Sanktionsliste s) => new(
        s.Id, s.Region, Regionen.FirstOrDefault(r => r.Code == s.Region).Name ?? s.Region,
        s.Kuerzel, s.Name, s.BezeichnungIntern, s.Aliase, s.Beschreibung, s.Art, s.Herausgeber,
        s.Geltungsbereich, s.Standardumfang, s.DownloadUrl, s.Import.ToString(), s.ImportHinweis,
        s.VerantwortlichId, s.Verantwortlich?.Anzeigename, s.VertreterId, s.Vertreter?.Anzeigename,
        s.Prozess, s.Aktiv, s.GeaendertAm, s.GeaendertVon?.Anzeigename);
}
