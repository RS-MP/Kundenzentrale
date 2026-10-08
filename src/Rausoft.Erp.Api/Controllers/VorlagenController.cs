using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Contracts;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Vertragsvorlagen mit Freigabe-Versionierung (FEEDBACK Nr. 17, Kern).
/// Lesen dürfen alle Mitarbeiter; Freigeben/Zurückziehen nur Compliance
/// (oder Admin). Entwürfe liegen als Word-Dateien im Vorlagen-Verzeichnis,
/// die Freigabe friert eine Kopie unter "Freigegeben" als Version ein.
/// </summary>
[ApiController]
[Route("api/vorlagen")]
public class VorlagenController(ErpDbContext db, IAktuellerBenutzer benutzer) : ControllerBase
{
    private const string FreigabeOrdner = "Freigegeben";

    /// <summary>Übersicht aller Vorlagen mit aktuell gültiger Version und Verwendungen.</summary>
    [HttpGet]
    public async Task<ActionResult<List<VorlageEintrag>>> Liste(CancellationToken ct)
    {
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var vorlagen = await db.Vorlagen.AsNoTracking()
            .Include(v => v.Versionen).ThenInclude(x => x.FreigegebenVon)
            .OrderBy(v => v.Name)
            .ToListAsync(ct);
        var verwendungen = await VerwendungenLaden(null, ct);

        return Ok(vorlagen.Select(v =>
        {
            var aktuelle = AktuelleVersion(v.Versionen, heute);
            var kommende = v.Versionen.Where(x => x.GueltigAb > heute && x.Nummer > (aktuelle?.Nummer ?? 0))
                .OrderByDescending(x => x.Nummer).FirstOrDefault();
            var eigene = verwendungen.Where(x => x.VorlageId == v.Id).ToList();
            return new VorlageEintrag(
                v.Id, v.Name, v.Aktiv,
                Dienste.Dokumentarten.Name(v.Typ), Dienste.Dokumentarten.Kuerzel(v.Typ),
                RollenNamen(v.ErlaubteRollen),
                aktuelle?.Nummer, aktuelle?.FreigegebenAm, aktuelle?.FreigegebenVon?.Anzeigename, aktuelle?.Notiz,
                kommende?.Nummer, kommende?.GueltigAb,
                eigene.Count);
        }).ToList());
    }

    /// <summary>Versions-Historie einer Vorlage.</summary>
    [HttpGet("{id:int}/versionen")]
    public async Task<ActionResult<List<VorlagenVersionEintrag>>> Versionen(int id, CancellationToken ct)
    {
        var versionen = await db.VorlagenVersionen.AsNoTracking()
            .Include(v => v.FreigegebenVon)
            .Where(v => v.VorlageId == id)
            .OrderByDescending(v => v.Nummer)
            .ToListAsync(ct);
        var verwendungen = await db.Vertragsdokumente.AsNoTracking()
            .Where(d => d.VorlagenVersion!.VorlageId == id)
            .GroupBy(d => d.VorlagenVersionId!.Value)
            .Select(g => new { VersionId = g.Key, Anzahl = g.Count() })
            .ToDictionaryAsync(g => g.VersionId, g => g.Anzahl, ct);
        return Ok(versionen.Select(v => new VorlagenVersionEintrag(
            v.Id, v.Nummer, v.FreigegebenAm, v.GueltigAb, v.FreigegebenVon?.Anzeigename, v.Notiz,
            verwendungen.GetValueOrDefault(v.Id))).ToList());
    }

    /// <summary>
    /// Welcher Kunde hat welchen Versionsstand dieser Vorlage bekommen? Reine
    /// Information — ein älterer Stand beim Kunden ist kein Mangel: unterschriebene
    /// Verträge bleiben gültig, auch wenn es inzwischen eine neuere Vorlage gibt.
    /// Neue Verträge entstehen ohnehin nur aus der aktuell gültigen Version.
    /// </summary>
    [HttpGet("{id:int}/verwendungen")]
    public async Task<ActionResult<List<VorlagenVerwendung>>> Verwendungen(int id, CancellationToken ct) =>
        Ok((await VerwendungenLaden(id, ct))
            .OrderByDescending(v => v.ErstelltAm)
            .Select(v => new VorlagenVerwendung(
                v.Nummer, v.VertragId, v.Vertragsnummer, v.KundeId, v.Kundennummer, v.Firmenname, v.ErstelltAm))
            .ToList());

