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
public class WikiController(ErpDbContext db, IAktuellerBenutzer benutzer, Dienste.WikiBerechtigung berechtigung)
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

    /// <summary>Wiki-Themen nach Bereich für die Startseite — mit dem Hinweis, ob der Benutzer sie pflegen darf.</summary>
    [HttpGet("themen")]
    public async Task<ActionResult<List<WikiThemaUebersicht>>> Themen(CancellationToken ct)
    {
        var bearbeitbar = await berechtigung.BearbeitbareThemenAsync(ct);
        var zustaendig = await berechtigung.ZustaendigeThemenAsync(ct);
        var eigeneId = await berechtigung.EigeneIdAsync(ct);

        // Neuerungen mit Bestätigungspflicht, die der Benutzer noch nicht bestätigt hat
        var unbestaetigt = eigeneId is null
            ? new Dictionary<string, int>()
            : await db.WikiNeuerungen.AsNoTracking()
                .Where(n => n.BestaetigungErforderlich
                            && !n.Lesungen.Any(l => l.MitarbeiterId == eigeneId && l.BestaetigtAm != null))
                .GroupBy(n => n.Thema).Select(g => new { g.Key, Anzahl = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Anzahl, ct);
        var offen = await db.WikiMeldungen.AsNoTracking()
            .Where(m => m.ErledigtAm == null)
            .GroupBy(m => m.Thema).Select(g => new { g.Key, Anzahl = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Anzahl, ct);

        return Ok(WikiThema.Alle
            .Select(t => new WikiThemaUebersicht(t.Schluessel, t.Name, t.Bereich, bearbeitbar.Contains(t.Schluessel),
                unbestaetigt.GetValueOrDefault(t.Schluessel), offen.GetValueOrDefault(t.Schluessel), zustaendig.Contains(t.Schluessel)))
            .ToList());
    }

    [HttpGet("sanktionslisten")]
    public async Task<ActionResult<SanktionslistenKatalog>> Sanktionslisten(CancellationToken ct)
    {
        var listen = await db.Sanktionslisten.AsNoTracking()
            .Include(s => s.Verantwortlich).Include(s => s.Vertreter).Include(s => s.GeaendertVon)
            .ToListAsync(ct);

        var reihenfolge = Regionen.Select((r, i) => (r.Code, i)).ToDictionary(x => x.Code, x => x.i);
        var pakete = await PaketeJeListeAsync(ct);
        var eintraege = listen
            .OrderBy(s => reihenfolge.GetValueOrDefault(s.Region, int.MaxValue))
            .ThenByDescending(s => s.Standardumfang)
            .ThenBy(s => s.Kuerzel ?? s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => NachEintrag(s) with { Pakete = pakete.GetValueOrDefault(s.Kuerzel ?? "") })
            .ToList();

        var standard = await db.SanktionslistenStandardprozesse.AsNoTracking()
            .Include(p => p.GeaendertVon).ToListAsync(ct);
        // Für jede Importart außer „offen" einen Eintrag liefern — auch wenn noch leer
        var standardprozesse = new[] { SanktionslistenImport.Automatisch, SanktionslistenImport.Manuell, SanktionslistenImport.NichtImEinsatz }
            .Select(art => standard.FirstOrDefault(p => p.Import == art) is { } p
                ? new StandardprozessEintrag(art.ToString(), p.Text, p.GeaendertAm, p.GeaendertVon?.Anzeigename)
                : new StandardprozessEintrag(art.ToString(), null, null, null))
            .ToList();

        return Ok(new SanktionslistenKatalog(eintraege, await DarfBearbeitenAsync(ct),
            Regionen.Select(r => new RegionEintrag(r.Code, r.Name)).ToList(), standardprozesse));
    }

    /// <summary>Standardprozess einer Importart setzen (leer = entfernen).</summary>
    [HttpPut("sanktionslisten/standardprozesse/{import}")]
    public async Task<IActionResult> StandardprozessSpeichern(string import, StandardprozessSpeichern daten, CancellationToken ct)
    {
        if (!await DarfBearbeitenAsync(ct))
            return KeinZugriff();
        if (!Enum.TryParse<SanktionslistenImport>(import, out var art) || art == SanktionslistenImport.Offen)
            return BadRequest($"Für die Importart '{import}' gibt es keinen Standardprozess.");
        var prozess = await db.SanktionslistenStandardprozesse.FirstOrDefaultAsync(p => p.Import == art, ct);
        if (prozess is null)
        {
            prozess = new SanktionslistenStandardprozess { Import = art };
            db.SanktionslistenStandardprozesse.Add(prozess);
        }
        prozess.Text = Leer(daten.Text);
        prozess.GeaendertAm = DateTime.UtcNow;
        prozess.GeaendertVonId = await db.Mitarbeiter
            .Where(Mitarbeiter.KontoFilter(benutzer.EntraObjectId, benutzer.WindowsKonto))
            .Select(m => (int?)m.Id).FirstOrDefaultAsync(ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Aktive Benutzer für Verantwortlich/Vertreter.</summary>
    [HttpGet("personen")]
    public async Task<ActionResult<List<PersonAuswahl>>> Personen(CancellationToken ct) =>
        Ok(await db.Mitarbeiter.AsNoTracking()
            .Where(m => m.Aktiv)
            .OrderBy(m => m.Anzeigename)
            .Select(m => new PersonAuswahl(m.Id, m.Anzeigename))
            .ToListAsync(ct));

    [HttpPost("sanktionslisten")]
    public async Task<ActionResult<SanktionslisteEintrag>> Anlegen(SanktionslisteSpeichern daten, CancellationToken ct)
    {
        if (!await DarfBearbeitenAsync(ct))
            return KeinZugriff();
        var liste = new Sanktionsliste { Region = "", Name = "" };
        if (await UebernehmenAsync(liste, daten, ct) is { } fehler)
            return BadRequest(fehler);
        db.Sanktionslisten.Add(liste);
        await db.SaveChangesAsync(ct);
        return Ok(NachEintrag(liste));
    }

    [HttpPut("sanktionslisten/{id:int}")]
    public async Task<IActionResult> Aendern(int id, SanktionslisteSpeichern daten, CancellationToken ct)
    {
        if (!await DarfBearbeitenAsync(ct))
            return KeinZugriff();
        var liste = await db.Sanktionslisten.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (liste is null)
            return NotFound();
        if (await UebernehmenAsync(liste, daten, ct) is { } fehler)
            return BadRequest(fehler);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // --- Preisliste (Import aus dem Preislisten-Projekt) --------------------------

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptionen = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Aktuelle (oder eine ältere) Preisliste — intern gefiltert, wenn der Benutzer kein Vertrieb/Admin/Compliance ist.</summary>
    [HttpGet("preisliste")]
    public async Task<ActionResult<PreislisteAnsicht>> Preisliste([FromQuery] int? version, CancellationToken ct)
    {
        var darfInternes = await DarfInternesAsync(ct);
        var darfBearbeiten = await berechtigung.DarfBearbeitenAsync(WikiThema.Preisliste, ct);
        var verzeichnis = (await db.Einstellungen.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Schluessel == EinstellungsSchluessel.PreislisteVerzeichnis, ct))?.Wert;

        var historie = await db.PreislistenVersionen.AsNoTracking()
            .Include(p => p.ImportiertVon)
            .OrderByDescending(p => p.Version)
            .ToListAsync(ct);
        var gewaehlt = version is null ? historie.FirstOrDefault() : historie.FirstOrDefault(p => p.Version == version);
        if (version is not null && gewaehlt is null)
            return NotFound();

        PreislisteDaten? daten = null;
        PreislisteVergleichErgebnis? vergleich = null;
        if (gewaehlt is not null)
        {
            var roh = System.Text.Json.JsonSerializer.Deserialize<PreislisteDaten>(gewaehlt.Daten, JsonOptionen);
            daten = roh is null ? null : PreislisteSichtbarkeit.Filtern(roh, darfInternes);

            // Automatischer Vergleich mit der nächstälteren Version — beide Seiten auf den Leser gefiltert
            var vorige = historie.FirstOrDefault(p => p.Version < gewaehlt.Version);
            if (daten is not null && vorige is not null)
            {
                try
                {
                    var alt = System.Text.Json.JsonSerializer.Deserialize<PreislisteDaten>(vorige.Daten, JsonOptionen);
                    if (alt is not null)
                        vergleich = new PreislisteVergleichErgebnis(vorige.Version,
                            PreislisteVergleich.Vergleichen(PreislisteSichtbarkeit.Filtern(alt, darfInternes), daten));
                }
                catch (System.Text.Json.JsonException) { /* alte Version nicht lesbar — dann ohne Vergleich */ }
            }
        }

        // Auch der Dateiname der internen PDF bleibt Unberechtigten verborgen
        PreislisteVersionEintrag Version(PreislistenVersion v) =>
            darfInternes ? NachVersion(v) : NachVersion(v) with { DateiIntern = null };

        return Ok(new PreislisteAnsicht(
            daten,
            gewaehlt is null ? null : Version(gewaehlt),
            darfInternes, darfBearbeiten,
            string.IsNullOrWhiteSpace(verzeichnis) ? null : verzeichnis,
            historie.Select(Version).ToList(),
            vergleich));
    }

    /// <summary>
    /// Import einer neuen Preislisten-Version (JSON-Export aus dem Preislisten-
    /// Projekt). Nur Redaktion des Themas; nur Versionen, die neuer sind als
    /// die vorhandene. Die Datei wird unverändert gespeichert.
    /// </summary>
    [HttpPost("preisliste/import")]
    [RequestSizeLimit(5_000_000)]
    public async Task<ActionResult<PreislisteVersionEintrag>> PreislisteImport(
        IFormFile datei, [FromForm] string? notiz, [FromForm] string? neuerung, [FromForm] bool bestaetigung, CancellationToken ct)
    {
        if (!await berechtigung.DarfBearbeitenAsync(WikiThema.Preisliste, ct))
            return StatusCode(StatusCodes.Status403Forbidden,
                "Preisliste importieren dürfen Admin, Compliance und Wiki-Redakteure mit Freigabe für dieses Thema.");
        if (datei.Length == 0)
            return BadRequest("Die Datei ist leer.");

        string text;
        using (var leser = new StreamReader(datei.OpenReadStream(), System.Text.Encoding.UTF8))
            text = await leser.ReadToEndAsync(ct);

        PreislisteDaten? p;
        try
        {
            p = System.Text.Json.JsonSerializer.Deserialize<PreislisteDaten>(text, JsonOptionen);
        }
        catch (System.Text.Json.JsonException ex)
        {
            return BadRequest($"Die Datei ist kein gültiger Preislisten-Export: {ex.Message}");
        }
        if (p is null || !p.Schema.StartsWith("rausoft.preisliste/", StringComparison.Ordinal))
            return BadRequest("Die Datei ist kein Preislisten-Export (Feld „schema“ fehlt oder passt nicht).");
        if (p.Version <= 0 || string.IsNullOrWhiteSpace(p.Stand) || p.Stufen.Count == 0 || p.Volumenstufen.Count == 0)
            return BadRequest("Version, Stand, Stufen oder Volumenstufen fehlen.");
        var stufenSchluessel = p.Stufen.Select(s => s.Schluessel).ToHashSet();
        var unbekannt = p.Volumenstufen.SelectMany(v => v.Preise.Keys).Where(k => !stufenSchluessel.Contains(k)).Distinct().ToList();
        if (unbekannt.Count > 0)
            return BadRequest($"Volumenstufen verwenden unbekannte Stufen-Schlüssel: {string.Join(", ", unbekannt)}");

        var hoechste = await db.PreislistenVersionen.MaxAsync(x => (int?)x.Version, ct) ?? 0;
        if (p.Version <= hoechste)
            return BadRequest($"Version {p.Version} ist nicht neuer als die vorhandene Version {hoechste}. " +
                "Bitte im Preislisten-Projekt eine neue Version erzeugen.");

        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();
        var eintrag = new PreislistenVersion
        {
            Version = p.Version,
            Stand = p.Stand,
            GueltigAb = p.GueltigAb,
            Produkt = p.Produkt,
            Schema = p.Schema,
            Daten = text,
            Sha256 = sha,
            DateiIntern = p.Dokumente?.Intern,
            DateiExtern = p.Dokumente?.Extern,
            ImportiertAm = DateTime.UtcNow,
            ImportiertVonId = await EigeneIdAsync(ct),
            Notiz = Leer(notiz),
        };
        db.PreislistenVersionen.Add(eintrag);

        // „Was ist neu" — Beschreibung des Redakteurs, ergänzt um die Änderungsliste aus dem Export
        var neuerungText = Leer(neuerung);
        if (p.Aenderungen is { Count: > 0 } aenderungen)
            neuerungText = (neuerungText is null ? "" : neuerungText + "\n\n") +
                           string.Join("\n", aenderungen.Select(x => "• " + x));
        if (neuerungText is not null)
            db.WikiNeuerungen.Add(new WikiNeuerung
            {
                Thema = WikiThema.Preisliste,
                Titel = $"Preisliste Version {p.Version} · Stand {p.Stand}",
                Text = neuerungText,
                PreislistenVersion = eintrag,
                BestaetigungErforderlich = bestaetigung,
                ErstelltAm = DateTime.UtcNow,
                ErstelltVonId = eintrag.ImportiertVonId,
            });

        await db.SaveChangesAsync(ct);
        return Ok(NachVersion(eintrag));
    }

    /// <summary>Zuständigkeit und Prozess eines Wiki-Themas (z. B. Preisliste).</summary>
    [HttpGet("themen/{thema}/info")]
    public async Task<ActionResult<WikiThemaInfoEintrag>> ThemaInfo(string thema, CancellationToken ct)
    {
        if (!WikiThema.IstBekannt(thema))
            return NotFound();
        var info = await db.WikiThemaInfos.AsNoTracking()
            .Include(i => i.Verantwortlich).Include(i => i.Vertreter).Include(i => i.GeaendertVon)
            .FirstOrDefaultAsync(i => i.Thema == thema, ct);
        return Ok(info is null
            ? new WikiThemaInfoEintrag(thema, null, null, null, null, null, null, null, [], [])
            : new WikiThemaInfoEintrag(thema, info.VerantwortlichId, info.Verantwortlich?.Anzeigename,
                info.VertreterId, info.Vertreter?.Anzeigename, info.Prozess, info.GeaendertAm, info.GeaendertVon?.Anzeigename,
                WikiAbschnittReihenfolge.Lesen(info.Abschnitte), WikiAbschnittReihenfolge.Lesen(info.AbschnitteOffen)));
    }

    /// <summary>
    /// Reihenfolge der Abschnitte einer Themenseite — die Redaktion ordnet an,
    /// was zuerst kommt (z. B. WorldCheck vor den Begriffen); gilt für alle Leser.
    /// Eine leere Liste stellt die Standardreihenfolge der Seite wieder her.
    /// </summary>
    [HttpPut("themen/{thema}/abschnitte")]
    public async Task<IActionResult> AbschnitteSpeichern(string thema, WikiAbschnitteSpeichern daten, CancellationToken ct)
    {
        if (!WikiThema.IstBekannt(thema))
            return NotFound();
        if (!await berechtigung.DarfBearbeitenAsync(thema, ct))
            return StatusCode(StatusCodes.Status403Forbidden, "Die Abschnitte dürfen nur freigegebene Wiki-Redakteure, Compliance und Admin anordnen.");
        var wert = WikiAbschnittReihenfolge.Schreiben(daten.Abschnitte, out var fehler);
        if (fehler is not null)
            return BadRequest(fehler);
        var offen = WikiAbschnittReihenfolge.Schreiben(daten.Offen, out fehler);
        if (fehler is not null)
            return BadRequest(fehler);

        var info = await db.WikiThemaInfos.FirstOrDefaultAsync(i => i.Thema == thema, ct);
        if (info is null)
        {
            info = new WikiThemaInfo { Thema = thema };
            db.WikiThemaInfos.Add(info);
        }
        info.Abschnitte = wert;
        info.AbschnitteOffen = offen;
        info.GeaendertAm = DateTime.UtcNow;
        info.GeaendertVonId = await EigeneIdAsync(ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("themen/{thema}/info")]
    public async Task<IActionResult> ThemaInfoSpeichern(string thema, WikiThemaInfoSpeichern daten, CancellationToken ct)
    {
        if (!WikiThema.IstBekannt(thema))
            return NotFound();
        if (!await berechtigung.DarfBearbeitenAsync(thema, ct))
            return StatusCode(StatusCodes.Status403Forbidden, "Dieses Thema dürfen nur freigegebene Wiki-Redakteure, Compliance und Admin pflegen.");
        if (daten.VerantwortlichId is not null && daten.VerantwortlichId == daten.VertreterId)
            return BadRequest("Verantwortlich und Vertreter müssen verschiedene Personen sein.");
        foreach (var personId in new[] { daten.VerantwortlichId, daten.VertreterId }.OfType<int>())
            if (!await db.Mitarbeiter.AnyAsync(m => m.Id == personId, ct))
                return BadRequest("Die gewählte Person existiert nicht.");

        var info = await db.WikiThemaInfos.FirstOrDefaultAsync(i => i.Thema == thema, ct);
        if (info is null)
        {
            info = new WikiThemaInfo { Thema = thema };
            db.WikiThemaInfos.Add(info);
        }
        info.VerantwortlichId = daten.VerantwortlichId;
        info.VertreterId = daten.VertreterId;
        info.Prozess = Leer(daten.Prozess);
        info.GeaendertAm = DateTime.UtcNow;
        info.GeaendertVonId = await EigeneIdAsync(ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Welche Sanktionsliste steckt in welchem Paket der aktuellen Preisliste? (Kürzel → Paketnamen)</summary>
    private async Task<Dictionary<string, List<string>>> PaketeJeListeAsync(CancellationToken ct)
    {
        var aktuelle = await db.PreislistenVersionen.AsNoTracking()
            .OrderByDescending(p => p.Version).Select(p => p.Daten).FirstOrDefaultAsync(ct);
        var ergebnis = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (aktuelle is null)
            return ergebnis;
        PreislisteDaten? p;
        try { p = System.Text.Json.JsonSerializer.Deserialize<PreislisteDaten>(aktuelle, JsonOptionen); }
        catch (System.Text.Json.JsonException) { return ergebnis; }
        if (p?.SanktionslistenPakete is null)
            return ergebnis;

        void Merke(string kuerzel, string paket) =>
            (ergebnis.TryGetValue(kuerzel, out var l) ? l : ergebnis[kuerzel] = []).Add(paket);
        foreach (var k in p.SanktionslistenPakete.Standardlisten)
            Merke(k, "Standard (in allen Stufen)");
        // Das All-Listen-Bundle wiederholt nur die Einzelpakete — nicht separat ausweisen
        foreach (var paket in p.SanktionslistenPakete.Pakete.Where(x => x.Listen.Count > 0 && x.Schluessel != "all-listen-bundle"))
            foreach (var k in paket.Listen)
                Merke(k, paket.Name);
        return ergebnis;
    }

    private async Task<bool> DarfInternesAsync(CancellationToken ct)
    {
        if (benutzer.EntraObjectId is null && benutzer.WindowsKonto is null)
            return false;
        var rollen = await db.Mitarbeiter.AsNoTracking()
            .Where(Mitarbeiter.KontoFilter(benutzer.EntraObjectId, benutzer.WindowsKonto))
            .Where(m => m.Aktiv).Select(m => m.Rollen).FirstOrDefaultAsync(ct);
        // Interne Preisliste: Vertrieb, Admin, Compliance (Entscheidung MP 14.10.2026)
        return rollen.HasFlag(MitarbeiterRolle.Vertrieb) || rollen.HasFlag(MitarbeiterRolle.Admin)
               || rollen.HasFlag(MitarbeiterRolle.Compliance);
    }

    private Task<int?> EigeneIdAsync(CancellationToken ct) =>
        db.Mitarbeiter.Where(Mitarbeiter.KontoFilter(benutzer.EntraObjectId, benutzer.WindowsKonto))
            .Select(m => (int?)m.Id).FirstOrDefaultAsync(ct);

    private static PreislisteVersionEintrag NachVersion(PreislistenVersion p) => new(
        p.Id, p.Version, p.Stand, p.GueltigAb, p.DateiIntern, p.DateiExtern,
        p.ImportiertAm, p.ImportiertVon?.Anzeigename, p.Notiz);

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
        liste.Relevanz = Leer(d.Relevanz);
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

    // Pflege-Recht fürs Thema Sanktionslisten: Admin, Compliance oder Wiki-Redaktion mit Freigabe
    private Task<bool> DarfBearbeitenAsync(CancellationToken ct) =>
        berechtigung.DarfBearbeitenAsync(WikiThema.Sanktionslisten, ct);

    private ObjectResult KeinZugriff() => StatusCode(StatusCodes.Status403Forbidden,
        "Sanktionslisten pflegen dürfen Admin, Compliance und Wiki-Redakteure mit Freigabe für dieses Thema (Administration → Benutzer & Rollen).");

    private static SanktionslisteEintrag NachEintrag(Sanktionsliste s) => new(
        s.Id, s.Region, Regionen.FirstOrDefault(r => r.Code == s.Region).Name ?? s.Region,
        s.Kuerzel, s.Name, s.BezeichnungIntern, s.Aliase, s.Beschreibung, s.Relevanz, s.Art, s.Herausgeber,
        s.Geltungsbereich, s.Standardumfang, s.DownloadUrl, s.Import.ToString(), s.ImportHinweis,
        s.VerantwortlichId, s.Verantwortlich?.Anzeigename, s.VertreterId, s.Vertreter?.Anzeigename,
        s.Prozess, s.Aktiv, s.GeaendertAm, s.GeaendertVon?.Anzeigename);
}
