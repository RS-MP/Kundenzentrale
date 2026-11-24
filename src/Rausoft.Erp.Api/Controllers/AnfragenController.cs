using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Customers;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Anfragen aus Website-Formularen: Ein Mitarbeiter fügt den Text der
/// Benachrichtigungsmail ein, die Kundenzentrale liest ihn aus und zeigt einen
/// Vorschlag mit möglichen Dubletten (Entscheidung MP 15.11.2026: Einfügen von Hand
/// statt Postfachüberwachung — der Mitarbeiter hat die Mail vorher gesehen).
/// Gespeichert wird die Anfrage mit ihrer Herkunft (Entscheidung MP 17.11.2026), nicht
/// der Mailtext. Das Anlegen einer gespeicherten Anfrage in Odoo (Stufe 2) steht in
/// <c>AnfragenController.Odoo.cs</c>.
/// </summary>
[ApiController]
[Route("api/anfragen")]
public partial class AnfragenController(
    ErpDbContext db, Dienste.AngemeldeterMitarbeiter angemeldet, Dienste.OdooSchreibZugang zugang,
    ILogger<AnfragenController> log) : ControllerBase
{
    private const int MaxDubletten = 10;
    private const int MaxEintraege = 500;
    /// <summary>Spaltenlänge von <see cref="Einstellung.Wert"/>.</summary>
    private const int MaxAusschlussLaenge = 1000;

    private Task<bool> DarfAsync(CancellationToken ct) =>
        angemeldet.HatRolleAsync(MitarbeiterRolle.Vertrieb | MitarbeiterRolle.Admin, ct);

    [HttpPost("auslesen")]
    [RequestSizeLimit(200_000)]
    public async Task<ActionResult<AnfrageVorschlag>> Auslesen(AnfrageAuslesen daten, CancellationToken ct)
    {
        if (!await DarfAsync(ct))
            return Forbid();
        var (anfrage, fehler) = WebsiteAnfrageLeser.Lesen(daten.Text);
        if (anfrage is null)
            return BadRequest(fehler);

        var entwurf = AnfrageHerkunft.Uebernehmen(anfrage, DateTime.UtcNow);
        return new AnfrageVorschlag(
            anfrage.Formular, anfrage.Website, anfrage.Firma, anfrage.Vorname, anfrage.Nachname,
            anfrage.EMail, anfrage.Telefon, anfrage.Nachricht, anfrage.DatenschutzBestaetigt, anfrage.Eingang,
            anfrage.Herkunft.Select(h => new AnfrageHerkunftsFeld(h.Name, h.Wert)).ToList(),
            anfrage.Hinweise.ToList(),
            await DublettenAsync(anfrage.Firma, anfrage.EMail, ct),
            AnfrageHerkunft.Text(entwurf, await KampagnenNamenAsync(ct)),
            anfrage.Eingang is not null && await GleicheAsync(entwurf).AnyAsync(ct));
    }

    /// <summary>
    /// Speichert die Anfrage. Der Text wird hier noch einmal ausgelesen — gespeichert wird
    /// nur, was der Server selbst gelesen hat, nicht was der Browser zurückschickt.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(200_000)]
    public async Task<ActionResult<AnfrageEintrag>> Speichern(AnfrageSpeichern daten, CancellationToken ct)
    {
        if (!await DarfAsync(ct))
            return Forbid();
        var (gelesen, fehler) = WebsiteAnfrageLeser.Lesen(daten.Text);
        if (gelesen is null)
            return BadRequest(fehler);

        var anfrage = AnfrageHerkunft.Uebernehmen(gelesen, DateTime.UtcNow);
        if (gelesen.Eingang is not null && await GleicheAsync(anfrage).AnyAsync(ct))
            return Conflict("Diese Anfrage ist bereits gespeichert.");
        if (daten.KundeId is { } kundeId && !await db.Kunden.AnyAsync(k => k.Id == kundeId, ct))
            return BadRequest("Der gewählte Kunde existiert nicht mehr.");

        anfrage.ErfasstVonId = await angemeldet.IdAsync(ct);
        anfrage.KundeId = daten.KundeId;
        var namen = await KampagnenNamenAsync(ct);

        await using var transaktion = await db.Database.BeginTransactionAsync(ct);
        db.Anfragen.Add(anfrage);
        await db.SaveChangesAsync(ct);
        if (anfrage.KundeId is not null)
        {
            db.Aktivitaeten.Add(AnfrageHerkunft.VerlaufsEintrag(anfrage, namen));
            await db.SaveChangesAsync(ct);
        }
        await transaktion.CommitAsync(ct);

        return (await EintraegeAsync(db.Anfragen.Where(a => a.Id == anfrage.Id), namen, ct)).Single();
    }

    /// <summary>Gespeicherte Anfragen eines Jahres (Standard: laufendes Jahr) mit Zählung je Kanal.</summary>
    [HttpGet]
    public async Task<ActionResult<AnfragenUebersicht>> Liste([FromQuery] int? jahr, CancellationToken ct)
    {
        if (!await DarfAsync(ct))
            return Forbid();
        var heute = Ortszeit.VonUtc(DateTime.UtcNow);
        var gewaehlt = jahr is >= 2000 and <= 2100 ? jahr.Value : heute.Year;
        var von = Ortszeit.NachUtc(new DateTime(gewaehlt, 1, 1));
        var bis = Ortszeit.NachUtc(new DateTime(gewaehlt + 1, 1, 1));

        var grenzen = await db.Anfragen.GroupBy(_ => 1)
            .Select(g => new { Min = g.Min(a => a.EingegangenAm), Max = g.Max(a => a.EingegangenAm) })
            .FirstOrDefaultAsync(ct);
        var jahre = new SortedSet<int> { heute.Year, gewaehlt };
        if (grenzen is not null)
            for (var j = Ortszeit.VonUtc(grenzen.Min).Year; j <= Ortszeit.VonUtc(grenzen.Max).Year; j++)
                jahre.Add(j);

        var imJahr = db.Anfragen.AsNoTracking().Where(a => a.EingegangenAm >= von && a.EingegangenAm < bis);
        var namen = await KampagnenNamenAsync(ct);
        var odooStand = await zugang.StandAsync(ct);
        var eintraege = await EintraegeAsync(imJahr.OrderByDescending(a => a.EingegangenAm).Take(MaxEintraege), namen, ct, odooStand);

        // „Wurde Kunde": der zugeordnete Kunde hat heute ein laufendes Abo oder einen aktiven Vertrag
        var kundenIds = eintraege.Where(e => e.KundeId is not null).Select(e => e.KundeId!.Value).Distinct().ToList();
        var echteKunden = kundenIds.Count == 0
            ? []
            : (await db.Kunden.AsNoTracking().Where(k => kundenIds.Contains(k.Id))
                .Where(KundenController.KundenRegel(db)).Select(k => k.Id).ToListAsync(ct)).ToHashSet();
        bool IstKunde(AnfrageEintrag e) => e.KundeId is { } id && echteKunden.Contains(id);
        // Je Anfrage mitliefern — die Seite filtert die Liste damit nach „heute Kunde“
        eintraege = eintraege.Select(e => e with { HeuteKunde = IstKunde(e) }).ToList();

        return new AnfragenUebersicht(
            gewaehlt, jahre.Reverse().ToList(), eintraege.Count, eintraege.Count(IstKunde),
            eintraege.GroupBy(e => e.Kanal)
                .Select(g => new AnfrageKanalZahl(g.Key, g.Count(), g.Count(IstKunde),
                    g.Count(e => e.Ergebnis == ErgebnisGewonnen), g.Count(e => e.Ergebnis == ErgebnisVerloren)))
                .OrderByDescending(k => k.Anzahl).ThenBy(k => k.Kanal).ToList(),
            eintraege,
            odooStand.Ziel.ToString(), zugang.SchreibSperre(odooStand));
    }

    /// <summary>Mögliche Kunden zu einer gespeicherten Anfrage — für die spätere Zuordnung.</summary>
    [HttpGet("{id:int}/treffer")]
    public async Task<ActionResult<List<AnfrageDublette>>> Treffer(int id, CancellationToken ct)
    {
        if (!await DarfAsync(ct))
            return Forbid();
        var anfrage = await db.Anfragen.AsNoTracking().Where(a => a.Id == id)
            .Select(a => new { a.Firma, a.Email }).FirstOrDefaultAsync(ct);
        return anfrage is null ? NotFound() : await DublettenAsync(anfrage.Firma, anfrage.Email, ct);
    }

    /// <summary>Ordnet die Anfrage einem Kunden zu (oder löst die Zuordnung); der Eintrag im Verlauf der Akte zieht mit.</summary>
    [HttpPut("{id:int}/kunde")]
    public async Task<ActionResult<AnfrageEintrag>> KundeSetzen(int id, AnfrageKundeSetzen daten, CancellationToken ct)
    {
        if (!await DarfAsync(ct))
            return Forbid();
        var anfrage = await db.Anfragen.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (anfrage is null)
            return NotFound();
        if (daten.KundeId is { } kundeId && !await db.Kunden.AnyAsync(k => k.Id == kundeId, ct))
            return BadRequest("Der gewählte Kunde existiert nicht mehr.");

        var namen = await KampagnenNamenAsync(ct);
        if (anfrage.KundeId != daten.KundeId)
        {
            db.Aktivitaeten.RemoveRange(await VerlaufsEintraege(id).ToListAsync(ct));
            anfrage.KundeId = daten.KundeId;
            if (anfrage.KundeId is not null)
                db.Aktivitaeten.Add(AnfrageHerkunft.VerlaufsEintrag(anfrage, namen));
            await db.SaveChangesAsync(ct);
        }
        return (await EintraegeAsync(db.Anfragen.Where(a => a.Id == id), namen, ct)).Single();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Loeschen(int id, CancellationToken ct)
    {
        if (!await DarfAsync(ct))
            return Forbid();
        var anfrage = await db.Anfragen.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (anfrage is null)
            return NotFound();
        // Aus Odoo übernommen oder mit einer Verkaufschance verknüpft: die Nummer merken,
        // sonst legte die nächste Übernahme die Anfrage wieder an
        if (anfrage.OdooLeadId is { } leadId)
        {
            var merker = await db.Einstellungen
                .FirstOrDefaultAsync(e => e.Schluessel == EinstellungsSchluessel.AnfragenOdooUebersprungen, ct);
            if (merker is null)
                db.Einstellungen.Add(merker = new Einstellung { Schluessel = EinstellungsSchluessel.AnfragenOdooUebersprungen });
            var liste = AnfragenAusschluss.Hinzufuegen(merker.Wert, leadId);
            if (liste.Length > MaxAusschlussLaenge)
                return Conflict("Die Liste der nicht zu übernehmenden Verkaufschancen ist voll. " +
                                "Bitte unter Administration → Einstellungen „Anfragen.OdooUebersprungen“ kürzen.");
            merker.Wert = liste;
        }
        db.Aktivitaeten.RemoveRange(await VerlaufsEintraege(id).ToListAsync(ct));
        db.Anfragen.Remove(anfrage);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- Hilfen ----

    private IQueryable<Aktivitaet> VerlaufsEintraege(int anfrageId) =>
        db.Aktivitaeten.Where(a => a.VerweisTyp == AktivitaetsVerweisTyp.Anfrage && a.VerweisId == anfrageId);

    /// <summary>Dieselbe Anfrage noch einmal: gleiches Formular, gleiche Eingangszeit, gleiche E-Mail bzw. Firma.</summary>
    private IQueryable<Anfrage> GleicheAsync(Anfrage a) =>
        db.Anfragen.Where(x => x.Formular == a.Formular && x.EingegangenAm == a.EingegangenAm
                               && x.Email == a.Email && x.Firma == a.Firma);

    private async Task<Dictionary<string, string>> KampagnenNamenAsync(CancellationToken ct) =>
        AnfrageHerkunft.KampagnenNamen(await db.Einstellungen.AsNoTracking()
            .Where(e => e.Schluessel == EinstellungsSchluessel.AnfragenKampagnen)
            .Select(e => e.Wert).FirstOrDefaultAsync(ct));

    private async Task<List<AnfrageEintrag>> EintraegeAsync(
        IQueryable<Anfrage> anfragen, IReadOnlyDictionary<string, string> namen, CancellationToken ct,
        Dienste.OdooSchreibZugang.Stand? odooStand = null)
    {
        var roh = await anfragen.AsNoTracking()
            .Select(a => new
            {
                Anfrage = a,
                Kundenname = a.Kunde != null ? a.Kunde.Firmenname : null,
                Kundennummer = a.Kunde != null ? a.Kunde.Kundennummer : null,
                ErfasstVon = a.ErfasstVon != null ? a.ErfasstVon.Anzeigename : null,
            })
            .ToListAsync(ct);

        // Stand des Anlegens in Odoo — die Nummern gelten nur in der Datenbank, in der sie vergeben wurden
        var stand = odooStand ?? await zugang.StandAsync(ct);
        var kennung = Dienste.OdooSchreibZugang.ZielKennung(stand);
        var schreibenGesperrt = zugang.SchreibSperre(stand) is not null;
        var ids = roh.Select(r => r.Anfrage.Id).ToList();
        var anlagen = (await db.AnfrageOdooAnlagen.AsNoTracking().Where(x => ids.Contains(x.AnfrageId)).ToListAsync(ct))
            .ToLookup(x => x.AnfrageId);
        var jetzt = DateTime.UtcNow;

        return roh.Select(r =>
        {
            var a = r.Anfrage;
            var eigene = anlagen[a.Id].ToList();
            return new AnfrageEintrag(
                a.Id, Ortszeit.VonUtc(a.EingegangenAm), a.Formular, a.Website, a.Firma, a.Kontaktname(),
                a.Email, a.Telefon, a.Nachricht,
                AnfrageHerkunft.Kanal(a), AnfrageHerkunft.Text(a, namen), a.Einstiegsseite, a.LetzteSeite,
                a.ErsterBesuch is { } besuch && besuch <= a.EingegangenAm.AddMinutes(1)
                    ? (int)Math.Max(0, Math.Round((a.EingegangenAm - besuch).TotalMinutes))
                    : null,
                a.HatKlickKennung() ? Ortszeit.VonUtc(a.EingegangenAm).Date.AddDays(AnfrageHerkunft.KlickKennungTage) : null,
                a.KundeId, r.Kundenname, r.Kundennummer, r.ErfasstVon,
                a.OdooLeadId,
                // Der Ausgang kommt aus Odoo — ohne Verkaufschance gibt es dazu keine Aussage
                a.OdooLeadId is null ? null : a.Ergebnis switch
                {
                    AnfrageErgebnis.Gewonnen => ErgebnisGewonnen,
                    AnfrageErgebnis.Verloren => ErgebnisVerloren,
                    _ => "offen",
                },
                a.OdooLeadId is null ? null : a.VerlustGrund,
                OdooAnlageInfo(eigene, stand, kennung),
                schreibenGesperrt || kennung is null ? null : OdooAnlageRegeln.Sperre(a, eigene, kennung, jetzt));
        }).ToList();
    }

    private const string ErgebnisGewonnen = "gewonnen";
    private const string ErgebnisVerloren = "verloren";

    /// <summary>
    /// Mögliche Dubletten im Kundenbestand: gleiche E-Mail-Adresse, gleiche E-Mail-Domäne
    /// (nicht bei Freemailern) und ähnlicher Firmenname. Die Domäne ist die zuverlässigste
    /// Spur — Firmennamen werden im Formular frei geschrieben.
    /// </summary>
    private async Task<List<AnfrageDublette>> DublettenAsync(string? firma, string? email, CancellationToken ct)
    {
        var treffer = new Dictionary<int, (int Punkte, string Grund)>();
        void Merke(int kundeId, int punkte, string grund)
        {
            if (!treffer.TryGetValue(kundeId, out var bisher) || bisher.Punkte < punkte)
                treffer[kundeId] = (punkte, grund);
        }

        var klammer = email?.LastIndexOf('@') ?? -1;
        if (email is not null && klammer > 0 && klammer < email.Length - 1)
        {
            var domaene = email[(klammer + 1)..].ToLowerInvariant();
            var endung = "@" + domaene;
            var adressen = await db.Ansprechpartner.AsNoTracking()
                .Where(a => a.Email != null && a.Email.EndsWith(endung))
                .Select(a => new { a.KundeId, a.Email })
                .ToListAsync(ct);
            foreach (var gruppe in adressen.GroupBy(a => a.KundeId))
            {
                if (gruppe.Any(a => string.Equals(a.Email!.Trim(), email, StringComparison.OrdinalIgnoreCase)))
                    Merke(gruppe.Key, 100, "Ansprechpartner mit dieser E-Mail-Adresse vorhanden");
                else if (!WebsiteAnfrageLeser.IstFreemailer(domaene))
                    Merke(gruppe.Key, 90, gruppe.Count() == 1
                        ? "gleiche E-Mail-Domäne (1 Ansprechpartner)"
                        : $"gleiche E-Mail-Domäne ({gruppe.Count()} Ansprechpartner)");
            }
        }

        // Namensvergleich im Speicher: die Ähnlichkeit lässt sich nicht in SQL ausdrücken,
        // und der Bestand ist klein genug (nur Id und Name)
        if (firma is not null)
        {
            var namen = await db.Kunden.AsNoTracking().Select(k => new { k.Id, k.Firmenname }).ToListAsync(ct);
            foreach (var k in namen)
            {
                var punkte = Firmenname.Aehnlichkeit(firma, k.Firmenname);
                if (punkte > 0)
                    Merke(k.Id, punkte, punkte switch
                    {
                        100 => "gleicher Firmenname",
                        80 => "Firmenname beginnt gleich",
                        _ => "ähnlicher Firmenname",
                    });
            }
        }

        if (treffer.Count == 0)
            return [];
        var ids = treffer.OrderByDescending(t => t.Value.Punkte).Take(MaxDubletten).Select(t => t.Key).ToList();
        var kunden = await db.Kunden.AsNoTracking()
            .Where(k => ids.Contains(k.Id))
            .Select(k => new { k.Id, k.Kundennummer, k.Firmenname, k.ArchiviertAm })
            .ToListAsync(ct);
        return kunden
            .Select(k => new AnfrageDublette(k.Id, k.Kundennummer, k.Firmenname, k.ArchiviertAm is not null,
                treffer[k.Id].Punkte, treffer[k.Id].Grund))
            .OrderByDescending(d => d.Uebereinstimmung).ThenBy(d => d.Firmenname)
            .ToList();
    }
}
