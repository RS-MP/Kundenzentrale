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
public class VertraegeController(
    ErpDbContext db, NummernkreisDienst nummernkreis, IAktuellerBenutzer benutzer, Dienste.PdfDienst pdfDienst)
    : ControllerBase
{
    private const int MaxProSeite = 100;

    [HttpGet]
    public async Task<ActionResult<SeitenErgebnis<VertragListenEintrag>>> Liste(
        [FromQuery] string? suche, [FromQuery] string? status, [FromQuery] string? bestandteil,
        [FromQuery] int? kundeId, [FromQuery] int? partnerId, [FromQuery] string? filter,
        [FromQuery] int seite = 1, [FromQuery] int proSeite = 25,
        CancellationToken ct = default)
    {
        seite = Math.Max(1, seite);
        proSeite = Math.Clamp(proSeite, 1, MaxProSeite);

        var query = db.Vertraege.AsNoTracking();

        if (kundeId is not null)
            query = query.Where(v => v.KundeId == kundeId);
        if (partnerId is not null)
            query = query.Where(v => v.PartnerId == partnerId);
        // Arbeitsfilter „Partnerverträge"
        if (filter == "partner")
            query = query.Where(v => v.PartnerId != null);
        if (!string.IsNullOrWhiteSpace(suche))
        {
            var s = suche.Trim();
            query = query.Where(v => v.Vertragsnummer.Contains(s)
                || v.Kunde!.Firmenname.Contains(s) || v.Kunde!.Kundennummer.Contains(s)
                || v.Partner!.Name.Contains(s));
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
                v.Id, v.Vertragsnummer, v.KundeId, v.PartnerId,
                // Partnerverträge: Partnername an Stelle des Kunden
                KundeName = v.Kunde != null ? v.Kunde.Firmenname : v.Partner!.Name,
                Kundennummer = v.Kunde != null ? v.Kunde.Kundennummer : "Partner",
                Kurzinfos = v.Bestandteile.Where(b => b.Kurzinfo != null).Select(b => b.Kurzinfo!).ToList(),
                v.Status, v.Beginn, v.MindestlaufzeitMonate, v.VerlaengerungMonate,
                v.KuendigungsfristMonate, v.GekuendigtZum,
                Teile = v.Bestandteile
                    .Select(b => new BestandteilRang(b.Id, b.Typ, b.UnterzeichnetAm ?? b.Dokumentdatum, b.Folge))
                    .ToList(),
            })
            .ToListAsync(ct);

        var heute = DateOnly.FromDateTime(DateTime.Today);
        var berechnet = roh.Select(v => new
        {
            v.Id, v.Vertragsnummer, v.KundeId, v.PartnerId, v.KundeName, v.Kundennummer, v.Kurzinfos,
            v.Status, v.Beginn, v.MindestlaufzeitMonate, v.Teile,
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
                StatusText(v.Status), v.Beginn, v.Ende,
                BestandteilZusammenfassung(v.Teile.Select(t => t.Typ).ToList()), AktuellerStand(v.Teile),
                v.PartnerId, v.Kurzinfos))
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

    /// <summary>Kompakte Zusammenfassung als Kürzel-Pills: „WV · NT ×2 · BE ×5" (Tooltip = voller Name).</summary>
    private static List<BestandteilKurz> BestandteilZusammenfassung(List<BestandteilTyp> typen) =>
        typen.GroupBy(t => t)
            .OrderBy(g => Dienste.Dokumentarten.Rang(g.Key))
            .Select(g => new BestandteilKurz(
                Dienste.Dokumentarten.Kuerzel(g.Key),
                g.Count() == 1 ? Dienste.Dokumentarten.Name(g.Key) : $"{g.Count()} {Dienste.Dokumentarten.Mehrzahl(g.Key)}",
                g.Count()))
            .ToList();

    // --- Reihenfolge der Dokumente (FEEDBACK Nr. 18) ---------------------------

    private record BestandteilRang(int Id, BestandteilTyp Typ, DateOnly? Stand, int Folge);

    /// <summary>
    /// Zeitliche Reihenfolge, älteste zuerst: Stand (unterzeichnet bzw.
    /// Dokumentdatum), bei gleichem Datum die Folgenummer (4228-1, -2 …),
    /// zuletzt die Erfassungsreihenfolge. Ohne Datum = ganz vorne (unbekannt alt).
    /// </summary>
    private static List<BestandteilRang> Chronologisch(IEnumerable<BestandteilRang> teile) =>
        teile.OrderBy(t => t.Stand ?? DateOnly.MinValue).ThenBy(t => t.Folge).ThenBy(t => t.Id).ToList();

    /// <summary>Laufende Nummer je Dokumentart — nur, wenn es mehrere gibt („Bestellung 3").</summary>
    private static Dictionary<int, int> LaufendeNummern(IEnumerable<BestandteilRang> teile) =>
        teile.GroupBy(t => t.Typ)
            .Where(g => g.Count() > 1)
            .SelectMany(g => Chronologisch(g).Select((t, i) => (t.Id, Nummer: i + 1)))
            .ToDictionary(x => x.Id, x => x.Nummer);

    /// <summary>Jüngstes datiertes Dokument der Akte, z. B. „Bestellung 3 · 08.09.2026".</summary>
    private static StandEintrag? AktuellerStand(List<BestandteilRang> teile)
    {
        var juengster = Chronologisch(teile.Where(t => t.Stand is not null)).LastOrDefault();
        if (juengster is null)
            return null;
        var nummer = LaufendeNummern(teile).TryGetValue(juengster.Id, out var n) ? $" {n}" : "";
        return new StandEintrag(Dienste.Dokumentarten.Kuerzel(juengster.Typ),
            Dienste.Dokumentarten.Name(juengster.Typ) + nummer, juengster.Stand!.Value);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<VertragDetail>> Einzeln(int id, CancellationToken ct)
    {
        var vertrag = await db.Vertraege
            .AsNoTracking()
            .Include(v => v.Kunde)
            .Include(v => v.Partner)
            .Include(v => v.Bestandteile)
                .ThenInclude(b => b.Dokumente)
                    .ThenInclude(d => d.ErstelltVon)
            .Include(v => v.Bestandteile)
                .ThenInclude(b => b.Dokumente)
                    .ThenInclude(d => d.VorlagenVersion!)
                        .ThenInclude(x => x.Vorlage)
            .FirstOrDefaultAsync(v => v.Id == id, ct);
        if (vertrag is null)
            return NotFound();

        return Ok(NachDetail(vertrag, await Einstellung(EinstellungsSchluessel.VertragsablageVerzeichnis, ct),
            await DarfVertraulicheAsync(ct)));
    }

    [HttpPost]
    public async Task<ActionResult<VertragDetail>> Anlegen(VertragAnlegen anlage, CancellationToken ct)
    {
        // Genau eine Vertragspartei: Kunde oder Partner (Partnervertrag)
        if ((anlage.KundeId is null) == (anlage.PartnerId is null))
            return BadRequest("Bitte entweder einen Kunden oder einen Partner angeben.");
        Kunde? kunde = null;
        Partner? partner = null;
        if (anlage.KundeId is { } kundeId && (kunde = await db.Kunden.FindAsync([kundeId], ct)) is null)
            return BadRequest("Der angegebene Kunde existiert nicht.");
        if (anlage.PartnerId is { } partnerId && (partner = await db.Partner.FindAsync([partnerId], ct)) is null)
            return BadRequest("Der angegebene Partner existiert nicht.");

        // Nummer zuerst ziehen (eigener SaveChanges), dann den Vertrag anlegen
        var nummer = await nummernkreis.NaechsteNummerAsync("Vertrag", ct);

        var vertrag = new Vertrag
        {
            Vertragsnummer = nummer,
            KundeId = kunde?.Id,
            PartnerId = partner?.Id,
            Status = VertragsStatus.Entwurf,
            Beginn = anlage.Beginn,
            MindestlaufzeitMonate = anlage.MindestlaufzeitMonate,
            VerlaengerungMonate = anlage.VerlaengerungMonate,
            KuendigungsfristMonate = anlage.KuendigungsfristMonate,
            Notiz = anlage.Notiz,
        };
        db.Vertraege.Add(vertrag);

        // Ein neuer Vertrag holt archivierte Kunden automatisch aus dem Archiv
        if (kunde?.ArchiviertAm != null)
        {
            kunde.ArchiviertAm = null;
            await VerlaufAsync(kunde.Id,
                "Aus dem Archiv geholt — neuer Vertrag angelegt", null, null, ct);
        }
        await db.SaveChangesAsync(ct);

        // Timeline-Eintrag mit Sprungziel — braucht die gespeicherte Vertrags-Id
        await VerlaufAsync(kunde?.Id,
            $"Vertrag {nummer} angelegt", null, vertrag.Id, ct);
        await db.SaveChangesAsync(ct);

        vertrag.Kunde = kunde;
        vertrag.Partner = partner;
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
            await VerlaufAsync(vertrag.KundeId,
                $"Vertrag {vertrag.Vertragsnummer}: {StatusText(alterStatus)} → {StatusText(neuerStatus)}",
                null, vertrag.Id, ct);
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
        if (anlage.Vertraulich && !await DarfVertraulicheAsync(ct))
            return KeinZugriffVertraulich();

        db.Vertragsbestandteile.Add(new Vertragsbestandteil
        {
            VertragId = vertrag.Id,
            Typ = typ,
            Titel = anlage.Titel.Trim(),
            Beschreibung = anlage.Beschreibung,
            UnterzeichnetAm = anlage.UnterzeichnetAm,
            Vertraulich = anlage.Vertraulich,
            Kurzinfo = Kuerzen(anlage.Kurzinfo),
        });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Bestandteil korrigieren — Dokumentart, Titel, Daten. Ein hier gesetztes
    /// Dokumentdatum gilt als manuell gepflegt und wird von der automatischen
    /// Erkennung nicht mehr überschrieben.
    /// </summary>
    [HttpPut("bestandteile/{bestandteilId:int}")]
    public async Task<IActionResult> BestandteilAendern(int bestandteilId, BestandteilAendern aenderung, CancellationToken ct)
    {
        var bestandteil = await db.Vertragsbestandteile.Include(b => b.Vertrag)
            .FirstOrDefaultAsync(b => b.Id == bestandteilId, ct);
        if (bestandteil is null)
            return NotFound();
        if (BestandteilWert(aenderung.Typ) is not { } typ)
            return BadRequest($"Unbekannter Bestandteil-Typ '{aenderung.Typ}'.");
        if (string.IsNullOrWhiteSpace(aenderung.Titel))
            return BadRequest("Der Titel darf nicht leer sein.");
        // Vertrauliches ändern oder Vertraulichkeit umschalten nur mit Berechtigung
        if ((bestandteil.Vertraulich || aenderung.Vertraulich) && !await DarfVertraulicheAsync(ct))
            return KeinZugriffVertraulich();
        if (bestandteil.Vertraulich && !aenderung.Vertraulich
            && await db.Vertragsdokumente.AnyAsync(d => d.VertragsbestandteilId == bestandteil.Id, ct))
            return BadRequest("Der Bestandteil hat Dokumente aus der vertraulichen Ablage — " +
                "die Vertraulichkeit kann deshalb nicht aufgehoben werden.");

        var aenderungen = new List<string>();
        if (bestandteil.Typ != typ)
            aenderungen.Add($"Art {BestandteilText(bestandteil.Typ)} → {BestandteilText(typ)}");
        if (bestandteil.Dokumentdatum != aenderung.Dokumentdatum)
            aenderungen.Add($"Dokumentdatum {aenderung.Dokumentdatum?.ToString("dd.MM.yyyy") ?? "entfernt"}");
        if (bestandteil.Vertraulich != aenderung.Vertraulich)
            aenderungen.Add(aenderung.Vertraulich ? "als vertraulich markiert" : "Vertraulichkeit aufgehoben");

        bestandteil.Typ = typ;
        bestandteil.Titel = aenderung.Titel.Trim();
        bestandteil.Beschreibung = string.IsNullOrWhiteSpace(aenderung.Beschreibung) ? null : aenderung.Beschreibung;
        bestandteil.UnterzeichnetAm = aenderung.UnterzeichnetAm;
        bestandteil.Vertraulich = aenderung.Vertraulich;
        bestandteil.Kurzinfo = Kuerzen(aenderung.Kurzinfo);
        if (bestandteil.Dokumentdatum != aenderung.Dokumentdatum)
        {
            bestandteil.Dokumentdatum = aenderung.Dokumentdatum;
            bestandteil.DokumentdatumQuelle = aenderung.Dokumentdatum is null ? null : DokumentdatumQuelle.Manuell;
        }

        if (aenderungen.Count > 0)
            await VerlaufAsync(bestandteil.Vertrag!.KundeId,
                // Den Verlauf sehen alle — vertrauliche Titel gehören nicht hinein
                $"Vertrag {bestandteil.Vertrag.Vertragsnummer}: {VerlaufsName(bestandteil)} — {string.Join(", ", aenderungen)}",
                null, bestandteil.VertragId, ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Nachrüstung (FEEDBACK Nr. 18): Dokumentdatum für alle Bestandteile ohne
    /// Datum aus Dateiname bzw. Datei-Änderungsdatum ermitteln. Idempotent —
    /// gepflegte Daten bleiben unverändert.
    /// </summary>
    [HttpPost("dokumentdaten-nachtragen")]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = "Admin")]
    public async Task<ActionResult<DokumentdatenNachgetragen>> DokumentdatenNachtragen(CancellationToken ct)
    {
        var ergebnis = await new DokumentdatumDienst(db).NachtragenAsync(ct);
        return Ok(new DokumentdatenNachgetragen(
            ergebnis.Geprueft, ergebnis.AusDateiname, ergebnis.AusDateidatum, ergebnis.OhneDatei));
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
        if (bestandteil.Vertraulich && !await DarfVertraulicheAsync(ct))
            return KeinZugriffVertraulich();

        // Vertrauliche Dokumente kommen ausschließlich aus der vertraulichen Ablage,
        // alle anderen ausschließlich aus der normalen Vertragsablage
        var basis = await Einstellung(bestandteil.Vertraulich
            ? EinstellungsSchluessel.VertraulicheAblageVerzeichnis
            : EinstellungsSchluessel.VertragsablageVerzeichnis, ct);
        var ablageName = bestandteil.Vertraulich ? "vertraulichen Ablage" : "Vertragsablage";
        if (string.IsNullOrWhiteSpace(basis))
            return BadRequest($"Das Verzeichnis der {ablageName} ist nicht konfiguriert (Administration → Verzeichnisse).");

        // Pfad-Sicherheit: nur Dateien unterhalb des Basisverzeichnisses
        var basisVoll = Path.GetFullPath(basis);
        var dateiVoll = Path.GetFullPath(Path.Combine(basisVoll, registrierung.RelativerPfad));
        if (!LiegtUnter(dateiVoll, basisVoll))
            return BadRequest($"Der Pfad liegt außerhalb der {ablageName}.");
        if (!bestandteil.Vertraulich
            && await Einstellung(EinstellungsSchluessel.VertraulicheAblageVerzeichnis, ct) is { Length: > 0 } vertraulich
            && LiegtUnter(dateiVoll, Path.GetFullPath(vertraulich)))
            return BadRequest("Die Datei liegt in der vertraulichen Ablage — bitte an einem vertraulichen Bestandteil registrieren.");
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
        // Stand des Dokuments für die Reihenfolge — nur, falls noch keiner gepflegt ist
        DokumentdatumDienst.Uebernehmen(bestandteil, dateiVoll);
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

        await VerlaufAsync(bestandteil.Vertrag!.KundeId,
            bestandteil.Vertraulich
                ? $"Vertrag {bestandteil.Vertrag.Vertragsnummer}: vertrauliches Dokument als Version {version} zu {VerlaufsName(bestandteil)} registriert"
                : $"Vertrag {bestandteil.Vertrag.Vertragsnummer}: Dokument „{Path.GetFileName(dateiVoll)}“ als Version {version} zu {bestandteil.Titel} registriert",
            null, bestandteil.VertragId, ct);

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

        // Pfade vertraulicher Dokumente nicht in Verlauf/Ergebnis schreiben (Admin ≠ Einsicht)
        var dateien = vertrag.Bestandteile.Where(b => !b.Vertraulich).SelectMany(b => b.Dokumente)
            .Select(d => d.Dateipfad).Distinct().ToList();
        var vertraulicheDateien = vertrag.Bestandteile.Where(b => b.Vertraulich)
            .SelectMany(b => b.Dokumente).Select(d => d.Dateipfad).Distinct().Count();
        if (vertraulicheDateien > 0)
            dateien.Add($"{vertraulicheDateien} Datei(en) in der vertraulichen Ablage");

        // Timeline-Einträge, die auf diesen Vertrag zeigen, mit entfernen (tote Links)
        var verweise = await db.Aktivitaeten
            .Where(a => a.VerweisTyp == AktivitaetsVerweisTyp.Vertrag && a.VerweisId == id)
            .ToListAsync(ct);
        db.Aktivitaeten.RemoveRange(verweise);

        await VerlaufAsync(vertrag.KundeId,
            $"Vertrag {vertrag.Vertragsnummer} gelöscht (Admin)",
            dateien.Count > 0 ? "Dateien verbleiben in der Ablage: " + string.Join("; ", dateien) : null,
            null, ct);

        db.ErlaubeVertragsdokumentLoeschung = true;
        db.Vertragsdokumente.RemoveRange(vertrag.Bestandteile.SelectMany(b => b.Dokumente));
        db.Vertragsbestandteile.RemoveRange(vertrag.Bestandteile);
        db.Vertraege.Remove(vertrag);
        await db.SaveChangesAsync(ct);
        db.ErlaubeVertragsdokumentLoeschung = false;

        return Ok(new VertragGeloescht(vertrag.Vertragsnummer, dateien));
    }

    /// <summary>
    /// Auswahl für die Vertragserzeugung: je aktiver, für die eigene Rolle
    /// freigegebener Vorlage die aktuell gültige Version (Freigabe unter
    /// „Vorlagen", nur Compliance; Stichtag über Gültig-ab).
    /// </summary>
    [HttpGet("vorlagen")]
    public async Task<ActionResult<List<VorlageAuswahl>>> Vorlagen(CancellationToken ct)
    {
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var rollen = await AktuelleRollen(ct);
        var vorlagen = await db.Vorlagen.AsNoTracking()
            .Where(v => v.Aktiv)
            .OrderBy(v => v.Name)
            .Select(v => new
            {
                Vorlage = v,
                Gueltig = v.Versionen.Where(x => x.GueltigAb <= heute)
                    .OrderByDescending(x => x.Nummer).FirstOrDefault(),
            })
            .ToListAsync(ct);
        return Ok(vorlagen
            .Where(v => v.Gueltig is not null && v.Vorlage.ErlaubtFuer(rollen))
            .Select(v => new VorlageAuswahl(v.Gueltig!.Id, v.Vorlage.Name, v.Gueltig.Nummer,
                Dienste.Dokumentarten.Kuerzel(v.Vorlage.Typ)))
            .ToList());
    }

    /// <summary>
    /// Erzeugt einen Vertrag aus einer freigegebenen Vorlagen-Version: füllt
    /// Kundendaten ein, wandelt nach PDF (Telerik, Metadaten + optionale
    /// digitale Signatur), legt PDF + docx-Quelle im Zielverzeichnis ab und
    /// registriert das PDF an einer neuen Vertragsakte (Entwurf).
    /// </summary>
    [HttpPost("aus-vorlage")]
    public async Task<ActionResult<VorlagenErgebnis>> AusVorlage(VertragAusVorlage anlage, CancellationToken ct)
    {
        var version = await db.VorlagenVersionen.AsNoTracking()
            .Include(v => v.Vorlage)
            .FirstOrDefaultAsync(v => v.Id == anlage.VorlagenVersionId, ct);
        if (version?.Vorlage is null)
            return BadRequest("Die angegebene Vorlagen-Version existiert nicht.");
        var vorlagenStand = $"{version.Vorlage.Name} v{version.Nummer}";
        // Serverseitig durchsetzen, was die Auswahl in der Akte anbietet
        if (!version.Vorlage.Aktiv)
            return BadRequest($"Die Vorlage „{version.Vorlage.Name}“ ist zurückgezogen.");
        if (!version.Vorlage.ErlaubtFuer(await AktuelleRollen(ct)))
            return StatusCode(StatusCodes.Status403Forbidden,
                $"Die Vorlage „{version.Vorlage.Name}“ ist für Ihre Rolle nicht freigegeben.");
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var gueltigeId = await db.VorlagenVersionen
            .Where(v => v.VorlageId == version.VorlageId && v.GueltigAb <= heute)
            .OrderByDescending(v => v.Nummer).Select(v => (int?)v.Id).FirstOrDefaultAsync(ct);
        if (gueltigeId != version.Id)
            return BadRequest($"„{vorlagenStand}“ ist nicht der aktuell gültige Stand — bitte die Seite neu laden.");

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

        var vorlagenPfad = Path.Combine(vorlagenVerzeichnis, version.Dateipfad);
        if (!System.IO.File.Exists(vorlagenPfad))
            return BadRequest($"Die eingefrorene Versionsdatei fehlt in der Ablage: {version.Dateipfad}");

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

        // Angebot für „Angebot S0…": gewähltes, sonst neuestes offenes Angebot
        // (Odoo-Abo im Entwurf), sonst laufendes Abo
        var angebote = db.Abos.Where(a => a.KundeId == kunde.Id);
        string? angebotsNummer;
        if (anlage.AboId == 0)
            angebotsNummer = null; // bewusst ohne Angebotsnummer
        else if (anlage.AboId is { } aboId)
        {
            angebotsNummer = await angebote.Where(a => a.Id == aboId).Select(a => a.Nummer).FirstOrDefaultAsync(ct);
            if (angebotsNummer is null)
                return BadRequest("Das gewählte Angebot gehört nicht zu diesem Kunden.");
        }
        else
        {
            angebotsNummer = await angebote
                .Where(a => a.Status == Domain.Subscriptions.AboStatus.Entwurf
                    || a.Status == Domain.Subscriptions.AboStatus.Laufend)
                .OrderBy(a => a.Status)          // Entwurf (= Angebot) vor Laufend
                .ThenByDescending(a => a.Id)     // neuestes zuerst
                .Select(a => a.Nummer)
                .FirstOrDefaultAsync(ct);
        }

        // Vorlage füllen
        using var speicher = new MemoryStream();
        await using (var quelle = System.IO.File.OpenRead(vorlagenPfad))
            await quelle.CopyToAsync(speicher, ct);
        var offene = Dienste.VorlagenDienst.Fuellen(speicher, PlatzhalterWerte(kunde, angebotsNummer));
        if (angebotsNummer is null && anlage.AboId != 0)
            offene.Add("Angebotsnummer (kein Angebot/Abo beim Kunden)");

        // Vertragsnummer vorab — sie steht in den PDF-Metadaten/der Signatur
        var nummer = await nummernkreis.NaechsteNummerAsync("Vertrag", ct);

        // Word → PDF (mit Metadaten, optional digital signiert — Stufe 1b)
        byte[] pdfBytes;
        try
        {
            pdfBytes = await pdfDienst.DocxZuPdfAsync(
                speicher.ToArray(), nummer, vorlagenStand, kunde.Firmenname, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        // Ablegen — Dateiname nach dem gewachsenen Ablage-Prinzip: <KundenNr>.pdf,
        // bei Bestehen <KundenNr>-1.pdf usw.; die gefüllte .docx liegt als
        // bearbeitbare Quelle gleichnamig daneben (registriert wird das PDF).
        var zielPfad = Path.Combine(zielVoll, kunde.Kundennummer + ".pdf");
        for (var n = 1; System.IO.File.Exists(zielPfad)
             || System.IO.File.Exists(Path.ChangeExtension(zielPfad, ".docx")); n++)
            zielPfad = Path.Combine(zielVoll, $"{kunde.Kundennummer}-{n}.pdf");
        await System.IO.File.WriteAllBytesAsync(zielPfad, pdfBytes, ct);
        await System.IO.File.WriteAllBytesAsync(Path.ChangeExtension(zielPfad, ".docx"), speicher.ToArray(), ct);

        string pruefsumme;
        await using (var stream = System.IO.File.OpenRead(zielPfad))
            pruefsumme = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, ct))
                .ToLowerInvariant();

        // Vertragsakte (Entwurf) + Bestandteil + Dokumentversion
        var vertrag = new Vertrag
        {
            Vertragsnummer = nummer,
            KundeId = kunde.Id,
            Status = VertragsStatus.Entwurf,
            Beginn = DateOnly.FromDateTime(DateTime.Today),
            Notiz = $"Aus Vorlage „{vorlagenStand}“ erstellt"
                + (angebotsNummer is null ? "" : $" zu Angebot {angebotsNummer}")
                + " — nach Unterschrift Status und Laufzeit pflegen.",
        };
        vertrag.Bestandteile.Add(new Vertragsbestandteil
        {
            Typ = version.Vorlage.Typ,
            Titel = version.Vorlage.Name,
            Vertrag = vertrag,
            Dokumentdatum = heute,
            DokumentdatumQuelle = DokumentdatumQuelle.Erzeugt,
            Folge = DokumentdatumErkennung.Folge(zielPfad),
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
                    // Vorlagen-Stand am Dokument: welcher Kunde hat welche Version bekommen
                    VorlagenVersionId = version.Id,
                },
            },
        });
        db.Vertraege.Add(vertrag);
        await db.SaveChangesAsync(ct);

        await VerlaufAsync(kunde.Id,
            $"Vertrag {nummer} aus Vorlage „{vorlagenStand}“ erstellt", null, vertrag.Id, ct);
        await db.SaveChangesAsync(ct);

        return Ok(new VorlagenErgebnis(vertrag.Id, nummer, Path.GetFileName(zielPfad), offene));
    }

    private static Dictionary<string, string> PlatzhalterWerte(Domain.Customers.Kunde kunde, string? aboNummer)
    {
        var hauptsitz = kunde.Adressen.FirstOrDefault(a => a.Typ == AdressTyp.Hauptsitz && a.Aktiv);
        var ansprechpartner = kunde.Ansprechpartner
            .Where(a => a.Aktiv)
            .OrderByDescending(a => a.Rollen.HasFlag(AnsprechpartnerRolle.Hauptansprechpartner))
            .ThenByDescending(a => a.Rollen != AnsprechpartnerRolle.Keine)
            .FirstOrDefault();
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
            .Include(d => d.Vertragsbestandteil)
            .FirstOrDefaultAsync(d => d.Id == dokumentId, ct);
        if (dokument is null)
            return NotFound();
        if (dokument.Vertragsbestandteil!.Vertraulich && !await DarfVertraulicheAsync(ct))
            return KeinZugriffVertraulich();
        if (!System.IO.File.Exists(dokument.Dateipfad))
            return NotFound($"Die Datei liegt nicht (mehr) am registrierten Pfad: {dokument.Dateipfad}");

        new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider()
            .TryGetContentType(dokument.DateinameOriginal, out var contentType);

        var stream = System.IO.File.OpenRead(dokument.Dateipfad);
        // Ohne fileDownloadName → Anzeige inline (Vorschau) statt Download
        return File(stream, contentType ?? "application/octet-stream", enableRangeProcessing: true);
    }

    // --- Hilfen ----------------------------------------------------------------

    /// <summary>
    /// Verlaufseintrag in der Kundenakte. Partnerverträge (ohne Kunde) haben
    /// noch keinen eigenen Verlauf — dann wird nichts geschrieben.
    /// </summary>
    private async Task VerlaufAsync(
        int? kundeId, string betreff, string? inhalt, int? vertragId, CancellationToken ct)
    {
        if (kundeId is { } id)
            db.Aktivitaeten.Add(await TimelineEintrag(id, betreff, inhalt, vertragId, ct));
    }

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

    private async Task<MitarbeiterRolle> AktuelleRollen(CancellationToken ct) =>
        benutzer.EntraObjectId is null && benutzer.WindowsKonto is null
            ? MitarbeiterRolle.Keine
            : await db.Mitarbeiter.Where(Domain.Shared.Mitarbeiter.KontoFilter(benutzer.EntraObjectId, benutzer.WindowsKonto))
                .Where(m => m.Aktiv).Select(m => m.Rollen).FirstOrDefaultAsync(ct);

    // --- Vertraulichkeit (FEEDBACK Nr. 20) -------------------------------------

    /// <summary>Rolle „Vertrauliche Verträge" — bewusst ohne Admin-Automatik.</summary>
    private async Task<bool> DarfVertraulicheAsync(CancellationToken ct) =>
        (await AktuelleRollen(ct)).HasFlag(MitarbeiterRolle.VertraulicheVertraege);

    private ObjectResult KeinZugriffVertraulich() => StatusCode(StatusCodes.Status403Forbidden,
        "Vertraulicher Bestandteil — nur mit der Rolle „Vertrauliche Verträge“ (Administration → Benutzer & Rollen).");

    /// <summary>Name für den für alle sichtbaren Verlauf: bei vertraulichen nur die Dokumentart.</summary>
    private static string VerlaufsName(Vertragsbestandteil b) =>
        b.Vertraulich ? $"vertrauliche(r) {BestandteilText(b.Typ)}" : b.Titel;

    private static string? Kuerzen(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim() is { Length: > 300 } lang ? lang[..300] : text.Trim();

    private static bool LiegtUnter(string pfad, string basis) =>
        pfad.StartsWith(basis.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private static VertragDetail NachDetail(Vertrag vertrag, string? ablage = null, bool darfVertrauliche = false)
    {
        var raenge = vertrag.Bestandteile
            .Select(b => new BestandteilRang(b.Id, b.Typ, b.Stand, b.Folge)).ToList();
        var nummern = LaufendeNummern(raenge);
        var stand = AktuellerStand(raenge);
        var juengsterId = Chronologisch(raenge.Where(r => r.Stand is not null)).LastOrDefault()?.Id;
        // Gruppiert nach Dokumentart (Hauptverträge zuerst), innerhalb neuestes oben
        var reihenfolge = raenge
            .GroupBy(r => Dienste.Dokumentarten.Rang(r.Typ))
            .OrderBy(g => g.Key)
            .SelectMany(g => Chronologisch(g).AsEnumerable().Reverse())
            .Select((r, i) => (r.Id, Position: i))
            .ToDictionary(x => x.Id, x => x.Position);

        return NachDetail(vertrag, ablage, nummern, stand, juengsterId, reihenfolge, darfVertrauliche);
    }

    private static VertragDetail NachDetail(
        Vertrag vertrag, string? ablage, Dictionary<int, int> nummern, StandEintrag? stand,
        int? juengsterId, Dictionary<int, int> reihenfolge, bool darfVertrauliche) => new(
        vertrag.Id,
        vertrag.Vertragsnummer,
        vertrag.KundeId,
        vertrag.Kunde?.Firmenname ?? vertrag.Partner?.Name ?? "",
        vertrag.Kunde?.Kundennummer ?? (vertrag.PartnerId is not null ? "Partner" : ""),
        StatusText(vertrag.Status),
        vertrag.Beginn,
        vertrag.MindestlaufzeitMonate,
        vertrag.VerlaengerungMonate,
        vertrag.KuendigungsfristMonate,
        vertrag.GekuendigtZum,
        vertrag.NaechstesVertragsende(DateOnly.FromDateTime(DateTime.Today)),
        vertrag.Notiz,
        vertrag.Bestandteile
            .OrderBy(b => reihenfolge.GetValueOrDefault(b.Id))
            .Select(b => NachBestandteil(b, ablage, nummern, juengsterId, darfVertrauliche))
            .ToList(),
        stand,
        darfVertrauliche,
        vertrag.PartnerId);

    private static BestandteilDetail NachBestandteil(
        Vertragsbestandteil b, string? ablage, Dictionary<int, int> nummern, int? juengsterId, bool darfVertrauliche)
    {
        // Vertraulich ohne Berechtigung: nur Art, Datum und Kurzinfo — Titel,
        // Beschreibung und Dokumente (inkl. Pfade) verlassen den Server nicht.
        var gesperrt = b.Vertraulich && !darfVertrauliche;
        return new BestandteilDetail(
            b.Id, BestandteilText(b.Typ), Dienste.Dokumentarten.Kuerzel(b.Typ),
            gesperrt ? "Vertraulich" : b.Titel,
            gesperrt ? null : b.Beschreibung,
            b.UnterzeichnetAm, b.Dokumentdatum, b.DokumentdatumQuelle?.ToString(),
            nummern.TryGetValue(b.Id, out var nummer) ? nummer : null,
            b.Id == juengsterId,
            b.Vertraulich, gesperrt, b.Kurzinfo,
            gesperrt
                ? []
                : b.Dokumente
                    .OrderByDescending(d => d.Version)
                    .Select(d => new DokumentEintrag(
                        d.Id, d.Version, d.Dateipfad, d.DateinameOriginal, d.Sha256,
                        d.ErstelltAm, d.ErstelltVon?.Anzeigename ?? "",
                        // Voller Ablage-Ordner (UNC) — fürs Öffnen im Explorer/Pfad-Kopieren im Frontend
                        string.IsNullOrEmpty(ablage) ? null : Path.GetDirectoryName(Path.Combine(ablage, d.Dateipfad)),
                        d.VorlagenVersion?.Vorlage is null ? null
                            : $"{d.VorlagenVersion.Vorlage.Name} v{d.VorlagenVersion.Nummer}"))
                    .ToList());
    }

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

    internal static string BestandteilText(BestandteilTyp typ) => Dienste.Dokumentarten.Name(typ);

    private static BestandteilTyp? BestandteilWert(string? text) => Dienste.Dokumentarten.Wert(text);
}
