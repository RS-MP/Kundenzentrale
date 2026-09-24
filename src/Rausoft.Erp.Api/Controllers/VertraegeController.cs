using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Contracts;
using Rausoft.Erp.Domain.Customers;
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
        [FromQuery] int? kundeId, [FromQuery] string? filter,
        [FromQuery] int seite = 1, [FromQuery] int proSeite = 25,
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

        // Nächstmögliches Ende wird berechnet → Arbeitsfilter/Paging im Speicher
        var roh = await query
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
        var berechnet = roh.Select(v => new
        {
            v.Id, v.Vertragsnummer, v.KundeId, v.KundeName, v.Kundennummer,
            v.Status, v.Beginn, v.MindestlaufzeitMonate, v.Typen,
            Ende = new Vertrag
            {
                Vertragsnummer = v.Vertragsnummer,
                Beginn = v.Beginn,
                MindestlaufzeitMonate = v.MindestlaufzeitMonate,
                VerlaengerungMonate = v.VerlaengerungMonate,
                KuendigungsfristMonate = v.KuendigungsfristMonate,
                GekuendigtZum = v.GekuendigtZum,
            }.NaechstesVertragsende(heute),
        });

        berechnet = filter switch
        {
            "nachzupflegen" => berechnet.Where(v => v.MindestlaufzeitMonate == null
                && v.Status is VertragsStatus.Aktiv or VertragsStatus.Entwurf),
            "ende90" => berechnet.Where(v => v.Ende is { } ende
                && ende <= heute.AddDays(90) && v.Status != VertragsStatus.Beendet),
            _ => berechnet,
        };

        var gefiltert = (filter == "ende90"
                ? berechnet.OrderBy(v => v.Ende)
                : berechnet.OrderBy(v => v.Vertragsnummer, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var eintraege = gefiltert
            .Skip((seite - 1) * proSeite)
            .Take(proSeite)
            .Select(v => new VertragListenEintrag(
                v.Id, v.Vertragsnummer, v.KundeId, v.KundeName, v.Kundennummer,
                StatusText(v.Status), v.Beginn, v.Ende, BestandteilZusammenfassung(v.Typen)))
            .ToList();

        return Ok(new SeitenErgebnis<VertragListenEintrag>(eintraege, gefiltert.Count, seite, proSeite));
    }

    /// <summary>Kennzahlen für die Arbeitsliste (KPI-Zeile).</summary>
    [HttpGet("kennzahlen")]
    public async Task<ActionResult<VertragsKennzahlen>> Kennzahlen(CancellationToken ct)
    {
        var roh = await db.Vertraege.AsNoTracking()
            .Select(v => new
            {
                v.Vertragsnummer, v.Status, v.Beginn, v.MindestlaufzeitMonate,
                v.VerlaengerungMonate, v.KuendigungsfristMonate, v.GekuendigtZum,
            })
            .ToListAsync(ct);

        var heute = DateOnly.FromDateTime(DateTime.Today);
        var mitEnde = roh.Select(v => new
        {
            v.Status, v.MindestlaufzeitMonate,
            Ende = new Vertrag
            {
                Vertragsnummer = v.Vertragsnummer,
                Beginn = v.Beginn,
                MindestlaufzeitMonate = v.MindestlaufzeitMonate,
                VerlaengerungMonate = v.VerlaengerungMonate,
                KuendigungsfristMonate = v.KuendigungsfristMonate,
                GekuendigtZum = v.GekuendigtZum,
            }.NaechstesVertragsende(heute),
        }).ToList();

        return Ok(new VertragsKennzahlen(
            roh.Count,
            mitEnde.Count(v => v.MindestlaufzeitMonate == null
                && v.Status is VertragsStatus.Aktiv or VertragsStatus.Entwurf),
            mitEnde.Count(v => v.Ende is { } ende && ende <= heute.AddDays(90)
                && v.Status != VertragsStatus.Beendet),
            roh.Count(v => v.Status == VertragsStatus.Gekuendigt)));
    }

    /// <summary>Kompakte Zusammenfassung wie „Wartungsvertrag + 2 Nachträge · 5 Bestellungen".</summary>
    private static List<string> BestandteilZusammenfassung(List<BestandteilTyp> typen) =>
        typen.GroupBy(t => t)
            .OrderBy(g => g.Key)
            .Select(g => g.Count() == 1 ? BestandteilText(g.Key) : $"{g.Count()} {MehrzahlText(g.Key)}")
            .ToList();

    private static string MehrzahlText(BestandteilTyp typ) => typ switch
    {
        BestandteilTyp.Nachtrag => "Nachträge",
        BestandteilTyp.Bestellung => "Bestellungen",
        BestandteilTyp.Installationsprotokoll => "Installationsprotokolle",
        BestandteilTyp.Kuendigung => "Kündigungen",
        _ => BestandteilText(typ),
    };

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

        return Ok(NachDetail(vertrag, await Einstellung(EinstellungsSchluessel.VertragsablageVerzeichnis, ct)));
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

        // Ein neuer Vertrag holt archivierte Kunden automatisch aus dem Archiv
        if (kunde.ArchiviertAm != null)
        {
            kunde.ArchiviertAm = null;
            db.Aktivitaeten.Add(await TimelineEintrag(kunde.Id,
                "Aus dem Archiv geholt — neuer Vertrag angelegt", null, null, ct));
        }
        await db.SaveChangesAsync(ct);

        // Timeline-Eintrag mit Sprungziel — braucht die gespeicherte Vertrags-Id
        db.Aktivitaeten.Add(await TimelineEintrag(kunde.Id,
            $"Vertrag {nummer} angelegt", null, vertrag.Id, ct));
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
                $"Vertrag {vertrag.Vertragsnummer}: {StatusText(alterStatus)} → {StatusText(neuerStatus)}",
                null, vertrag.Id, ct));
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
            null, bestandteil.VertragId, ct));

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Admin-Löschung einer kompletten Vertragsakte (z. B. Testdaten):
    /// entfernt Vertrag, Bestandteile, Dokument-Registrierungen und die
    /// zugehörigen Timeline-Verweise — die Dateien im Ablageverzeichnis
    /// bleiben liegen (Leitplanke) und werden im Ergebnis aufgelistet.
    /// Die Löschung selbst wird in Timeline und Audit-Log protokolliert.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = "Admin")]
    public async Task<ActionResult<VertragGeloescht>> Loeschen(int id, CancellationToken ct)
    {
        var vertrag = await db.Vertraege
            .Include(v => v.Bestandteile).ThenInclude(b => b.Dokumente)
            .FirstOrDefaultAsync(v => v.Id == id, ct);
        if (vertrag is null)
            return NotFound();

        var dateien = vertrag.Bestandteile.SelectMany(b => b.Dokumente)
            .Select(d => d.Dateipfad).Distinct().ToList();

        // Timeline-Einträge, die auf diesen Vertrag zeigen, mit entfernen (tote Links)
        var verweise = await db.Aktivitaeten
            .Where(a => a.VerweisTyp == AktivitaetsVerweisTyp.Vertrag && a.VerweisId == id)
            .ToListAsync(ct);
        db.Aktivitaeten.RemoveRange(verweise);

        db.Aktivitaeten.Add(await TimelineEintrag(vertrag.KundeId,
            $"Vertrag {vertrag.Vertragsnummer} gelöscht (Admin)",
            dateien.Count > 0 ? "Dateien verbleiben in der Ablage: " + string.Join("; ", dateien) : null,
            null, ct));

        db.ErlaubeVertragsdokumentLoeschung = true;
        db.Vertragsdokumente.RemoveRange(vertrag.Bestandteile.SelectMany(b => b.Dokumente));
        db.Vertragsbestandteile.RemoveRange(vertrag.Bestandteile);
        db.Vertraege.Remove(vertrag);
        await db.SaveChangesAsync(ct);
        db.ErlaubeVertragsdokumentLoeschung = false;

        return Ok(new VertragGeloescht(vertrag.Vertragsnummer, dateien));
    }

    /// <summary>Aktive Word-Vorlagen: Dateien direkt im Vorlagen-Verzeichnis (keine Unterordner).</summary>
    [HttpGet("vorlagen")]
    public async Task<ActionResult<List<string>>> Vorlagen(CancellationToken ct)
    {
        var verzeichnis = await Einstellung(EinstellungsSchluessel.VorlagenVerzeichnis, ct);
        if (string.IsNullOrWhiteSpace(verzeichnis) || !Directory.Exists(verzeichnis))
            return Ok(new List<string>());
        return Ok(Directory.EnumerateFiles(verzeichnis, "*.docx", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(n => n is not null && !n.StartsWith("~$"))
            .OrderBy(n => n)
            .ToList());
    }

    /// <summary>
    /// Erzeugt einen Vertrag aus einer Word-Vorlage: füllt Kundendaten ein
    /// (Muster-Texte der Bestandsvorlage und {{Platzhalter}}), legt die Datei im
    /// Zielverzeichnis ab und registriert sie an einer neuen Vertragsakte (Entwurf).
    /// Ausgabe vorerst .docx; PDF-Export folgt mit Telerik (Stufe 1b).
    /// </summary>
    [HttpPost("aus-vorlage")]
    public async Task<ActionResult<VorlagenErgebnis>> AusVorlage(VertragAusVorlage anlage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(anlage.Vorlage) || Path.GetFileName(anlage.Vorlage) != anlage.Vorlage)
            return BadRequest("Ungültiger Vorlagenname.");

        var kunde = await db.Kunden
            .Include(k => k.Adressen)
            .Include(k => k.Ansprechpartner)
            .FirstOrDefaultAsync(k => k.Id == anlage.KundeId, ct);
        if (kunde is null)
            return BadRequest("Der angegebene Kunde existiert nicht.");

        var vorlagenVerzeichnis = await Einstellung(EinstellungsSchluessel.VorlagenVerzeichnis, ct);
        var zielVerzeichnis = await Einstellung(EinstellungsSchluessel.VertragserzeugungZielverzeichnis, ct);
        var basis = await Einstellung(EinstellungsSchluessel.VertragsablageVerzeichnis, ct);
        if (string.IsNullOrWhiteSpace(vorlagenVerzeichnis) || string.IsNullOrWhiteSpace(zielVerzeichnis)
            || string.IsNullOrWhiteSpace(basis))
            return BadRequest("Vorlagen-, Ziel- oder Ablageverzeichnis ist nicht konfiguriert (Administration).");

        var vorlagenPfad = Path.Combine(vorlagenVerzeichnis, anlage.Vorlage);
        if (!System.IO.File.Exists(vorlagenPfad))
            return BadRequest($"Vorlage nicht gefunden: {anlage.Vorlage}");

        // Zielverzeichnis muss in der Vertragsablage liegen (Viewer/Registrierung)
        var basisVoll = Path.GetFullPath(basis).TrimEnd(Path.DirectorySeparatorChar);
        var zielVoll = Path.GetFullPath(zielVerzeichnis);
        if (!zielVoll.StartsWith(basisVoll + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !zielVoll.Equals(basisVoll, StringComparison.OrdinalIgnoreCase))
            return BadRequest("Das Zielverzeichnis liegt außerhalb des Vertragsablage-Verzeichnisses.");
        if (!Directory.Exists(zielVoll))
            return BadRequest($"Zielverzeichnis nicht erreichbar: {zielVerzeichnis}");

        var mitarbeiterId = await AktuellerMitarbeiterId(ct);
        if (mitarbeiterId is null)
            return BadRequest("Für den angemeldeten Benutzer existiert kein Mitarbeiter-Datensatz.");

        // Vorlage füllen
        using var speicher = new MemoryStream();
        await using (var quelle = System.IO.File.OpenRead(vorlagenPfad))
            await quelle.CopyToAsync(speicher, ct);
        var offene = Dienste.VorlagenDienst.Fuellen(speicher, await PlatzhalterWerte(kunde, ct));

        // Ablegen — Dateiname nach dem gewachsenen Ablage-Prinzip:
        // <KundenNr>.docx, bei Bestehen <KundenNr>-1.docx usw.
        var zielPfad = Path.Combine(zielVoll, kunde.Kundennummer + ".docx");
        for (var n = 1; System.IO.File.Exists(zielPfad); n++)
            zielPfad = Path.Combine(zielVoll, $"{kunde.Kundennummer}-{n}.docx");
        await System.IO.File.WriteAllBytesAsync(zielPfad, speicher.ToArray(), ct);

        string pruefsumme;
        await using (var stream = System.IO.File.OpenRead(zielPfad))
            pruefsumme = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, ct))
                .ToLowerInvariant();

        // Vertragsakte (Entwurf) + Bestandteil + Dokumentversion
        var nummer = await nummernkreis.NaechsteNummerAsync("Vertrag", ct);
        var vertrag = new Vertrag
        {
            Vertragsnummer = nummer,
            KundeId = kunde.Id,
            Status = VertragsStatus.Entwurf,
            Beginn = DateOnly.FromDateTime(DateTime.Today),
            Notiz = $"Aus Vorlage „{anlage.Vorlage}“ erstellt — nach Unterschrift Status und Laufzeit pflegen.",
        };
        vertrag.Bestandteile.Add(new Vertragsbestandteil
        {
            Typ = BestandteilTyp.Lizenzvertrag,
            Titel = Path.GetFileNameWithoutExtension(anlage.Vorlage),
            Vertrag = vertrag,
            Dokumente =
            {
                new Vertragsdokument
                {
                    Version = 1,
                    Dateipfad = zielPfad,
                    DateinameOriginal = Path.GetFileName(zielPfad),
                    Sha256 = pruefsumme,
                    ErstelltAm = DateTime.UtcNow,
                    ErstelltVonId = mitarbeiterId.Value,
                },
            },
        });
        db.Vertraege.Add(vertrag);
        await db.SaveChangesAsync(ct);

        db.Aktivitaeten.Add(await TimelineEintrag(kunde.Id,
            $"Vertrag {nummer} aus Vorlage „{anlage.Vorlage}“ erstellt", null, vertrag.Id, ct));
        await db.SaveChangesAsync(ct);

        return Ok(new VorlagenErgebnis(vertrag.Id, nummer, Path.GetFileName(zielPfad), offene));
    }

    private async Task<Dictionary<string, string>> PlatzhalterWerte(Domain.Customers.Kunde kunde, CancellationToken ct)
    {
        var hauptsitz = kunde.Adressen.FirstOrDefault(a => a.Typ == AdressTyp.Hauptsitz && a.Aktiv);
        var ansprechpartner = kunde.Ansprechpartner
            .Where(a => a.Aktiv)
            .OrderByDescending(a => a.Rollen.HasFlag(AnsprechpartnerRolle.Hauptansprechpartner))
            .ThenByDescending(a => a.Rollen != AnsprechpartnerRolle.Keine)
            .FirstOrDefault();
        var aboNummer = await db.Abos
            .Where(a => a.KundeId == kunde.Id && a.Status == Domain.Subscriptions.AboStatus.Laufend)
            .OrderByDescending(a => a.Id)
            .Select(a => a.Nummer)
            .FirstOrDefaultAsync(ct);

        return new Dictionary<string, string>
        {
            // {{Platzhalter}} für neue/überarbeitete Vorlagen
            ["{{Kundennummer}}"] = kunde.Kundennummer,
            ["{{Firmenname}}"] = kunde.Firmenname,
            ["{{Strasse}}"] = hauptsitz?.Strasse ?? "",
            ["{{Plz}}"] = hauptsitz?.Plz ?? "",
            ["{{Ort}}"] = hauptsitz?.Ort ?? "",
            ["{{PlzOrt}}"] = hauptsitz is null ? "" : $"{hauptsitz.Plz} {hauptsitz.Ort}".Trim(),
            ["{{Briefanrede}}"] = ansprechpartner?.Briefanrede() ?? "Sehr geehrte Damen und Herren",
            ["{{Ansprechpartner}}"] = ansprechpartner?.Anzeigename() ?? "",
            ["{{Email}}"] = ansprechpartner?.Email ?? "",
            ["{{UStIdNr}}"] = kunde.UStIdNr ?? "",
            ["{{AboNummer}}"] = aboNummer ?? "",
            ["{{Datum}}"] = DateTime.Today.ToString("dd.MM.yyyy"),
            // Muster-Texte der bestehenden Lizenzbedingungen-Vorlage
            ["Muster Straße 70"] = hauptsitz?.Strasse ?? "",
            // inkl. Muster-PLZ — sonst bliebe "57319" im erzeugten Vertrag stehen
            ["57319 Bad Musterburg"] = hauptsitz is null ? "" : $"{hauptsitz.Plz} {hauptsitz.Ort}".Trim(),
            ["Bad Musterburg"] = hauptsitz?.Ort ?? "",
            ["Muster GmbH"] = kunde.Firmenname,
            ["S0xxxx"] = aboNummer ?? "S0____",
            ["xxxxx"] = kunde.Kundennummer,
            ["57319"] = hauptsitz?.Plz ?? "",
        };
    }

    private async Task<string?> Einstellung(string schluessel, CancellationToken ct) =>
        (await db.Einstellungen.AsNoTracking().FirstOrDefaultAsync(e => e.Schluessel == schluessel, ct))?.Wert;

    /// <summary>
    /// Liefert die Datei einer registrierten Dokumentversion aus (Vorschau/Download).
    /// Es sind ausschließlich registrierte Dokumente erreichbar — kein freier
    /// Zugriff auf das Ablageverzeichnis.
    /// </summary>
    [HttpGet("dokumente/{dokumentId:int}/datei")]
    public async Task<IActionResult> DokumentDatei(int dokumentId, CancellationToken ct)
    {
        var dokument = await db.Vertragsdokumente.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == dokumentId, ct);
        if (dokument is null)
            return NotFound();
        if (!System.IO.File.Exists(dokument.Dateipfad))
            return NotFound($"Die Datei liegt nicht (mehr) am registrierten Pfad: {dokument.Dateipfad}");

        new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider()
            .TryGetContentType(dokument.DateinameOriginal, out var contentType);

        var stream = System.IO.File.OpenRead(dokument.Dateipfad);
        // Ohne fileDownloadName → Anzeige inline (Vorschau) statt Download
        return File(stream, contentType ?? "application/octet-stream", enableRangeProcessing: true);
    }

    // --- Hilfen ----------------------------------------------------------------

    private async Task<Aktivitaet> TimelineEintrag(
        int kundeId, string betreff, string? inhalt, int? vertragId, CancellationToken ct)
        => new()
        {
            KundeId = kundeId,
            Typ = AktivitaetsTyp.Vertrag,
            Zeitpunkt = DateTime.UtcNow,
            MitarbeiterId = await AktuellerMitarbeiterId(ct),
            Richtung = AktivitaetsRichtung.Intern,
            Betreff = betreff,
            Inhalt = inhalt,
            VerweisTyp = vertragId is not null ? AktivitaetsVerweisTyp.Vertrag : null,
            VerweisId = vertragId,
        };

    private Task<int?> AktuellerMitarbeiterId(CancellationToken ct) =>
        benutzer.EntraObjectId is null && benutzer.WindowsKonto is null
            ? Task.FromResult<int?>(null)
            : db.Mitarbeiter.Where(Domain.Shared.Mitarbeiter.KontoFilter(benutzer.EntraObjectId, benutzer.WindowsKonto))
                .Select(m => (int?)m.Id).FirstOrDefaultAsync(ct);

    private static VertragDetail NachDetail(Vertrag vertrag, string? ablage = null) => new(
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
                        d.ErstelltAm, d.ErstelltVon?.Anzeigename ?? "",
                        // Voller Ablage-Ordner (UNC) — fürs Öffnen im Explorer/Pfad-Kopieren im Frontend
                        string.IsNullOrEmpty(ablage) ? null : Path.GetDirectoryName(Path.Combine(ablage, d.Dateipfad))))
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
        BestandteilTyp.Wartungsvertrag => "Wartungsvertrag",
        BestandteilTyp.Bestellung => "Bestellung",
        BestandteilTyp.Installationsprotokoll => "Installationsprotokoll",
        BestandteilTyp.Kuendigung => "Kündigung",
        _ => "Sonstiges",
    };

    private static BestandteilTyp? BestandteilWert(string? text) => text?.Trim() switch
    {
        "Lizenzvertrag" => BestandteilTyp.Lizenzvertrag,
        "AVV" => BestandteilTyp.Avv,
        "NDA" => BestandteilTyp.Nda,
        "Sondervereinbarung" => BestandteilTyp.Sondervereinbarung,
        "Nachtrag" => BestandteilTyp.Nachtrag,
        "Wartungsvertrag" => BestandteilTyp.Wartungsvertrag,
        "Bestellung" => BestandteilTyp.Bestellung,
        "Installationsprotokoll" => BestandteilTyp.Installationsprotokoll,
        "Kündigung" => BestandteilTyp.Kuendigung,
        "Sonstiges" => BestandteilTyp.Sonstiges,
        _ => null,
    };
}
