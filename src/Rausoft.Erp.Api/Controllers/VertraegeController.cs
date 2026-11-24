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
    ErpDbContext db, NummernkreisDienst nummernkreis, Dienste.PdfDienst pdfDienst,
    Dienste.AngemeldeterMitarbeiter angemeldet)
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
        // Zusammengefasste Verträge (11/2026): Die Liste zeigt je Gruppe den Hauptvertrag,
        // die Anhänge hängen darunter. Suche und Filter wirken deshalb auf die ganze Gruppe —
        // ein Treffer im Anhang zeigt die Gruppe; der Status ist der des Hauptvertrags.
        if (!string.IsNullOrWhiteSpace(suche))
        {
            var s = suche.Trim();
            var gruppen = db.Vertraege
                .Where(v => v.Vertragsnummer.Contains(s)
                    || (v.Bezeichnung != null && v.Bezeichnung.Contains(s))
                    || v.Kunde!.Firmenname.Contains(s) || v.Kunde!.Kundennummer.Contains(s)
                    || v.Partner!.Name.Contains(s))
                .Select(v => v.HauptvertragId ?? v.Id);
            query = query.Where(v => gruppen.Contains(v.HauptvertragId ?? v.Id));
        }
        if (StatusWert(status) is { } statusWert)
            query = query.Where(v => (v.Hauptvertrag != null ? v.Hauptvertrag.Status : v.Status) == statusWert);
        // Explizite Anforderung: „Wer hat eine Sondervereinbarung?" — Filter über den Bestandteil-Typ
        if (BestandteilWert(bestandteil) is { } typWert)
        {
            var gruppen = db.Vertraege
                .Where(v => v.Bestandteile.Any(b => b.Typ == typWert))
                .Select(v => v.HauptvertragId ?? v.Id);
            query = query.Where(v => gruppen.Contains(v.HauptvertragId ?? v.Id));
        }

        // Nächstmögliches Ende wird berechnet → Arbeitsfilter/Paging im Speicher
        var roh = await query.Select(AlsZeile).ToListAsync(ct);
        var darfVertrauliche = await DarfVertraulicheAsync(ct);

        // Gruppieren: Anhänge unter ihren Hauptvertrag. Fehlt der Hauptvertrag in der
        // Ergebnismenge (dürfte nicht vorkommen), bleibt der Anhang als eigene Zeile sichtbar.
        var vorhanden = roh.Select(v => v.Id).ToHashSet();
        var anhaengeVon = roh
            .Where(v => v.HauptvertragId is { } h && vorhanden.Contains(h))
            .ToLookup(v => v.HauptvertragId!.Value);

        var heute = DateOnly.FromDateTime(DateTime.Today);
        var berechnet = roh
            .Where(v => v.HauptvertragId is not { } h || !vorhanden.Contains(h))
            .Select(v => new
            {
                v.Id, v.Vertragsnummer, v.Bezeichnung, v.KundeId, v.PartnerId, v.KundeName, v.Kundennummer,
                v.Kurzinfos, v.Status, v.Beginn, v.MindestlaufzeitMonate, v.Teile,
                Ende = Ende(v, heute),
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
                BestandteilZusammenfassung(v.Teile.Select(t => t.Typ).ToList()), AktuellerStand(Raenge(v.Teile)),
                v.PartnerId, v.Kurzinfos, v.Bezeichnung,
                DokumenteKurz(v.Teile, darfVertrauliche),
                anhaengeVon[v.Id]
                    .OrderBy(a => a.Vertragsnummer, StringComparer.OrdinalIgnoreCase)
                    .Select(a => NachAnhang(a, darfVertrauliche))
                    .ToList()))
            .ToList();

        return Ok(new SeitenErgebnis<VertragListenEintrag>(eintraege, gefiltert.Count, seite, proSeite));
    }

    // --- Zeilen für Liste, Anhänge und Auswahl ---------------------------------

    private sealed record BestandteilZeile(
        int Id, BestandteilTyp Typ, DateOnly? Stand, int Folge, string Titel, bool Vertraulich, int? GehoertZuId);

    /// <summary>Was Liste, Anhang-Anzeige und Zuordnungs-Auswahl von einem Vertrag brauchen.</summary>
    private sealed record VertragZeile(
        int Id, string Vertragsnummer, string? Bezeichnung, int? HauptvertragId, bool HatAnhaenge,
        int? KundeId, int? PartnerId, string KundeName, string Kundennummer,
        VertragsStatus Status, DateOnly Beginn, int? MindestlaufzeitMonate, int? VerlaengerungMonate,
        int? KuendigungsfristMonate, DateOnly? GekuendigtZum,
        List<string> Kurzinfos, List<BestandteilZeile> Teile);

    private static readonly System.Linq.Expressions.Expression<Func<Vertrag, VertragZeile>> AlsZeile =
        v => new VertragZeile(
            v.Id, v.Vertragsnummer, v.Bezeichnung, v.HauptvertragId, v.Anhaenge.Any(),
            v.KundeId, v.PartnerId,
            // Partnerverträge: Partnername an Stelle des Kunden
            v.Kunde != null ? v.Kunde.Firmenname : v.Partner!.Name,
            v.Kunde != null ? v.Kunde.Kundennummer : "Partner",
            v.Status, v.Beginn, v.MindestlaufzeitMonate, v.VerlaengerungMonate,
            v.KuendigungsfristMonate, v.GekuendigtZum,
            v.Bestandteile.Where(b => b.Kurzinfo != null).Select(b => b.Kurzinfo!).ToList(),
            v.Bestandteile
                .Select(b => new BestandteilZeile(
                    b.Id, b.Typ, b.UnterzeichnetAm ?? b.Dokumentdatum, b.Folge, b.Titel, b.Vertraulich, b.GehoertZuId))
                .ToList());

    private static DateOnly? Ende(VertragZeile v, DateOnly heute) => new Vertrag
    {
        Vertragsnummer = v.Vertragsnummer,
        Beginn = v.Beginn,
        MindestlaufzeitMonate = v.MindestlaufzeitMonate,
        VerlaengerungMonate = v.VerlaengerungMonate,
        KuendigungsfristMonate = v.KuendigungsfristMonate,
        GekuendigtZum = v.GekuendigtZum,
    }.NaechstesVertragsende(heute);

    private static List<BestandteilRang> Raenge(IEnumerable<BestandteilZeile> teile) =>
        teile.Select(t => new BestandteilRang(t.Id, t.Typ, t.Stand, t.Folge, t.GehoertZuId)).ToList();

    /// <summary>
    /// Dokumente einer Akte für Listen — gleiche Reihenfolge wie in der Vertragsansicht
    /// (nach Dokumentart, innerhalb neuestes oben, Anlagen unter ihrem Hauptdokument).
    /// Vertrauliche Titel verlassen den Server nur mit der Rolle „Vertrauliche Verträge".
    /// </summary>
    private static List<VertragDokumentKurz> DokumenteKurz(List<BestandteilZeile> teile, bool darfVertrauliche)
    {
        var raenge = Raenge(teile);
        var nummern = LaufendeNummern(raenge);
        var nachId = teile.ToDictionary(t => t.Id);
        return Anzeigereihenfolge(raenge)
            .Select(r =>
            {
                var teil = nachId[r.Id];
                return new VertragDokumentKurz(
                    Dienste.Dokumentarten.Kuerzel(r.Typ),
                    Dienste.Dokumentarten.Name(r.Typ) + (nummern.TryGetValue(r.Id, out var n) ? $" {n}" : ""),
                    teil.Vertraulich && !darfVertrauliche ? "" : teil.Titel,
                    r.Stand, teil.Vertraulich,
                    r.GehoertZuId is { } haupt && nachId.ContainsKey(haupt));
            })
            .ToList();
    }

    private static VertragAnhangEintrag NachAnhang(VertragZeile a, bool darfVertrauliche) => new(
        a.Id, a.Vertragsnummer, a.Bezeichnung,
        BestandteilZusammenfassung(a.Teile.Select(t => t.Typ).ToList()), AktuellerStand(Raenge(a.Teile)),
        a.Kurzinfos, DokumenteKurz(a.Teile, darfVertrauliche));

    private static VertragVerweis NachVerweis(VertragZeile v, DateOnly heute) => new(
        v.Id, v.Vertragsnummer, v.Bezeichnung, StatusText(v.Status), Ende(v, heute),
        v.HauptvertragId is not null, v.HatAnhaenge);

    /// <summary>
    /// Kennzahlen für die Arbeitsliste (KPI-Zeile). Anhänge zählen nicht extra —
    /// Status und Laufzeit gelten vom Hauptvertrag, gezählt wird wie in der Liste je Gruppe.
    /// </summary>
    [HttpGet("kennzahlen")]
    public async Task<ActionResult<VertragsKennzahlen>> Kennzahlen(CancellationToken ct)
    {
        var roh = await db.Vertraege.AsNoTracking()
            .Where(v => v.HauptvertragId == null)
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

    private record BestandteilRang(int Id, BestandteilTyp Typ, DateOnly? Stand, int Folge, int? GehoertZuId = null);

    /// <summary>
    /// Reihenfolge der Dokumente in Vertragsansicht und Listen: gruppiert nach Dokumentart
    /// (Hauptverträge zuerst), innerhalb neuestes oben. Anlagen (11/2026) stehen direkt
    /// unter ihrem Hauptdokument — dort in zeitlicher Folge (Anlage 1, 2, 3 …), unabhängig
    /// von ihrer eigenen Dokumentart.
    /// </summary>
    private static List<BestandteilRang> Anzeigereihenfolge(List<BestandteilRang> teile)
    {
        var vorhanden = teile.Select(t => t.Id).ToHashSet();
        bool IstAnlage(BestandteilRang t) => t.GehoertZuId is { } haupt && vorhanden.Contains(haupt);
        var anlagenVon = teile.Where(IstAnlage).ToLookup(t => t.GehoertZuId!.Value);
        return teile.Where(t => !IstAnlage(t))
            .GroupBy(t => Dienste.Dokumentarten.Rang(t.Typ))
            .OrderBy(g => g.Key)
            .SelectMany(g => Chronologisch(g).AsEnumerable().Reverse())
            .SelectMany(t => Chronologisch(anlagenVon[t.Id]).Prepend(t))
            .ToList();
    }

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

        // Übrige Verträge derselben Vertragspartei: Hauptvertrag, Anhänge und Auswahl fürs Zuordnen
        var partei = vertrag.KundeId is { } kundeId
            ? db.Vertraege.Where(v => v.KundeId == kundeId)
            : db.Vertraege.Where(v => v.PartnerId == vertrag.PartnerId);
        var andere = await partei.AsNoTracking().Where(v => v.Id != id)
            .OrderBy(v => v.Vertragsnummer).Select(AlsZeile).ToListAsync(ct);

        return Ok(NachDetail(vertrag, await Einstellung(EinstellungsSchluessel.VertragsablageVerzeichnis, ct),
            await DarfVertraulicheAsync(ct), andere));
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

        // Nummer, Vertrag und Verlauf in EINER Transaktion (SICHERHEIT.md N3): bricht es
        // nach der Nummernvergabe ab, wird auch die Nummer nicht verbraucht.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
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
        await tx.CommitAsync(ct);

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

        // Anhang: Status, Laufzeit und Kündigung gelten vom Hauptvertrag — hier ist nur die
        // Notiz pflegbar. Die eigenen Werte bleiben unverändert für den Fall, dass gelöst wird.
        if (vertrag.HauptvertragId is not null)
        {
            vertrag.Notiz = aenderung.Notiz;
            await db.SaveChangesAsync(ct);
            return NoContent();
        }

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

    /// <summary>
    /// Vertrag umbenennen (11/2026). Die Bezeichnung ist reine Handpflege — Bestandsscan
    /// und Odoo-Import schreiben sie nie. Leer entfernt die Bezeichnung.
    /// </summary>
    [HttpPut("{id:int}/bezeichnung")]
    public async Task<IActionResult> Umbenennen(int id, VertragUmbenennen eingabe, CancellationToken ct)
    {
        var vertrag = await db.Vertraege.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (vertrag is null)
            return NotFound();
        if (await GruppeGesperrtAsync(ct, id))
            return KeinZugriffVertraulicherVertrag();

        var neu = VertragsGruppe.BezeichnungBereinigen(eingabe.Bezeichnung);
        if (neu == vertrag.Bezeichnung)
            return NoContent();
        vertrag.Bezeichnung = neu;
        await VerlaufAsync(vertrag.KundeId,
            neu is null
                ? $"Vertrag {vertrag.Vertragsnummer}: Bezeichnung entfernt"
                : $"Vertrag {vertrag.Vertragsnummer} umbenannt: „{neu}“",
            null, vertrag.Id, ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Zusammenfassen (11/2026): Vertrag einem Hauptvertrag derselben Vertragspartei als
    /// Anhang zuordnen — oder mit <c>null</c> wieder lösen. Beide Datensätze bleiben
    /// bestehen, Dokumente werden nicht angefasst. Regeln: <see cref="VertragsGruppe"/>.
    /// </summary>
    [HttpPut("{id:int}/hauptvertrag")]
    public async Task<IActionResult> HauptvertragSetzen(int id, VertragZuordnen eingabe, CancellationToken ct)
    {
        var vertrag = await db.Vertraege.Include(v => v.Hauptvertrag).FirstOrDefaultAsync(v => v.Id == id, ct);
        if (vertrag is null)
            return NotFound();
        if (eingabe.HauptvertragId == vertrag.HauptvertragId)
            return NoContent();

        if (eingabe.HauptvertragId is not { } hauptId)
        {
            // Lösen: der Vertrag steht wieder für sich, mit seinen eigenen gespeicherten Werten
            if (await GruppeGesperrtAsync(ct, id, vertrag.HauptvertragId!.Value))
                return KeinZugriffVertraulicherVertrag();
            await VerlaufAsync(vertrag.KundeId,
                $"Vertrag {vertrag.Vertragsnummer} vom Hauptvertrag {vertrag.Hauptvertrag!.Vertragsnummer} gelöst",
                null, vertrag.Id, ct);
            vertrag.HauptvertragId = null;
            await db.SaveChangesAsync(ct);
            return NoContent();
        }

        var haupt = await db.Vertraege.FirstOrDefaultAsync(v => v.Id == hauptId, ct);
        if (haupt is null)
            return BadRequest("Der angegebene Hauptvertrag existiert nicht.");
        var hatAnhaenge = await db.Vertraege.AnyAsync(v => v.HauptvertragId == id, ct);
        if (VertragsGruppe.ZuordnungsFehler(vertrag, haupt, hatAnhaenge) is { } fehler)
            return BadRequest(fehler);
        // Auch ein bisheriger Hauptvertrag (Umhängen) zählt für die Vertraulichkeitsprüfung
        var beteiligte = vertrag.HauptvertragId is { } bisher ? new[] { id, hauptId, bisher } : [id, hauptId];
        if (await GruppeGesperrtAsync(ct, beteiligte))
            return KeinZugriffVertraulicherVertrag();

        vertrag.HauptvertragId = haupt.Id;
        await VerlaufAsync(vertrag.KundeId,
            $"Vertrag {vertrag.Vertragsnummer} als Anhang dem Vertrag {haupt.Vertragsnummer} zugeordnet",
            "Status, Laufzeit und Kündigung gelten ab jetzt vom Hauptvertrag.", vertrag.Id, ct);
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
    /// Dokumente innerhalb einer Akte gruppieren (11/2026): Bestandteil einem anderen
    /// derselben Akte als Anlage zuordnen — oder mit <c>null</c> wieder lösen. Es ändert
    /// sich nur die Anzeige; Dokumentart, Daten und Dokumentversionen bleiben unberührt.
    /// Regeln: <see cref="VertragsGruppe.AnlageFehler"/>.
    /// </summary>
    [HttpPut("bestandteile/{bestandteilId:int}/gehoert-zu")]
    public async Task<IActionResult> BestandteilZuordnen(
        int bestandteilId, BestandteilZuordnen eingabe, CancellationToken ct)
    {
        var bestandteil = await db.Vertragsbestandteile
            .Include(b => b.Vertrag).Include(b => b.GehoertZu)
            .FirstOrDefaultAsync(b => b.Id == bestandteilId, ct);
        if (bestandteil is null)
            return NotFound();
        if (eingabe.GehoertZuId == bestandteil.GehoertZuId)
            return NoContent();
        var darfVertrauliche = await DarfVertraulicheAsync(ct);
        // Vertrauliches umhängen (auch: an oder von Vertraulichem) nur mit Berechtigung
        if (!darfVertrauliche && (bestandteil.Vertraulich || bestandteil.GehoertZu is { Vertraulich: true }))
            return KeinZugriffVertraulich();

        if (eingabe.GehoertZuId is not { } hauptId)
        {
            await VerlaufAsync(bestandteil.Vertrag!.KundeId,
                $"Vertrag {bestandteil.Vertrag.Vertragsnummer}: {VerlaufsName(bestandteil)} ist keine Anlage mehr zu {VerlaufsName(bestandteil.GehoertZu!)}",
                null, bestandteil.VertragId, ct);
            bestandteil.GehoertZuId = null;
            await db.SaveChangesAsync(ct);
            return NoContent();
        }

        var haupt = await db.Vertragsbestandteile.FirstOrDefaultAsync(b => b.Id == hauptId, ct);
        if (haupt is null)
            return BadRequest("Das angegebene Hauptdokument existiert nicht.");
        if (haupt.Vertraulich && !darfVertrauliche)
            return KeinZugriffVertraulich();
        var hatAnlagen = await db.Vertragsbestandteile.AnyAsync(b => b.GehoertZuId == bestandteilId, ct);
        if (VertragsGruppe.AnlageFehler(bestandteil, haupt, hatAnlagen) is { } fehler)
            return BadRequest(fehler);

        bestandteil.GehoertZuId = haupt.Id;
        await VerlaufAsync(bestandteil.Vertrag!.KundeId,
            // Den Verlauf sehen alle — vertrauliche Titel gehören nicht hinein
            $"Vertrag {bestandteil.Vertrag.Vertragsnummer}: {VerlaufsName(bestandteil)} als Anlage zu {VerlaufsName(haupt)} zugeordnet",
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

        // Anhänge dieses Vertrags lösen — sie bleiben erhalten und stehen wieder für sich
        var anhaenge = await db.Vertraege.Where(v => v.HauptvertragId == id).ToListAsync(ct);
        foreach (var anhang in anhaenge)
        {
            anhang.HauptvertragId = null;
            await VerlaufAsync(anhang.KundeId,
                $"Vertrag {anhang.Vertragsnummer} vom Hauptvertrag {vertrag.Vertragsnummer} gelöst — Hauptvertrag gelöscht",
                null, anhang.Id, ct);
        }

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
                Dienste.Dokumentarten.Kuerzel(v.Vorlage.Typ), v.Gueltig.GueltigAb))
            .ToList());
    }

    /// <summary>
    /// Vorschau zur Vertragserzeugung (Fenster „Vertrag aus Vorlage", Schritt 3):
    /// zeigt die Werte, die eingesetzt würden, Warnungen und nicht gefüllte
    /// Platzhalter — ohne etwas abzulegen oder zu registrieren.
    /// </summary>
    [HttpPost("aus-vorlage/vorschau")]
    public async Task<ActionResult<VorlagenVorschau>> AusVorlageVorschau(VertragAusVorlage anlage, CancellationToken ct)
    {
        var (vorbereitung, fehler) = await VorbereitenAsync(anlage, ct);
        if (fehler is not null)
            return fehler;
        var v = vorbereitung!;

        using var speicher = new MemoryStream();
        await using (var quelle = System.IO.File.OpenRead(v.VorlagenPfad))
            await quelle.CopyToAsync(speicher, ct);
        var offene = Dienste.VorlagenDienst.Fuellen(speicher, v.Werte);

        var hauptsitz = v.Kunde.Adressen.FirstOrDefault(a => a.Typ == AdressTyp.Hauptsitz && a.Aktiv);
        var werte = new List<VorschauWert>
        {
            new("Vorlage", v.VorlagenStand),
            new("Firma", v.Kunde.Firmenname),
            new("Kundennummer", v.Kunde.Kundennummer),
            new("Anschrift", hauptsitz is null ? "—" : $"{hauptsitz.Strasse}, {hauptsitz.Plz} {hauptsitz.Ort}".Trim(' ', ',')),
            new("Angebot", v.AngebotsNummer ?? "—"),
            new("Ansprechpartner", v.Ansprechpartner?.Anzeigename() ?? "—"),
            new("Briefanrede", v.Werte["{{Briefanrede}}"]),
            new("Datum", v.Werte["{{Datum}}"]),
        };
        return Ok(new VorlagenVorschau(werte, v.Warnungen, offene));
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
        var (vorbereitung, fehler) = await VorbereitenAsync(anlage, ct);
        if (fehler is not null)
            return fehler;
        var v = vorbereitung!;
        var kunde = v.Kunde;

        // Vorlage füllen
        using var speicher = new MemoryStream();
        await using (var quelle = System.IO.File.OpenRead(v.VorlagenPfad))
            await quelle.CopyToAsync(speicher, ct);
        var offene = Dienste.VorlagenDienst.Fuellen(speicher, v.Werte);
        if (v.AngebotsNummer is null && anlage.AboId != 0)
            offene.Add("Angebotsnummer (kein Angebot/Abo beim Kunden)");

        // Nummer, Datenbank und Dateien gehören zusammen (SICHERHEIT.md N3): Die Transaktion
        // umfasst Nummernvergabe und Vertragsakte; die Dateien werden erst geschrieben, wenn
        // die Datenbank sie annimmt, und bei einem Fehler vor dem Commit wieder entfernt.
        // So bleiben weder verwaiste PDFs noch verbrauchte Vertragsnummern zurück.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Vertragsnummer vorab — sie steht in den PDF-Metadaten/der Signatur
        var nummer = await nummernkreis.NaechsteNummerAsync("Vertrag", ct);

        // Word → PDF (mit Metadaten, optional digital signiert — Stufe 1b)
        byte[] pdfBytes;
        try
        {
            pdfBytes = await pdfDienst.DocxZuPdfAsync(
                speicher.ToArray(), nummer, v.VorlagenStand, kunde.Firmenname, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        // Ablegen — Dateiname nach dem gewachsenen Ablage-Prinzip: <KundenNr>.pdf,
        // bei Bestehen <KundenNr>-1.pdf usw.; die gefüllte .docx liegt als
        // bearbeitbare Quelle gleichnamig daneben (registriert wird das PDF).
        // Kundennummer kommt aus Odoo (ref) — vor dem Pfad bereinigen (SICHERHEIT.md N5)
        var dateiBasis = Dateinamen.Sicher(kunde.Kundennummer, ersatz: $"kunde-{kunde.Id}");
        var zielPfad = Path.Combine(v.ZielVoll, dateiBasis + ".pdf");
        for (var n = 1; System.IO.File.Exists(zielPfad)
             || System.IO.File.Exists(Path.ChangeExtension(zielPfad, ".docx")); n++)
            zielPfad = Path.Combine(v.ZielVoll, $"{dateiBasis}-{n}.pdf");
        var docxPfad = Path.ChangeExtension(zielPfad, ".docx");
        var pruefsumme = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pdfBytes)).ToLowerInvariant();

        // Vertragsakte (Entwurf) + Bestandteil + Dokumentversion
        var vertrag = new Vertrag
        {
            Vertragsnummer = nummer,
            KundeId = kunde.Id,
            // Neue Akte: Vorlagenname als Bezeichnung vorbelegen — danach frei änderbar
            Bezeichnung = VertragsGruppe.BezeichnungBereinigen(v.Version.Vorlage!.Name),
            Status = VertragsStatus.Entwurf,
            Beginn = DateOnly.FromDateTime(DateTime.Today),
            Notiz = $"Aus Vorlage „{v.VorlagenStand}“ erstellt"
                + (v.AngebotsNummer is null ? "" : $" zu Angebot {v.AngebotsNummer}")
                + " — nach Unterschrift Status und Laufzeit pflegen.",
        };
        vertrag.Bestandteile.Add(new Vertragsbestandteil
        {
            Typ = v.Version.Vorlage!.Typ,
            Titel = v.Version.Vorlage.Name,
            Vertrag = vertrag,
            Dokumentdatum = DateOnly.FromDateTime(DateTime.Today),
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
                    ErstelltVonId = v.MitarbeiterId,
                    // Vorlagen-Stand am Dokument: welcher Kunde hat welche Version bekommen
                    VorlagenVersionId = v.Version.Id,
                },
            },
        });
        db.Vertraege.Add(vertrag);
        await db.SaveChangesAsync(ct);

        await VerlaufAsync(kunde.Id,
            $"Vertrag {nummer} aus Vorlage „{v.VorlagenStand}“ erstellt", null, vertrag.Id, ct);
        await db.SaveChangesAsync(ct);

        try
        {
            await System.IO.File.WriteAllBytesAsync(zielPfad, pdfBytes, ct);
            await System.IO.File.WriteAllBytesAsync(docxPfad, speicher.ToArray(), ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            // Datenbank rollt beim Dispose der Transaktion zurück — die Dateien müssen wir selbst entfernen
            foreach (var pfad in new[] { zielPfad, docxPfad })
                try { if (System.IO.File.Exists(pfad)) System.IO.File.Delete(pfad); } catch (IOException) { }
            throw;
        }

        return Ok(new VorlagenErgebnis(vertrag.Id, nummer, Path.GetFileName(zielPfad), offene));
    }

    private sealed record Vorbereitung(
        VorlagenVersion Version, string VorlagenStand, Kunde Kunde, string VorlagenPfad, string ZielVoll,
        int MitarbeiterId, string? AngebotsNummer, Ansprechpartner? Ansprechpartner,
        Dictionary<string, string> Werte, List<string> Warnungen);

    /// <summary>
    /// Gemeinsame Prüfungen und Werte für Vorschau und Erzeugung — so zeigt die
    /// Vorschau garantiert dasselbe, was später im Vertrag steht.
    /// </summary>
    private async Task<(Vorbereitung? Vorbereitung, ActionResult? Fehler)> VorbereitenAsync(
        VertragAusVorlage anlage, CancellationToken ct)
    {
        var version = await db.VorlagenVersionen.AsNoTracking()
            .Include(v => v.Vorlage)
            .FirstOrDefaultAsync(v => v.Id == anlage.VorlagenVersionId, ct);
        if (version?.Vorlage is null)
            return (null, BadRequest("Die angegebene Vorlagen-Version existiert nicht."));
        var vorlagenStand = $"{version.Vorlage.Name} v{version.Nummer}";
        // Serverseitig durchsetzen, was die Auswahl in der Akte anbietet
        if (!version.Vorlage.Aktiv)
            return (null, BadRequest($"Die Vorlage „{version.Vorlage.Name}“ ist zurückgezogen."));
        if (!version.Vorlage.ErlaubtFuer(await AktuelleRollen(ct)))
            return (null, StatusCode(StatusCodes.Status403Forbidden,
                $"Die Vorlage „{version.Vorlage.Name}“ ist für Ihre Rolle nicht freigegeben."));
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var gueltigeId = await db.VorlagenVersionen
            .Where(v => v.VorlageId == version.VorlageId && v.GueltigAb <= heute)
            .OrderByDescending(v => v.Nummer).Select(v => (int?)v.Id).FirstOrDefaultAsync(ct);
        if (gueltigeId != version.Id)
            return (null, BadRequest($"„{vorlagenStand}“ ist nicht der aktuell gültige Stand — bitte die Seite neu laden."));

        var kunde = await db.Kunden
            .Include(k => k.Adressen)
            .Include(k => k.Ansprechpartner)
            .FirstOrDefaultAsync(k => k.Id == anlage.KundeId, ct);
        if (kunde is null)
            return (null, BadRequest("Der angegebene Kunde existiert nicht."));

        var vorlagenVerzeichnis = await Einstellung(EinstellungsSchluessel.VorlagenVerzeichnis, ct);
        var zielVerzeichnis = await Einstellung(EinstellungsSchluessel.VertragserzeugungZielverzeichnis, ct);
        var basis = await Einstellung(EinstellungsSchluessel.VertragsablageVerzeichnis, ct);
        if (string.IsNullOrWhiteSpace(vorlagenVerzeichnis) || string.IsNullOrWhiteSpace(zielVerzeichnis)
            || string.IsNullOrWhiteSpace(basis))
            return (null, BadRequest("Vorlagen-, Ziel- oder Ablageverzeichnis ist nicht konfiguriert (Administration)."));

        var vorlagenPfad = Path.Combine(vorlagenVerzeichnis, version.Dateipfad);
        if (!System.IO.File.Exists(vorlagenPfad))
            return (null, BadRequest($"Die eingefrorene Versionsdatei fehlt in der Ablage: {version.Dateipfad}"));

        // Zielverzeichnis muss in der Vertragsablage liegen (Viewer/Registrierung)
        var basisVoll = Path.GetFullPath(basis).TrimEnd(Path.DirectorySeparatorChar);
        var zielVoll = Path.GetFullPath(zielVerzeichnis);
        if (!zielVoll.StartsWith(basisVoll + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !zielVoll.Equals(basisVoll, StringComparison.OrdinalIgnoreCase))
            return (null, BadRequest("Das Zielverzeichnis liegt außerhalb des Vertragsablage-Verzeichnisses."));
        if (!Directory.Exists(zielVoll))
            return (null, BadRequest($"Zielverzeichnis nicht erreichbar: {zielVerzeichnis}"));

        var mitarbeiterId = await AktuellerMitarbeiterId(ct);
        if (mitarbeiterId is null)
            return (null, BadRequest("Für den angemeldeten Benutzer existiert kein Mitarbeiter-Datensatz."));

        // Angebot für „Angebot S0…": gewähltes, sonst neuestes offenes Angebot
        // (Odoo-Abo im Entwurf), sonst laufendes Abo; 0 = bewusst ohne
        var angebote = db.Abos.Where(a => a.KundeId == kunde.Id);
        string? angebotsNummer;
        if (anlage.AboId == 0)
            angebotsNummer = null;
        else if (anlage.AboId is { } aboId)
        {
            angebotsNummer = await angebote.Where(a => a.Id == aboId).Select(a => a.Nummer).FirstOrDefaultAsync(ct);
            if (angebotsNummer is null)
                return (null, BadRequest("Das gewählte Angebot gehört nicht zu diesem Kunden."));
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

        // Ansprechpartner für Anrede/Name: gewählter, sonst Hauptansprechpartner
        Ansprechpartner? ansprechpartner;
        if (anlage.AnsprechpartnerId is { } apId)
        {
            ansprechpartner = kunde.Ansprechpartner.FirstOrDefault(a => a.Id == apId && a.Aktiv);
            if (ansprechpartner is null)
                return (null, BadRequest("Der gewählte Ansprechpartner gehört nicht zu diesem Kunden."));
        }
        else
        {
            ansprechpartner = kunde.Ansprechpartner
                .Where(a => a.Aktiv)
                .OrderByDescending(a => a.Rollen.HasFlag(AnsprechpartnerRolle.Hauptansprechpartner))
                .ThenByDescending(a => a.Rollen != AnsprechpartnerRolle.Keine)
                .FirstOrDefault();
        }

        var warnungen = new List<string>();
        if (!kunde.Adressen.Any(a => a.Typ == AdressTyp.Hauptsitz && a.Aktiv))
            warnungen.Add("Keine Hauptsitz-Adresse gepflegt — die Anschrift bleibt im Vertrag leer.");
        if (angebotsNummer is null && anlage.AboId != 0)
            warnungen.Add("Kein Angebot und kein laufendes Abo — im Vertrag steht „S0____“.");
        if (ansprechpartner is null)
            warnungen.Add("Kein Ansprechpartner — Anrede „Sehr geehrte Damen und Herren“.");

        return (new Vorbereitung(version, vorlagenStand, kunde, vorlagenPfad, zielVoll, mitarbeiterId.Value,
            angebotsNummer, ansprechpartner, PlatzhalterWerte(kunde, angebotsNummer, ansprechpartner), warnungen), null);
    }

    /// <summary>Alle Platzhalter, die die Vertragserzeugung kennt — für die Prüfung von Vorlagen vor der Freigabe.</summary>
    internal static IReadOnlyCollection<string> BekanntePlatzhalter { get; } =
        PlatzhalterWerte(new Kunde { Kundennummer = "", Firmenname = "" }, null, null)
            .Keys.Where(k => k.StartsWith("{{")).ToList();

    private static Dictionary<string, string> PlatzhalterWerte(Kunde kunde, string? aboNummer, Ansprechpartner? ansprechpartner)
    {
        var hauptsitz = kunde.Adressen.FirstOrDefault(a => a.Typ == AdressTyp.Hauptsitz && a.Aktiv);
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
        contentType ??= "application/octet-stream";

        var stream = System.IO.File.OpenRead(dokument.Dateipfad);
        // Nur PDF inline (Vorschau im Browser); alles andere als Download, damit eine im
        // Archiv abgelegte HTML-/SVG-Datei nie im Kontext der Anwendung ausgeführt wird (SICHERHEIT.md V5)
        return contentType == "application/pdf"
            ? File(stream, contentType, enableRangeProcessing: true)
            : File(stream, contentType, dokument.DateinameOriginal, enableRangeProcessing: true);
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

    private Task<int?> AktuellerMitarbeiterId(CancellationToken ct) => angemeldet.IdAsync(ct);

    private Task<MitarbeiterRolle> AktuelleRollen(CancellationToken ct) => angemeldet.RollenAsync(ct);

    // --- Vertraulichkeit (FEEDBACK Nr. 20) -------------------------------------

    /// <summary>Rolle „Vertrauliche Verträge" — bewusst ohne Admin-Automatik.</summary>
    private async Task<bool> DarfVertraulicheAsync(CancellationToken ct) =>
        (await AktuelleRollen(ct)).HasFlag(MitarbeiterRolle.VertraulicheVertraege);

    private ObjectResult KeinZugriffVertraulich() => StatusCode(StatusCodes.Status403Forbidden,
        "Vertraulicher Bestandteil — nur mit der Rolle „Vertrauliche Verträge“ (Administration → Benutzer & Rollen).");

    /// <summary>
    /// Umbenennen und Zuordnen: Ist einer der beteiligten Verträge vertraulich
    /// (<see cref="VertragsGruppe.IstVertraulich"/>), braucht es die Rolle „Vertrauliche Verträge".
    /// </summary>
    private async Task<bool> GruppeGesperrtAsync(CancellationToken ct, params int[] vertragIds)
    {
        if (await DarfVertraulicheAsync(ct))
            return false;
        var teile = await db.Vertragsbestandteile.AsNoTracking()
            .Where(b => vertragIds.Contains(b.VertragId))
            .Select(b => new { b.VertragId, b.Vertraulich })
            .ToListAsync(ct);
        return teile.GroupBy(b => b.VertragId)
            .Any(g => VertragsGruppe.IstVertraulich(g.Select(b => b.Vertraulich)));
    }

    private ObjectResult KeinZugriffVertraulicherVertrag() => StatusCode(StatusCodes.Status403Forbidden,
        "Vertraulicher Vertrag — Umbenennen und Zuordnen nur mit der Rolle „Vertrauliche Verträge“ " +
        "(Administration → Benutzer & Rollen).");

    /// <summary>Name für den für alle sichtbaren Verlauf: bei vertraulichen nur die Dokumentart.</summary>
    private static string VerlaufsName(Vertragsbestandteil b) =>
        b.Vertraulich ? $"vertrauliche(r) {BestandteilText(b.Typ)}" : b.Titel;

    private static string? Kuerzen(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim() is { Length: > 300 } lang ? lang[..300] : text.Trim();

    private static bool LiegtUnter(string pfad, string basis) =>
        pfad.StartsWith(basis.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private static VertragDetail NachDetail(
        Vertrag vertrag, string? ablage = null, bool darfVertrauliche = false, List<VertragZeile>? andere = null)
    {
        andere ??= [];
        var raenge = vertrag.Bestandteile
            .Select(b => new BestandteilRang(b.Id, b.Typ, b.Stand, b.Folge, b.GehoertZuId)).ToList();
        var nummern = LaufendeNummern(raenge);
        var stand = AktuellerStand(raenge);
        var juengsterId = Chronologisch(raenge.Where(r => r.Stand is not null)).LastOrDefault()?.Id;
        var reihenfolge = Anzeigereihenfolge(raenge)
            .Select((r, i) => (r.Id, Position: i))
            .ToDictionary(x => x.Id, x => x.Position);

        return NachDetail(vertrag, ablage, nummern, stand, juengsterId, reihenfolge, darfVertrauliche, andere);
    }

    private static VertragDetail NachDetail(
        Vertrag vertrag, string? ablage, Dictionary<int, int> nummern, StandEintrag? stand,
        int? juengsterId, Dictionary<int, int> reihenfolge, bool darfVertrauliche, List<VertragZeile> andere) => new(
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
        vertrag.PartnerId,
        vertrag.Bezeichnung,
        andere.Where(a => a.Id == vertrag.HauptvertragId)
            .Select(a => NachVerweis(a, DateOnly.FromDateTime(DateTime.Today))).FirstOrDefault(),
        andere.Where(a => a.HauptvertragId == vertrag.Id)
            .Select(a => NachAnhang(a, darfVertrauliche)).ToList(),
        andere.Select(a => NachVerweis(a, DateOnly.FromDateTime(DateTime.Today))).ToList(),
        !darfVertrauliche && VertragsGruppe.IstVertraulich(vertrag.Bestandteile.Select(b => b.Vertraulich)));

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
                    .ToList(),
            b.GehoertZuId);
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