    /// <summary>Dokumentart und Nutzungsfreigabe je Rolle ändern.</summary>
    [Authorize(Policy = "Compliance")]
    [HttpPut("{id:int}/einstellungen")]
    public async Task<IActionResult> EinstellungenAendern(int id, VorlageEinstellungen einstellungen, CancellationToken ct)
    {
        var vorlage = await db.Vorlagen.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (vorlage is null)
            return NotFound();
        if (Dienste.Dokumentarten.Wert(einstellungen.Typ) is not { } typ)
            return BadRequest($"Unbekannte Dokumentart '{einstellungen.Typ}'.");
        vorlage.Typ = typ;
        vorlage.ErlaubteRollen = MitarbeiterController.NachRollen(einstellungen.ErlaubteRollen);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // --- Hilfen ----------------------------------------------------------------

    /// <summary>Aktuell gültig = höchste Version, deren Gültig-ab erreicht ist.</summary>
    private static VorlagenVersion? AktuelleVersion(IEnumerable<VorlagenVersion> versionen, DateOnly heute) =>
        versionen.Where(v => v.GueltigAb <= heute).OrderByDescending(v => v.Nummer).FirstOrDefault();

    private record Verwendung(
        int VorlageId, int Nummer, int VertragId, string Vertragsnummer,
        int? KundeId, string Kundennummer, string Firmenname, DateTime ErstelltAm);

    private Task<List<Verwendung>> VerwendungenLaden(int? vorlageId, CancellationToken ct) =>
        db.Vertragsdokumente.AsNoTracking()
            .Where(d => d.VorlagenVersionId != null
                && (vorlageId == null || d.VorlagenVersion!.VorlageId == vorlageId))
            .Select(d => new Verwendung(
                d.VorlagenVersion!.VorlageId,
                d.VorlagenVersion.Nummer,
                d.Vertragsbestandteil!.VertragId,
                d.Vertragsbestandteil.Vertrag!.Vertragsnummer,
                d.Vertragsbestandteil.Vertrag.KundeId,
                d.Vertragsbestandteil.Vertrag.Kunde!.Kundennummer,
                d.Vertragsbestandteil.Vertrag.Kunde.Firmenname,
                d.ErstelltAm))
            .ToListAsync(ct);

    private static List<string> RollenNamen(MitarbeiterRolle rollen) =>
        MitarbeiterController.AlleRollen.Where(r => rollen.HasFlag(r.Rolle)).Select(r => r.Name).ToList();

    /// <summary>
    /// Dateitabelle für Compliance: alle Word-Dateien im Vorlagen-Verzeichnis mit
    /// Zuordnung über die Prüfsumme (entspricht Version vN → aktiv/ersetzt/…,
    /// abweichender Inhalt bei bekannter Quelldatei → geändert, sonst neu),
    /// unbekannten Platzhaltern und dem Zustand der eingefrorenen Kopien.
    /// </summary>
    [Authorize(Policy = "Compliance")]
    [HttpGet("dateien")]
    public async Task<ActionResult<VorlagenDateiUebersicht>> Dateien(CancellationToken ct)
    {
        var bekannte = VertraegeController.BekanntePlatzhalter;
        var verzeichnis = await VorlagenVerzeichnis(ct);
        if (verzeichnis is null || !Directory.Exists(verzeichnis))
            return Ok(new VorlagenDateiUebersicht([], [], bekannte.ToList()));

        var heute = DateOnly.FromDateTime(DateTime.Today);
        var vorlagen = await db.Vorlagen.AsNoTracking().Include(v => v.Versionen).ToListAsync(ct);
        var versionen = vorlagen.SelectMany(v => v.Versionen.Select(x => (Vorlage: v, Version: x))).ToList();

        string Status(Vorlage vorlage, VorlagenVersion version)
        {
            if (!vorlage.Aktiv)
                return "zurueckgezogen";
            if (version.GueltigAb > heute)
                return "zukuenftig";
            return AktuelleVersion(vorlage.Versionen, heute)?.Id == version.Id ? "aktiv" : "ersetzt";
        }

        var dateien = new List<VorlagenDatei>();
        foreach (var pfad in Directory.EnumerateFiles(verzeichnis, "*.docx", SearchOption.TopDirectoryOnly)
                     .Where(p => !Path.GetFileName(p).StartsWith("~$"))
                     .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(pfad);
            var geaendert = System.IO.File.GetLastWriteTime(pfad);
            try
            {
                var inhalt = await System.IO.File.ReadAllBytesAsync(pfad, ct);
                var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(inhalt)).ToLowerInvariant();
                List<string> unbekannt;
                using (var speicher = new MemoryStream(inhalt))
                    unbekannt = Dienste.VorlagenDienst.Platzhalter(speicher).Where(p => !bekannte.Contains(p)).ToList();

                // Gleicher Inhalt wie eine eingefrorene Version?
                var gleich = versionen.Where(x => x.Version.Sha256 == sha)
                    .OrderByDescending(x => x.Version.Nummer).FirstOrDefault();
                if (gleich.Vorlage is not null)
                {
                    dateien.Add(new VorlagenDatei(name, geaendert, Status(gleich.Vorlage, gleich.Version),
                        gleich.Vorlage.Id, gleich.Vorlage.Name, gleich.Version.Nummer, unbekannt, null));
                    continue;
                }

                // Sonst: Datei, aus der schon einmal freigegeben wurde (bzw. gleichnamig zur Vorlage) → geändert
                var zugehoerig = versionen
                    .Where(x => string.Equals(x.Version.QuellDateiname, name, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.Version.Nummer).Select(x => x.Vorlage).FirstOrDefault()
                    ?? vorlagen.FirstOrDefault(v => string.Equals(v.Name, Path.GetFileNameWithoutExtension(name),
                        StringComparison.OrdinalIgnoreCase));
                dateien.Add(new VorlagenDatei(name, geaendert, zugehoerig is null ? "neu" : "geaendert",
                    zugehoerig?.Id, zugehoerig?.Name, null, unbekannt, null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                           or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException
                                           or InvalidDataException)
            {
                dateien.Add(new VorlagenDatei(name, geaendert, "fehler", null, null, null, [],
                    "Datei nicht lesbar — in Word geöffnet oder kein gültiges Word-Dokument?"));
            }
        }

        // Eingefrorene Kopien: vorhanden und unverändert?
        var eingefroren = new List<EingefroreneVersion>();
        foreach (var (vorlage, version) in versionen.OrderBy(x => x.Vorlage.Name).ThenByDescending(x => x.Version.Nummer))
        {
            var datei = Path.Combine(verzeichnis, version.Dateipfad);
            string zustand;
            if (!System.IO.File.Exists(datei))
                zustand = "fehlt";
            else
            {
                await using var stream = System.IO.File.OpenRead(datei);
                var sha = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, ct))
                    .ToLowerInvariant();
                zustand = sha == version.Sha256 ? "ok" : "veraendert";
            }
            eingefroren.Add(new EingefroreneVersion(vorlage.Id, vorlage.Name, version.Nummer, version.Dateipfad, zustand));
        }

        return Ok(new VorlagenDateiUebersicht(dateien, eingefroren, bekannte.ToList()));
    }

    /// <summary>Entwurfsdateien im Vorlagen-Verzeichnis (Root) — Kandidaten für eine Freigabe.</summary>
    [Authorize(Policy = "Compliance")]
    [HttpGet("entwuerfe")]
    public async Task<ActionResult<List<VorlagenEntwurf>>> Entwuerfe(CancellationToken ct)
    {
        var verzeichnis = await VorlagenVerzeichnis(ct);
        if (verzeichnis is null || !Directory.Exists(verzeichnis))
            return Ok(new List<VorlagenEntwurf>());
        return Ok(Directory.EnumerateFiles(verzeichnis, "*.docx", SearchOption.TopDirectoryOnly)
            .Where(p => !Path.GetFileName(p).StartsWith("~$"))
            .Select(p => new VorlagenEntwurf(Path.GetFileName(p), System.IO.File.GetLastWriteTime(p)))
            .OrderBy(e => e.Dateiname)
            .ToList());
    }

    /// <summary>
    /// Freigabe: friert die Entwurfsdatei als neue Version ein (Kopie nach
    /// "Freigegeben\{Name} v{N}.docx" mit SHA-256) — ab dann in der Auswahl.
    /// </summary>
    [Authorize(Policy = "Compliance")]
    [HttpPost("freigeben")]
    public async Task<ActionResult<VorlageEintrag>> Freigeben(VorlageFreigeben freigabe, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(freigabe.Dateiname)
            || Path.GetFileName(freigabe.Dateiname) != freigabe.Dateiname
            || !freigabe.Dateiname.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Ungültiger Dateiname.");

        var verzeichnis = await VorlagenVerzeichnis(ct);
        if (verzeichnis is null)
            return BadRequest("Das Vorlagen-Verzeichnis ist nicht konfiguriert (Administration).");
        var quelle = Path.Combine(verzeichnis, freigabe.Dateiname);
        if (!System.IO.File.Exists(quelle))
            return BadRequest($"Entwurfsdatei nicht gefunden: {freigabe.Dateiname}");

        Vorlage? vorlage;
        if (freigabe.VorlageId is { } vorlageId)
        {
            vorlage = await db.Vorlagen.Include(v => v.Versionen)
                .FirstOrDefaultAsync(v => v.Id == vorlageId, ct);
            if (vorlage is null)
                return BadRequest("Die angegebene Vorlage existiert nicht.");
        }
        else
        {
            var name = (freigabe.NeuerName ?? Path.GetFileNameWithoutExtension(freigabe.Dateiname)).Trim();
            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return BadRequest("Ungültiger Vorlagen-Name.");
            if (await db.Vorlagen.AnyAsync(v => v.Name == name, ct))
                return BadRequest($"Eine Vorlage „{name}“ existiert bereits — dort eine neue Version freigeben.");
            if (freigabe.Typ is not null && Dienste.Dokumentarten.Wert(freigabe.Typ) is null)
                return BadRequest($"Unbekannte Dokumentart '{freigabe.Typ}'.");
            vorlage = new Vorlage
            {
                Name = name,
                Typ = Dienste.Dokumentarten.Wert(freigabe.Typ) ?? BestandteilTyp.Lizenzvertrag,
            };
            db.Vorlagen.Add(vorlage);
        }

        // Gültig-ab: leer oder in der Vergangenheit = sofort; sonst gilt bis dahin die Vorversion
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var gueltigAb = freigabe.GueltigAb is { } datum && datum > heute ? datum : heute;

        var mitarbeiterId = await db.Mitarbeiter
            .Where(Mitarbeiter.KontoFilter(benutzer.EntraObjectId, benutzer.WindowsKonto))
            .Select(m => (int?)m.Id).FirstOrDefaultAsync(ct);

        // Unveränderte Datei nicht erneut freigeben — sonst entstünden inhaltsgleiche Versionen
        var quellSha = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(await System.IO.File.ReadAllBytesAsync(quelle, ct))).ToLowerInvariant();
        if (await db.VorlagenVersionen.Include(v => v.Vorlage)
                .Where(v => v.Sha256 == quellSha)
                .Select(v => v.Vorlage!.Name + " v" + v.Nummer).FirstOrDefaultAsync(ct) is { } identisch)
            return BadRequest($"Die Datei ist inhaltsgleich mit der bereits freigegebenen Version „{identisch}“.");

        var nummer = (vorlage.Versionen.Count == 0 ? 0 : vorlage.Versionen.Max(v => v.Nummer)) + 1;
        var relativerPfad = Path.Combine(FreigabeOrdner, $"{vorlage.Name} v{nummer}.docx");
        var ziel = Path.Combine(verzeichnis, relativerPfad);
        Directory.CreateDirectory(Path.Combine(verzeichnis, FreigabeOrdner));
        if (System.IO.File.Exists(ziel))
            return BadRequest($"Die Versionsdatei existiert bereits: {relativerPfad}");
        System.IO.File.Copy(quelle, ziel);

        string pruefsumme;
        await using (var stream = System.IO.File.OpenRead(ziel))
            pruefsumme = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, ct))
                .ToLowerInvariant();

        vorlage.Versionen.Add(new VorlagenVersion
        {
            Vorlage = vorlage,
            Nummer = nummer,
            Dateipfad = relativerPfad,
            Sha256 = pruefsumme,
            FreigegebenAm = DateTime.UtcNow,
            FreigegebenVonId = mitarbeiterId,
            QuellDateiname = freigabe.Dateiname,
            GueltigAb = gueltigAb,
            Notiz = string.IsNullOrWhiteSpace(freigabe.Notiz) ? null : freigabe.Notiz.Trim(),
        });
        await db.SaveChangesAsync(ct);

        var neueste = vorlage.Versionen.OrderByDescending(v => v.Nummer).First();
        return Ok(new VorlageEintrag(vorlage.Id, vorlage.Name, vorlage.Aktiv,
            Dienste.Dokumentarten.Name(vorlage.Typ), Dienste.Dokumentarten.Kuerzel(vorlage.Typ),
            RollenNamen(vorlage.ErlaubteRollen),
            neueste.Nummer, neueste.FreigegebenAm, null, neueste.Notiz,
            gueltigAb > heute ? neueste.Nummer : null, gueltigAb > heute ? gueltigAb : null, 0));
    }

    /// <summary>Vorlage zurückziehen bzw. wieder aktivieren (Historie bleibt erhalten).</summary>
    [Authorize(Policy = "Compliance")]
    [HttpPut("{id:int}/aktiv")]
    public async Task<IActionResult> AktivAendern(int id, VorlageAktivAenderung aenderung, CancellationToken ct)
    {
        var vorlage = await db.Vorlagen.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (vorlage is null)
            return NotFound();
        vorlage.Aktiv = aenderung.Aktiv;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<string?> VorlagenVerzeichnis(CancellationToken ct)
    {
        var wert = (await db.Einstellungen.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Schluessel == EinstellungsSchluessel.VorlagenVerzeichnis, ct))?.Wert;
        return string.IsNullOrWhiteSpace(wert) ? null : wert;
    }
}
