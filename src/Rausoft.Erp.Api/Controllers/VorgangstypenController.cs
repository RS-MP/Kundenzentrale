using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Domain.Vorgaenge;
using Rausoft.Erp.Dtos;
using static Rausoft.Erp.Api.Dienste.VorgangsHilfen;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Vorgangstypen (Vorlagen der Vorgänge). Lesen dürfen alle (für das Anlegen),
/// pflegen Admins und Bereichsleiter. Deaktivieren statt Löschen; laufende
/// Vorgänge behalten ihre Schritte, jede Änderung zählt den Stand hoch.
/// </summary>
[ApiController]
[Route("api/vorgangstypen")]
public class VorgangstypenController(ErpDbContext db, IAktuellerBenutzer benutzer) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<VorgangstypenUebersicht>> Liste(CancellationToken ct)
    {
        var laufend = await db.Vorgaenge.AsNoTracking().Where(v => v.AbgeschlossenAm == null)
            .GroupBy(v => v.VorgangstypId).Select(g => new { g.Key, Anzahl = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Anzahl, ct);
        var typen = await db.Vorgangstypen.AsNoTracking()
            .OrderByDescending(t => t.Aktiv).ThenBy(t => t.Name)
            .Select(t => new { t.Id, t.Name, t.Beschreibung, t.Aktiv, t.Stand, Schritte = t.Schritte.Count })
            .ToListAsync(ct);
        var ich = await IchAsync(db, benutzer, ct);
        return Ok(new VorgangstypenUebersicht(
            typen.Select(t => new VorgangstypEintrag(t.Id, t.Name, t.Beschreibung, t.Aktiv, t.Stand, t.Schritte,
                laufend.GetValueOrDefault(t.Id))).ToList(),
            await db.Bereiche.AsNoTracking().Where(b => b.Aktiv).OrderBy(b => b.Name)
                .Select(b => new AuswahlEintrag(b.Id, b.Name, b.Leiter != null ? b.Leiter.Anzeigename : null)).ToListAsync(ct),
            await DarfTypenPflegenAsync(db, ich, ct)));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<VorgangstypDetail>> Detail(int id, CancellationToken ct)
    {
        var t = await db.Vorgangstypen.AsNoTracking().Include(x => x.Schritte).Include(x => x.GeaendertVon)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null)
            return NotFound();
        var laufend = await db.Vorgaenge.CountAsync(v => v.VorgangstypId == id && v.AbgeschlossenAm == null, ct);
        return Ok(DetailDto(t, laufend));
    }

    private static VorgangstypDetail DetailDto(Vorgangstyp t, int laufend) => new(
        t.Id, t.Name, t.Beschreibung, t.Aktiv, t.Stand, t.GeaendertAm, t.GeaendertVon?.Anzeigename, laufend,
        VorgangsLogik.Bedingungen(t.Schritte),
        t.Schritte.OrderBy(s => s.Nr).Select(s => new VorgangstypSchrittDaten(
            s.Nr, s.Titel, s.BereichId, s.FristTage, s.WartetAufNr, s.Bedingung, s.Benachrichtigung.ToString(),
            s.Pflichtfeld, s.AutoAbhaken.ToString())).ToList());

    [HttpPost]
    public async Task<ActionResult<int>> Anlegen(VorgangstypSpeichern daten, CancellationToken ct)
    {
        var ich = await IchAsync(db, benutzer, ct);
        if (!await DarfTypenPflegenAsync(db, ich, ct))
            return Forbid();
        var (schritte, fehler) = await SchritteLesenAsync(daten, null, ct);
        if (fehler is not null)
            return BadRequest(fehler);
        var typ = new Vorgangstyp
        {
            Name = daten.Name.Trim(),
            Beschreibung = Beschreibung(daten.Beschreibung),
            GeaendertAm = DateTime.UtcNow,
            GeaendertVonId = ich,
            Schritte = schritte!,
        };
        db.Vorgangstypen.Add(typ);
        await db.SaveChangesAsync(ct);
        return Ok(typ.Id);
    }

    /// <summary>Name, Beschreibung und Schritte speichern; geänderte Schritte ergeben einen neuen Stand.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Speichern(int id, VorgangstypSpeichern daten, CancellationToken ct)
    {
        var ich = await IchAsync(db, benutzer, ct);
        if (!await DarfTypenPflegenAsync(db, ich, ct))
            return Forbid();
        var typ = await db.Vorgangstypen.Include(t => t.Schritte).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (typ is null)
            return NotFound();
        var (neu, fehler) = await SchritteLesenAsync(daten, id, ct);
        if (fehler is not null)
            return BadRequest(fehler);

        var alt = typ.Schritte.OrderBy(s => s.Nr).ToList();
        var geaendert = alt.Count != neu!.Count || alt.Zip(neu).Any(p => !Gleich(p.First, p.Second));
        // Zeilen nach Nummer weiterverwenden (eindeutiger Index Vorgangstyp + Nr), Überzählige löschen
        for (var i = 0; i < neu.Count; i++)
        {
            if (i < alt.Count)
                Uebernehmen(alt[i], neu[i]);
            else
                typ.Schritte.Add(neu[i]);
        }
        foreach (var weg in alt.Skip(neu.Count))
            db.VorgangstypSchritte.Remove(weg);

        var name = daten.Name.Trim();
        var beschreibung = Beschreibung(daten.Beschreibung);
        if (!geaendert && name == typ.Name && beschreibung == typ.Beschreibung)
            return NoContent();
        typ.Name = name;
        typ.Beschreibung = beschreibung;
        if (geaendert)
            typ.Stand++;
        typ.GeaendertAm = DateTime.UtcNow;
        typ.GeaendertVonId = ich;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:int}/kopie")]
    public async Task<ActionResult<int>> Kopieren(int id, VorgangstypKopieren kopie, CancellationToken ct)
    {
        var ich = await IchAsync(db, benutzer, ct);
        if (!await DarfTypenPflegenAsync(db, ich, ct))
            return Forbid();
        var quelle = await db.Vorgangstypen.AsNoTracking().Include(t => t.Schritte).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (quelle is null)
            return NotFound();
        var name = kopie.Name?.Trim() ?? "";
        if (name.Length is 0 or > 100)
            return BadRequest("Die Kopie braucht einen Namen (höchstens 100 Zeichen).");
        if (await db.Vorgangstypen.AnyAsync(t => t.Name == name, ct))
            return BadRequest($"Es gibt schon einen Vorgangstyp „{name}“.");
        var typ = new Vorgangstyp
        {
            Name = name,
            Beschreibung = quelle.Beschreibung,
            GeaendertAm = DateTime.UtcNow,
            GeaendertVonId = ich,
            Schritte = quelle.Schritte.OrderBy(s => s.Nr).Select(s =>
            {
                var k = new VorgangstypSchritt { Titel = s.Titel };
                Uebernehmen(k, s);
                return k;
            }).ToList(),
        };
        db.Vorgangstypen.Add(typ);
        await db.SaveChangesAsync(ct);
        return Ok(typ.Id);
    }

    [HttpPut("{id:int}/aktiv")]
    public async Task<IActionResult> Aktiv(int id, AktivSetzen setzen, CancellationToken ct)
    {
        var ich = await IchAsync(db, benutzer, ct);
        if (!await DarfTypenPflegenAsync(db, ich, ct))
            return Forbid();
        var typ = await db.Vorgangstypen.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (typ is null)
            return NotFound();
        if (typ.Aktiv == setzen.Aktiv)
            return NoContent();
        if (!setzen.Aktiv && !await db.Vorgangstypen.AnyAsync(t => t.Aktiv && t.Id != id, ct))
            return BadRequest("Mindestens ein Vorgangstyp muss aktiv bleiben.");
        typ.Aktiv = setzen.Aktiv;
        typ.GeaendertAm = DateTime.UtcNow;
        typ.GeaendertVonId = ich;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Die vier Standardtypen (Entscheidung 28.10.2026) als Startpunkt anlegen — nur solange
    /// es noch keinen Vorgangstyp gibt. Fehlende Bereiche (Vertrieb, Technik, Buchhaltung,
    /// Support) werden dabei angelegt.
    /// </summary>
    [HttpPost("standard")]
    public async Task<IActionResult> StandardAnlegen(CancellationToken ct)
    {
        var ich = await IchAsync(db, benutzer, ct);
        if (!await DarfTypenPflegenAsync(db, ich, ct))
            return Forbid();
        if (await db.Vorgangstypen.AnyAsync(ct))
            return BadRequest("Es gibt schon Vorgangstypen — weitere bitte einzeln anlegen.");

        var namen = VorgangstypStandard.Alle.SelectMany(t => t.Schritte).Select(s => s.Bereich).Distinct().ToList();
        var bereiche = await db.Bereiche.Where(b => namen.Contains(b.Name)).ToListAsync(ct);
        foreach (var fehlt in namen.Where(n => bereiche.All(b => b.Name != n)))
        {
            var b = new Bereich { Name = fehlt };
            db.Bereiche.Add(b);
            bereiche.Add(b);
        }
        foreach (var b in bereiche.Where(b => !b.Aktiv))
            b.Aktiv = true;

        foreach (var t in VorgangstypStandard.Alle)
            db.Vorgangstypen.Add(new Vorgangstyp
            {
                Name = t.Name,
                Beschreibung = t.Beschreibung,
                GeaendertAm = DateTime.UtcNow,
                GeaendertVonId = ich,
                Schritte = t.Schritte.Select((s, i) => new VorgangstypSchritt
                {
                    Nr = i + 1,
                    Titel = s.Titel,
                    Bereich = bereiche.First(b => b.Name == s.Bereich),
                    FristTage = s.FristTage,
                    WartetAufNr = s.WartetAufNr,
                    Bedingung = s.Bedingung,
                    Benachrichtigung = s.Benachrichtigung,
                    Pflichtfeld = s.Pflichtfeld,
                    AutoAbhaken = s.AutoAbhaken,
                }).ToList(),
            });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // --- Hilfen --------------------------------------------------------------------

    private async Task<(List<VorgangstypSchritt>? Schritte, string? Fehler)> SchritteLesenAsync(
        VorgangstypSpeichern daten, int? eigeneId, CancellationToken ct)
    {
        var schritte = new List<VorgangstypSchritt>();
        var liste = daten.Schritte ?? [];
        for (var i = 0; i < liste.Count; i++)
        {
            var s = liste[i];
            if (!Enum.TryParse<Benachrichtigung>(s.Benachrichtigung, true, out var info))
                return (null, $"Schritt {i + 1}: unbekannte Benachrichtigung „{s.Benachrichtigung}“.");
            if (!Enum.TryParse<AutoAbhaken>(s.AutoAbhaken, true, out var auto))
                return (null, $"Schritt {i + 1}: unbekannte Auswahl „{s.AutoAbhaken}“ bei „Hakt sich ab bei“.");
            schritte.Add(new VorgangstypSchritt
            {
                Nr = i + 1,
                Titel = s.Titel?.Trim() ?? "",
                BereichId = s.BereichId,
                FristTage = s.FristTage,
                WartetAufNr = s.WartetAufNr,
                Bedingung = string.IsNullOrWhiteSpace(s.Bedingung) ? null : s.Bedingung.Trim(),
                Benachrichtigung = info,
                Pflichtfeld = string.IsNullOrWhiteSpace(s.Pflichtfeld) ? null : s.Pflichtfeld.Trim(),
                AutoAbhaken = auto,
            });
        }
        var fehler = VorgangsLogik.VorlagePruefen(daten.Name, schritte);
        if (fehler is not null)
            return (null, fehler);
        if (daten.Beschreibung is { Length: > 300 })
            return (null, "Die Beschreibung darf höchstens 300 Zeichen lang sein.");
        var bereichIds = schritte.Select(s => s.BereichId).Distinct().ToList();
        if (await db.Bereiche.CountAsync(b => bereichIds.Contains(b.Id) && b.Aktiv, ct) != bereichIds.Count)
            return (null, "Ein gewählter Bereich existiert nicht oder ist deaktiviert.");
        var name = daten.Name.Trim();
        if (await db.Vorgangstypen.AnyAsync(t => t.Name == name && t.Id != eigeneId, ct))
            return (null, $"Es gibt schon einen Vorgangstyp „{name}“.");
        return (schritte, null);
    }

    private static string? Beschreibung(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static bool Gleich(VorgangstypSchritt a, VorgangstypSchritt b) =>
        a.Titel == b.Titel && a.BereichId == b.BereichId && a.FristTage == b.FristTage && a.WartetAufNr == b.WartetAufNr
        && a.Bedingung == b.Bedingung && a.Benachrichtigung == b.Benachrichtigung && a.Pflichtfeld == b.Pflichtfeld
        && a.AutoAbhaken == b.AutoAbhaken;

    private static void Uebernehmen(VorgangstypSchritt ziel, VorgangstypSchritt quelle)
    {
        ziel.Nr = quelle.Nr;
        ziel.Titel = quelle.Titel;
        ziel.BereichId = quelle.BereichId;
        ziel.FristTage = quelle.FristTage;
        ziel.WartetAufNr = quelle.WartetAufNr;
        ziel.Bedingung = quelle.Bedingung;
        ziel.Benachrichtigung = quelle.Benachrichtigung;
        ziel.Pflichtfeld = quelle.Pflichtfeld;
        ziel.AutoAbhaken = quelle.AutoAbhaken;
    }
}
