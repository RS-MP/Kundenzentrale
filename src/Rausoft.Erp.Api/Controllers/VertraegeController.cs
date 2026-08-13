using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Contracts;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

[ApiController]
[Route("api/vertraege")]
public class VertraegeController(ErpDbContext db, NummernkreisDienst nummernkreis, IAktuellerBenutzer benutzer)
    : ControllerBase
{
    private const int MaxProSeite = 100;

    [HttpGet]
    public async Task<ActionResult<SeitenErgebnis<VertragListenEintrag>>> Liste(
        [FromQuery] string? suche, [FromQuery] string? status, [FromQuery] string? bestandteil,
        [FromQuery] int? kundeId, [FromQuery] int seite = 1, [FromQuery] int proSeite = 25,
        CancellationToken ct = default)
    {
        seite = Math.Max(1, seite);
        proSeite = Math.Clamp(proSeite, 1, MaxProSeite);

        var query = db.Vertraege.AsNoTracking();

        if (kundeId is not null)
            query = query.Where(v => v.KundeId == kundeId);
        if (!string.IsNullOrWhiteSpace(suche))
        {
            var s = suche.Trim();
            query = query.Where(v => v.Vertragsnummer.Contains(s)
                || v.Kunde!.Firmenname.Contains(s) || v.Kunde!.Kundennummer.Contains(s));
        }
        if (StatusWert(status) is { } statusWert)
            query = query.Where(v => v.Status == statusWert);
        // Explizite Anforderung: „Wer hat eine Sondervereinbarung?" — Filter über den Bestandteil-Typ
        if (BestandteilWert(bestandteil) is { } typWert)
            query = query.Where(v => v.Bestandteile.Any(b => b.Typ == typWert));

        var gesamt = await query.CountAsync(ct);
        var roh = await query
            .OrderBy(v => v.Vertragsnummer)
            .Skip((seite - 1) * proSeite)
            .Take(proSeite)
            .Select(v => new
            {
                v.Id, v.Vertragsnummer, v.KundeId,
                KundeName = v.Kunde!.Firmenname,
                v.Kunde!.Kundennummer,
                v.Status, v.Beginn, v.MindestlaufzeitMonate, v.VerlaengerungMonate,
                v.KuendigungsfristMonate, v.GekuendigtZum,
                Typen = v.Bestandteile.Select(b => b.Typ).ToList(),
            })
            .ToListAsync(ct);

        var heute = DateOnly.FromDateTime(DateTime.Today);
        var eintraege = roh.Select(v => new VertragListenEintrag(
                v.Id, v.Vertragsnummer, v.KundeId, v.KundeName, v.Kundennummer,
                StatusText(v.Status), v.Beginn,
                new Vertrag
                {
                    Vertragsnummer = v.Vertragsnummer,
                    Beginn = v.Beginn,
                    MindestlaufzeitMonate = v.MindestlaufzeitMonate,
                    VerlaengerungMonate = v.VerlaengerungMonate,
                    KuendigungsfristMonate = v.KuendigungsfristMonate,
                    GekuendigtZum = v.GekuendigtZum,
                }.NaechstesVertragsende(heute),
                v.Typen.Distinct().Select(BestandteilText).ToList()))
            .ToList();

        return Ok(new SeitenErgebnis<VertragListenEintrag>(eintraege, gesamt, seite, proSeite));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<VertragDetail>> Einzeln(int id, CancellationToken ct)
    {
        var vertrag = await db.Vertraege
            .AsNoTracking()
            .Include(v => v.Kunde)
            .Include(v => v.Bestandteile)
                .ThenInclude(b => b.Dokumente)
                    .ThenInclude(d => d.ErstelltVon)
            .FirstOrDefaultAsync(v => v.Id == id, ct);
        if (vertrag is null)
            return NotFound();

        return Ok(NachDetail(vertrag));
    }

    [HttpPost]
    public async Task<ActionResult<VertragDetail>> Anlegen(VertragAnlegen anlage, CancellationToken ct)
    {
        var kunde = await db.Kunden.FindAsync([anlage.KundeId], ct);
        if (kunde is null)
            return BadRequest("Der angegebene Kunde existiert nicht.");

        // Nummer zuerst ziehen (eigener SaveChanges), dann den Vertrag anlegen
        var nummer = await nummernkreis.NaechsteNummerAsync("Vertrag", ct);

        var vertrag = new Vertrag
        {
            Vertragsnummer = nummer,
            KundeId = kunde.Id,
            Status = VertragsStatus.Entwurf,
            Beginn = anlage.Beginn,
            MindestlaufzeitMonate = anlage.MindestlaufzeitMonate,
            VerlaengerungMonate = anlage.VerlaengerungMonate,
            KuendigungsfristMonate = anlage.KuendigungsfristMonate,
            Notiz = anlage.Notiz,
        };
        db.Vertraege.Add(vertrag);

        db.Aktivitaeten.Add(await TimelineEintrag(kunde.Id,
            $"Vertrag {nummer} angelegt", null, ct));
        await db.SaveChangesAsync(ct);

        vertrag.Kunde = kunde;
        return CreatedAtAction(nameof(Einzeln), new { id = vertrag.Id }, NachDetail(vertrag));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Aendern(int id, VertragAendern aenderung, CancellationToken ct)
    {
        var vertrag = await db.Vertraege.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (vertrag is null)
            return NotFound();
        if (StatusWert(aenderung.Status) is not { } neuerStatus)
            return BadRequest($"Unbekannter Status '{aenderung.Status}'.");

        var alterStatus = vertrag.Status;
        vertrag.Status = neuerStatus;
        vertrag.Beginn = aenderung.Beginn;
        vertrag.MindestlaufzeitMonate = aenderung.MindestlaufzeitMonate;
        vertrag.VerlaengerungMonate = aenderung.VerlaengerungMonate;
        vertrag.KuendigungsfristMonate = aenderung.KuendigungsfristMonate;
        vertrag.GekuendigtZum = aenderung.GekuendigtZum;
        vertrag.Notiz = aenderung.Notiz;

        if (alterStatus != neuerStatus)
        {
            db.Aktivitaeten.Add(await TimelineEintrag(vertrag.KundeId,
                $"Vertrag {vertrag.Vertragsnummer}: {StatusText(alterStatus)} → {StatusText(neuerStatus)}", null, ct));
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:int}/bestandteile")]
    public async Task<IActionResult> BestandteilAnlegen(int id, BestandteilAnlegen anlage, CancellationToken ct)
    {
        var vertrag = await db.Vertraege.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (vertrag is null)
            return NotFound();
        if (BestandteilWert(anlage.Typ) is not { } typ)
            return BadRequest($"Unbekannter Bestandteil-Typ '{anlage.Typ}'.");
        if (string.IsNullOrWhiteSpace(anlage.Titel))
            return BadRequest("Der Titel darf nicht leer sein.");

        db.Vertragsbestandteile.Add(new Vertragsbestandteil
        {
            VertragId = vertrag.Id,
            Typ = typ,
            Titel = anlage.Titel.Trim(),
            Beschreibung = anlage.Beschreibung,
            UnterzeichnetAm = anlage.UnterzeichnetAm,
        });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Registriert eine neue, unveränderliche Dokumentversion: Die Datei liegt
    /// bereits im Vertragsablage-Verzeichnis (Admin-Einstellung); die API prüft
    /// den Pfad, berechnet die SHA-256-Prüfsumme und vergibt die nächste Version.
    /// </summary>
    [HttpPost("bestandteile/{bestandteilId:int}/dokumente")]
    public async Task<IActionResult> DokumentRegistrieren(
        int bestandteilId, DokumentRegistrieren registrierung, CancellationToken ct)
    {
        var bestandteil = await db.Vertragsbestandteile
            .Include(b => b.Vertrag)
            .Include(b => b.Dokumente)
            .FirstOrDefaultAsync(b => b.Id == bestandteilId, ct);
        if (bestandteil is null)
            return NotFound();

        var basis = (await db.Einstellungen.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Schluessel == EinstellungsSchluessel.VertragsablageVerzeichnis, ct))
            ?.Wert;
        if (string.IsNullOrWhiteSpace(basis))
            return BadRequest("Das Vertragsablage-Verzeichnis ist nicht konfiguriert (Administration → Einstellungen).");

        // Pfad-Sicherheit: nur Dateien unterhalb des Basisverzeichnisses
        var basisVoll = Path.GetFullPath(basis);
        var dateiVoll = Path.GetFullPath(Path.Combine(basisVoll, registrierung.RelativerPfad));
        if (!dateiVoll.StartsWith(basisVoll.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            return BadRequest("Der Pfad liegt außerhalb des Vertragsablage-Verzeichnisses.");
        if (!System.IO.File.Exists(dateiVoll))
            return BadRequest($"Datei nicht gefunden: {registrierung.RelativerPfad}");

        var mitarbeiterId = await AktuellerMitarbeiterId(ct);
        if (mitarbeiterId is null)
            return BadRequest("Für den angemeldeten Benutzer existiert kein Mitarbeiter-Datensatz.");

        string pruefsumme;
        await using (var stream = System.IO.File.OpenRead(dateiVoll))
        {
            pruefsumme = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        }

        var version = bestandteil.Dokumente.Count == 0 ? 1 : bestandteil.Dokumente.Max(d => d.Version) + 1;
        db.Vertragsdokumente.Add(new Vertragsdokument
        {
            VertragsbestandteilId = bestandteil.Id,
            Version = version,
            Dateipfad = dateiVoll,
            DateinameOriginal = Path.GetFileName(dateiVoll),
            Sha256 = pruefsumme,
            ErstelltAm = DateTime.UtcNow,
            ErstelltVonId = mitarbeiterId.Value,
        });

        db.Aktivitaeten.Add(await TimelineEintrag(bestandteil.Vertrag!.KundeId,
            $"Vertrag {bestandteil.Vertrag.Vertragsnummer}: Dokument „{Path.GetFileName(dateiVoll)}“ als Version {version} zu {bestandteil.Titel} registriert",
            null, ct));

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // --- Hilfen ----------------------------------------------------------------

    private async Task<Aktivitaet> TimelineEintrag(int kundeId, string betreff, string? inhalt, CancellationToken ct)
        => new()
        {
            KundeId = kundeId,
            Typ = AktivitaetsTyp.Vertrag,
            Zeitpunkt = DateTime.UtcNow,
            MitarbeiterId = await AktuellerMitarbeiterId(ct),
            Richtung = AktivitaetsRichtung.Intern,
            Betreff = betreff,
            Inhalt = inhalt,
        };

    private Task<int?> AktuellerMitarbeiterId(CancellationToken ct) =>
        benutzer.EntraObjectId is Guid oid
            ? db.Mitarbeiter.Where(m => m.EntraObjectId == oid).Select(m => (int?)m.Id).FirstOrDefaultAsync(ct)
            : Task.FromResult<int?>(null);

    private static VertragDetail NachDetail(Vertrag vertrag) => new(
        vertrag.Id,
        vertrag.Vertragsnummer,
        vertrag.KundeId,
        vertrag.Kunde?.Firmenname ?? "",
        vertrag.Kunde?.Kundennummer ?? "",
        StatusText(vertrag.Status),
        vertrag.Beginn,
        vertrag.MindestlaufzeitMonate,
        vertrag.VerlaengerungMonate,
        vertrag.KuendigungsfristMonate,
        vertrag.GekuendigtZum,
        vertrag.NaechstesVertragsende(DateOnly.FromDateTime(DateTime.Today)),
        vertrag.Notiz,
        vertrag.Bestandteile
            .OrderBy(b => b.Id)
            .Select(b => new BestandteilDetail(
                b.Id, BestandteilText(b.Typ), b.Titel, b.Beschreibung, b.UnterzeichnetAm,
                b.Dokumente
                    .OrderByDescending(d => d.Version)
                    .Select(d => new DokumentEintrag(
                        d.Id, d.Version, d.Dateipfad, d.DateinameOriginal, d.Sha256,
                        d.ErstelltAm, d.ErstelltVon?.Anzeigename ?? ""))
                    .ToList()))
            .ToList());

    internal static string StatusText(VertragsStatus status) => status switch
    {
        VertragsStatus.Entwurf => "Entwurf",
        VertragsStatus.Aktiv => "Aktiv",
        VertragsStatus.Gekuendigt => "Gekündigt",
        VertragsStatus.Beendet => "Beendet",
        _ => status.ToString(),
    };

    private static VertragsStatus? StatusWert(string? text) => text?.Trim() switch
    {
        "Entwurf" => VertragsStatus.Entwurf,
        "Aktiv" => VertragsStatus.Aktiv,
        "Gekündigt" => VertragsStatus.Gekuendigt,
        "Beendet" => VertragsStatus.Beendet,
        _ => null,
    };

    internal static string BestandteilText(BestandteilTyp typ) => typ switch
    {
        BestandteilTyp.Lizenzvertrag => "Lizenzvertrag",
        BestandteilTyp.Avv => "AVV",
        BestandteilTyp.Nda => "NDA",
        BestandteilTyp.Sondervereinbarung => "Sondervereinbarung",
        BestandteilTyp.Nachtrag => "Nachtrag",
        _ => "Sonstiges",
    };

    private static BestandteilTyp? BestandteilWert(string? text) => text?.Trim() switch
    {
        "Lizenzvertrag" => BestandteilTyp.Lizenzvertrag,
        "AVV" => BestandteilTyp.Avv,
        "NDA" => BestandteilTyp.Nda,
        "Sondervereinbarung" => BestandteilTyp.Sondervereinbarung,
        "Nachtrag" => BestandteilTyp.Nachtrag,
        "Sonstiges" => BestandteilTyp.Sonstiges,
        _ => null,
    };
}
