using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Customers;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

[ApiController]
[Route("api/kunden")]
public class KundenController(ErpDbContext db, Dienste.AngemeldeterMitarbeiter angemeldet) : ControllerBase
{
    private const int MaxProSeite = 100;

    [HttpGet]
    public async Task<ActionResult<SeitenErgebnis<KundeListenEintrag>>> Liste(
        [FromQuery] string? suche, [FromQuery] string? status, [FromQuery] string? kategorie,
        [FromQuery] bool mitPartner = false,
        [FromQuery] int seite = 1, [FromQuery] int proSeite = 25,
        CancellationToken ct = default)
    {
        seite = Math.Max(1, seite);
        proSeite = Math.Clamp(proSeite, 1, MaxProSeite);

        var query = db.Kunden.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(suche))
        {
            var s = suche.Trim();
            query = query.Where(k => k.Firmenname.Contains(s) || k.Kundennummer.Contains(s));
        }
        if (status == "Aktiv")
            query = query.Where(k => k.Status == KundenStatus.Aktiv);
        else if (status == "Inaktiv")
            query = query.Where(k => k.Status == KundenStatus.Inaktiv);
        if (mitPartner)
            query = query.Where(k => k.VermitteltDurchPartnerId != null || k.RechnungsempfaengerPartnerId != null);

        // Archiv: Standard-Sichten blenden archivierte Kunden aus; die Archiv-Sicht zeigt nur sie.
        if (kategorie == "archiv")
            query = query.Where(k => k.ArchiviertAm != null);
        else
            query = query.Where(k => k.ArchiviertAm == null);

        // Kategorie ist berechnet, nie gepflegt (Regel V2, 09/2026):
        //  Kunde           = laufendes Abo ODER aktiver Vertrag MIT gepflegter Laufzeit
        //  Ehemaliger Kunde = beendetes/abgelöstes Abo oder gekündigter/beendeter Vertrag, nichts läuft
        //  Einmalbestellung = nur Bestellungen, nie ein Abo
        //  Zu klären        = übrige Artefakte ohne gültige laufende Beziehung (z. B. aktiver
        //                     Vertrag ohne Laufzeit — dahinter stecken oft unerkannte Kündigungen)
        if (kategorie == "kunden")
            query = query.Where(KundenRegel(db));
        // Abo-ENTWÜRFE sind Angebote (Odoo) und zählen bewusst nicht als Artefakt —
        // ein Interessent mit (abgelehntem) Angebot bleibt Interessent.
        else if (kategorie == "einmalbestellung")
            query = query.Where(k =>
                !db.Abos.Any(a => a.KundeId == k.Id && a.Status == Domain.Subscriptions.AboStatus.Laufend)
                && !db.Vertraege.Any(v => v.KundeId == k.Id && v.Status == Domain.Contracts.VertragsStatus.Aktiv && v.MindestlaufzeitMonate != null)
                && !db.Vertraege.Any(v => v.KundeId == k.Id
                    && (v.Status == Domain.Contracts.VertragsStatus.Gekuendigt || v.Status == Domain.Contracts.VertragsStatus.Beendet))
                && db.Vertraege.Any(v => v.KundeId == k.Id)
                && !db.Abos.Any(a => a.KundeId == k.Id && a.Status != Domain.Subscriptions.AboStatus.Entwurf)
                && !db.Vertraege.Any(v => v.KundeId == k.Id
                    && v.Bestandteile.Any(b => b.Typ != Domain.Contracts.BestandteilTyp.Bestellung)));
        else if (kategorie == "zuklaeren")
            query = query.Where(k =>
                !db.Abos.Any(a => a.KundeId == k.Id && a.Status == Domain.Subscriptions.AboStatus.Laufend)
                && !db.Vertraege.Any(v => v.KundeId == k.Id && v.Status == Domain.Contracts.VertragsStatus.Aktiv && v.MindestlaufzeitMonate != null)
                && !db.Abos.Any(a => a.KundeId == k.Id
                    && (a.Status == Domain.Subscriptions.AboStatus.Abgeloest || a.Status == Domain.Subscriptions.AboStatus.Beendet))
                && !db.Vertraege.Any(v => v.KundeId == k.Id
                    && (v.Status == Domain.Contracts.VertragsStatus.Gekuendigt || v.Status == Domain.Contracts.VertragsStatus.Beendet))
                && (db.Abos.Any(a => a.KundeId == k.Id && a.Status != Domain.Subscriptions.AboStatus.Entwurf)
                    || db.Vertraege.Any(v => v.KundeId == k.Id
                        && v.Bestandteile.Any(b => b.Typ != Domain.Contracts.BestandteilTyp.Bestellung))));
        else if (kategorie == "ehemalige")
            query = query.Where(k =>
                !db.Abos.Any(a => a.KundeId == k.Id && a.Status == Domain.Subscriptions.AboStatus.Laufend)
                && !db.Vertraege.Any(v => v.KundeId == k.Id && v.Status == Domain.Contracts.VertragsStatus.Aktiv && v.MindestlaufzeitMonate != null)
                && (db.Abos.Any(a => a.KundeId == k.Id
                        && (a.Status == Domain.Subscriptions.AboStatus.Abgeloest || a.Status == Domain.Subscriptions.AboStatus.Beendet))
                    || db.Vertraege.Any(v => v.KundeId == k.Id
                        && (v.Status == Domain.Contracts.VertragsStatus.Gekuendigt || v.Status == Domain.Contracts.VertragsStatus.Beendet))));
        else if (kategorie == "interessenten")
            query = query.Where(k =>
                !db.Abos.Any(a => a.KundeId == k.Id && a.Status != Domain.Subscriptions.AboStatus.Entwurf)
                && !db.Vertraege.Any(v => v.KundeId == k.Id));

        var gesamt = await query.CountAsync(ct);
        var roh = await query
            .OrderBy(k => k.Firmenname)
            .Skip((seite - 1) * proSeite)
            .Take(proSeite)
            .Select(k => new
            {
                k.Id,
                k.Kundennummer,
                k.Firmenname,
                k.Status,
                k.Betriebsart,
                k.Merkmale,
                k.ArchiviertAm,
                Ort = k.Adressen.Where(a => a.Typ == AdressTyp.Hauptsitz).Select(a => a.Ort).FirstOrDefault(),
                ServerAlias = k.Server!.Alias,
                ServerErreichbar = k.Server!.IstErreichbar,
                ServerConnector = k.Server!.ConnectorAktiv,
                ServerFehler = k.Server!.Fehlertext,
                Vermittelt = k.VermitteltDurchPartner!.Name,
                RechnungAn = k.RechnungsempfaengerPartner!.Name,
                HatAktives = db.Abos.Any(a => a.KundeId == k.Id && a.Status == Domain.Subscriptions.AboStatus.Laufend)
                    || db.Vertraege.Any(v => v.KundeId == k.Id
                        && v.Status == Domain.Contracts.VertragsStatus.Aktiv && v.MindestlaufzeitMonate != null),
                IstEhemalig = db.Abos.Any(a => a.KundeId == k.Id
                        && (a.Status == Domain.Subscriptions.AboStatus.Abgeloest || a.Status == Domain.Subscriptions.AboStatus.Beendet))
                    || db.Vertraege.Any(v => v.KundeId == k.Id
                        && (v.Status == Domain.Contracts.VertragsStatus.Gekuendigt || v.Status == Domain.Contracts.VertragsStatus.Beendet)),
                IstEinmalbestellung = db.Vertraege.Any(v => v.KundeId == k.Id)
                    && !db.Abos.Any(a => a.KundeId == k.Id && a.Status != Domain.Subscriptions.AboStatus.Entwurf)
                    && !db.Vertraege.Any(v => v.KundeId == k.Id
                        && v.Bestandteile.Any(b => b.Typ != Domain.Contracts.BestandteilTyp.Bestellung)),
                // Abo-Entwürfe = Angebote → kein Artefakt (Interessent bleibt Interessent)
                HatArtefakte = db.Abos.Any(a => a.KundeId == k.Id && a.Status != Domain.Subscriptions.AboStatus.Entwurf)
                    || db.Vertraege.Any(v => v.KundeId == k.Id),
            })
            .ToListAsync(ct);

        var eintraege = roh.Select(k => new KundeListenEintrag(
                k.Id,
                k.Kundennummer,
                k.Firmenname,
                StatusText(k.Status),
                BetriebsartText(k.Betriebsart),
                k.Ort,
                k.RechnungAn != null ? $"via {k.RechnungAn} · Rechnung an Partner"
                    : k.Vermittelt != null ? $"via {k.Vermittelt}" : "",
                KategorieText(k.HatAktives, k.IstEhemalig, k.IstEinmalbestellung, k.HatArtefakte),
                k.Merkmale,
                k.ArchiviertAm != null,
                k.ServerAlias,
                k.ServerAlias == null ? null : ServerAmpel.Berechne(k.ServerErreichbar, k.ServerConnector),
                k.ServerFehler))
            .ToList();

        return Ok(new SeitenErgebnis<KundeListenEintrag>(eintraege, gesamt, seite, proSeite));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<KundeDetail>> Einzeln(int id, CancellationToken ct)
    {
        var kunde = await db.Kunden
            .AsNoTracking()
            .Include(k => k.Ansprechpartner)
            .Include(k => k.Adressen)
            .Include(k => k.VermitteltDurchPartner)
            .Include(k => k.RechnungsempfaengerPartner)
            .Include(k => k.Server)
            .FirstOrDefaultAsync(k => k.Id == id, ct);
        if (kunde is null)
            return NotFound();

        var hatAktives = await db.Abos.AnyAsync(
                a => a.KundeId == id && a.Status == Domain.Subscriptions.AboStatus.Laufend, ct)
            || await db.Vertraege.AnyAsync(v => v.KundeId == id
                && v.Status == Domain.Contracts.VertragsStatus.Aktiv && v.MindestlaufzeitMonate != null, ct);
        var istEhemalig = await db.Abos.AnyAsync(a => a.KundeId == id
                && (a.Status == Domain.Subscriptions.AboStatus.Abgeloest || a.Status == Domain.Subscriptions.AboStatus.Beendet), ct)
            || await db.Vertraege.AnyAsync(v => v.KundeId == id
                && (v.Status == Domain.Contracts.VertragsStatus.Gekuendigt || v.Status == Domain.Contracts.VertragsStatus.Beendet), ct);
        var istEinmalbestellung = await db.Vertraege.AnyAsync(v => v.KundeId == id, ct)
            && !await db.Abos.AnyAsync(a => a.KundeId == id
                && a.Status != Domain.Subscriptions.AboStatus.Entwurf, ct)
            && !await db.Vertraege.AnyAsync(v => v.KundeId == id
                && v.Bestandteile.Any(b => b.Typ != Domain.Contracts.BestandteilTyp.Bestellung), ct);
        var auchPartner = await db.Partner.AsNoTracking()
            .Where(p => p.KundeId == id).Select(p => new { p.Id, p.Name }).FirstOrDefaultAsync(ct);
        var hatArtefakte = await db.Abos.AnyAsync(a => a.KundeId == id
                && a.Status != Domain.Subscriptions.AboStatus.Entwurf, ct)
            || await db.Vertraege.AnyAsync(v => v.KundeId == id, ct);

        return Ok(new KundeDetail(
            kunde.Id,
            kunde.Kundennummer,
            kunde.Firmenname,
            StatusText(kunde.Status),
            BetriebsartText(kunde.Betriebsart),
            kunde.Kundengruppe,
            kunde.UStIdNr,
            kunde.OdooId,
            kunde.Notiz,
            kunde.VermitteltDurchPartner?.Name,
            kunde.RechnungsempfaengerPartner?.Name,
            kunde.Ansprechpartner
                .OrderByDescending(a => a.Aktiv)
                .ThenBy(a => a.Nachname)
                .Select(a => new AnsprechpartnerEintrag(
                    a.Id, AnredeText(a.Anrede), a.Titel, a.Vorname, a.Nachname, a.Position,
                    a.Email, a.TelefonFestnetz, a.TelefonMobil, RollenTexte(a.Rollen), a.Aktiv))
                .ToList(),
            kunde.Adressen
                .Where(a => a.Aktiv)
                .OrderBy(a => a.Typ)
                .Select(a => new AdresseEintrag(
                    a.Id, AdressTypText(a.Typ), a.Strasse, a.Zusatz, a.Plz, a.Ort, a.Land))
                .ToList(),
            KategorieText(hatAktives, istEhemalig, istEinmalbestellung, hatArtefakte),
            kunde.Merkmale,
            kunde.MerkmalePruefen,
            kunde.ArchiviertAm,
            kunde.ServerId,
            kunde.Server?.Alias,
            kunde.MandantenKennung,
            kunde.Server is null ? null : ServerAmpel.Berechne(kunde.Server.IstErreichbar, kunde.Server.ConnectorAktiv),
            kunde.Server?.Fehlertext,
            kunde.Individualentwicklung,
            kunde.VermitteltDurchPartnerId,
            kunde.RechnungsempfaengerPartnerId,
            auchPartner?.Id,
            auchPartner?.Name));
    }

    /// <summary>
    /// Manuelle Merkmal-Pflege (z. B. "Insolvenz"). Merkmale werden nur hier
    /// geändert — kein Import überschreibt sie; Speichern setzt die
    /// Prüfen-Markierung zurück. Änderungen laufen durchs Audit-Log.
    /// </summary>
    [HttpPut("{id:int}/merkmale")]
    public async Task<IActionResult> MerkmaleAendern(int id, MerkmaleAenderung aenderung, CancellationToken ct)
    {
        var kunde = await db.Kunden.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (kunde is null)
            return NotFound();

        var wert = string.IsNullOrWhiteSpace(aenderung.Wert) ? null : aenderung.Wert.Trim();
        if (wert is { Length: > 500 })
            return BadRequest("Merkmale dürfen höchstens 500 Zeichen lang sein.");

        var alt = kunde.Merkmale;
        kunde.Merkmale = wert;
        kunde.MerkmalePruefen = false;

        if (alt != wert)
        {
            db.Aktivitaeten.Add(new Domain.Shared.Aktivitaet
            {
                KundeId = kunde.Id,
                Typ = Domain.Shared.AktivitaetsTyp.Notiz,
                Zeitpunkt = DateTime.UtcNow,
                MitarbeiterId = await AktuellerMitarbeiterId(ct),
                Richtung = Domain.Shared.AktivitaetsRichtung.Intern,
                Betreff = wert is null
                    ? $"Merkmal „{alt}“ entfernt"
                    : alt is null ? $"Merkmal „{wert}“ vergeben" : $"Merkmal geändert: „{alt}“ → „{wert}“",
            });
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Individualentwicklung pflegen (FEEDBACK Nr. 2) — gesetzter Text ist
    /// zugleich das Kennzeichen. Manuell gepflegt, mit Verlaufseintrag.
    /// </summary>
    [HttpPut("{id:int}/individualentwicklung")]
    public async Task<IActionResult> IndividualentwicklungAendern(int id, IndividualentwicklungAenderung aenderung, CancellationToken ct)
    {
        var kunde = await db.Kunden.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (kunde is null)
            return NotFound();

        var wert = string.IsNullOrWhiteSpace(aenderung.Wert) ? null : aenderung.Wert.Trim();
        if (wert is { Length: > 2000 })
            return BadRequest("Die Beschreibung darf höchstens 2000 Zeichen lang sein.");

        var alt = kunde.Individualentwicklung;
        kunde.Individualentwicklung = wert;

        if (alt != wert)
        {
            db.Aktivitaeten.Add(new Domain.Shared.Aktivitaet
            {
                KundeId = kunde.Id,
                Typ = Domain.Shared.AktivitaetsTyp.Notiz,
                Zeitpunkt = DateTime.UtcNow,
                MitarbeiterId = await AktuellerMitarbeiterId(ct),
                Richtung = Domain.Shared.AktivitaetsRichtung.Intern,
                Betreff = wert is null
                    ? "Kennzeichen Individualentwicklung entfernt"
                    : alt is null ? "Individualentwicklung hinterlegt" : "Individualentwicklung geändert",
                Inhalt = wert,
            });
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Server-Zuordnung des Kunden (Betriebs-Stammdaten, intern).</summary>
    [HttpPut("{id:int}/server")]
    public async Task<IActionResult> ServerZuordnen(int id, KundeServerZuordnung zuordnung, CancellationToken ct)
    {
        var kunde = await db.Kunden.Include(k => k.Server).FirstOrDefaultAsync(k => k.Id == id, ct);
        if (kunde is null)
            return NotFound();

        Domain.Servers.Server? neuerServer = null;
        if (zuordnung.ServerId is { } serverId)
        {
            neuerServer = await db.Server.FirstOrDefaultAsync(s => s.Id == serverId, ct);
            if (neuerServer is null)
                return BadRequest("Der angegebene Server existiert nicht.");
        }

        var alterAlias = kunde.Server?.Alias;
        kunde.ServerId = zuordnung.ServerId;
        kunde.MandantenKennung = string.IsNullOrWhiteSpace(zuordnung.MandantenKennung)
            ? null : zuordnung.MandantenKennung.Trim();

        if (alterAlias != neuerServer?.Alias)
        {
            db.Aktivitaeten.Add(new Domain.Shared.Aktivitaet
            {
                KundeId = kunde.Id,
                Typ = Domain.Shared.AktivitaetsTyp.Notiz,
                Zeitpunkt = DateTime.UtcNow,
                MitarbeiterId = await AktuellerMitarbeiterId(ct),
                Richtung = Domain.Shared.AktivitaetsRichtung.Intern,
                Betreff = neuerServer is null
                    ? $"Server-Zuordnung entfernt (vorher {alterAlias})"
                    : alterAlias is null
                        ? $"Server zugeordnet: {neuerServer.Alias}"
                        : $"Server-Umzug: {alterAlias} → {neuerServer.Alias}",
            });
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Kunden ins Archiv verschieben bzw. zurückholen. Neue Geschäftsaktivität
    /// (laufendes Abo aus dem Odoo-Import, neuer Vertrag) holt archivierte
    /// Kunden automatisch zurück.
    /// </summary>
    [HttpPut("{id:int}/archiv")]
    public async Task<IActionResult> ArchivAendern(int id, ArchivAenderung aenderung, CancellationToken ct)
    {
        var kunde = await db.Kunden.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (kunde is null)
            return NotFound();

        var istArchiviert = kunde.ArchiviertAm != null;
        if (istArchiviert == aenderung.Archivieren)
            return NoContent();

        kunde.ArchiviertAm = aenderung.Archivieren ? DateTime.UtcNow : null;
        db.Aktivitaeten.Add(new Domain.Shared.Aktivitaet
        {
            KundeId = kunde.Id,
            Typ = Domain.Shared.AktivitaetsTyp.Notiz,
            Zeitpunkt = DateTime.UtcNow,
            MitarbeiterId = await AktuellerMitarbeiterId(ct),
            Richtung = Domain.Shared.AktivitaetsRichtung.Intern,
            Betreff = aenderung.Archivieren
                ? $"Kunde ins Archiv verschoben{(kunde.Merkmale is not null ? $" (Merkmal: {kunde.Merkmale})" : "")}"
                : "Kunde aus dem Archiv geholt",
        });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Task<int?> AktuellerMitarbeiterId(CancellationToken ct) => angemeldet.IdAsync(ct);

    /// <summary>Abos des Kunden mit Lizenz-Positionen — Basis der Karte „Betrieb &amp; Lizenzen".</summary>
    [HttpGet("{id:int}/abos")]
    public async Task<ActionResult<List<AboEintrag>>> Abos(int id, CancellationToken ct)
    {
        var abos = await db.Abos
            .AsNoTracking()
            .Where(a => a.KundeId == id)
            .Include(a => a.Positionen)
            .OrderBy(a => a.Status)
            .ThenByDescending(a => a.Nummer)
            .ToListAsync(ct);

        return Ok(abos.Select(a => new AboEintrag(
            a.Id,
            a.Nummer,
            a.Status switch
            {
                Domain.Subscriptions.AboStatus.Laufend => "Laufend",
                Domain.Subscriptions.AboStatus.Abgeloest => "Abgelöst",
                Domain.Subscriptions.AboStatus.Beendet => "Beendet",
                _ => "Entwurf",
            },
            a.IntervallMonate,
            a.LaufzeitBeginn ?? a.Beginn,
            a.NaechsteVerlaengerung,
            a.Positionen
                .OrderByDescending(p => p.Menge)
                .Select(p => new AboPositionEintrag(p.Produkt, p.Menge))
                .ToList())).ToList());
    }

    /// <summary>
    /// Timeline der Kundenakte — neueste Einträge zuerst.
    /// <paramref name="mitOdoo"/> = false blendet alle aus Odoo übernommenen Einträge aus;
    /// <paramref name="kategorien"/> (kommagetrennt, z. B. „Notizen,EMails,Meilensteine")
    /// wählt die sichtbaren Kategorien, leer = Standard (das Wesentliche);
    /// <paramref name="vorgang"/> („sale.order:1198") zeigt nur die Einträge eines Odoo-Vorgangs.
    /// </summary>
    [HttpGet("{id:int}/verlauf")]
    public async Task<ActionResult<List<VerlaufEintrag>>> Verlauf(int id, [FromQuery] bool mitOdoo = true,
        [FromQuery] string? kategorien = null, [FromQuery] string? vorgang = null,
        [FromQuery] int? tage = null, [FromQuery] string? paar = null, CancellationToken ct = default)
    {
        var abfrage = VerlaufAbfrage(id, mitOdoo, vorgang, tage, paar)
            .Where(Domain.Shared.VerlaufKategorien.Filter(KategorienLesen(kategorien)));
        var odooUrl = await OdooUrlAsync(ct);

        var roh = await abfrage
            .OrderByDescending(a => a.Zeitpunkt)
            .Take(300)
            .Select(a => new
            {
                a.Id, a.Typ, a.Zeitpunkt, a.Betreff, a.Inhalt, a.VerweisTyp, a.VerweisId,
                a.Quelle, a.Richtung, a.AutorName, a.Bezug, a.Stufe, a.FilterGrund, a.OdooId, a.OdooModell, a.OdooResId,
                a.Empfaenger, a.StrangSchluessel, a.StrangName,
                Mitarbeiter = a.Mitarbeiter != null ? a.Mitarbeiter.Anzeigename : null,
                Bereich = a.Mitarbeiter != null && a.Mitarbeiter.Bereich != null ? a.Mitarbeiter.Bereich.Name : null,
                ApAnrede = a.Ansprechpartner != null ? a.Ansprechpartner.Anrede : (Anrede?)null,
                ApVorname = a.Ansprechpartner != null ? a.Ansprechpartner.Vorname : null,
                ApNachname = a.Ansprechpartner != null ? a.Ansprechpartner.Nachname : null,
                Anhaenge = a.Anhaenge.OrderBy(x => x.Dateiname)
                    .Select(x => new { x.Id, x.Dateiname, x.Groesse, x.MimeType, x.RelativerPfad, x.OdooId }).ToList(),
                AutorRoh = a.Mitarbeiter == null ? null : new Dienste.TeilnehmerRoh(Domain.Shared.TeilnehmerSeite.Rausoft,
                    a.MitarbeiterId, a.Mitarbeiter.Anzeigename, a.Mitarbeiter.Bereich != null ? a.Mitarbeiter.Bereich.Name : null,
                    null, null, null, null, null, null),
                ApRoh = a.Ansprechpartner == null ? null : new Dienste.TeilnehmerRoh(Domain.Shared.TeilnehmerSeite.Kunde,
                    null, null, null, a.AnsprechpartnerId, a.Ansprechpartner.Anrede, a.Ansprechpartner.Vorname,
                    a.Ansprechpartner.Nachname, a.Ansprechpartner.Position, null),
                Teilnehmer = a.Teilnehmer.Select(t => new Dienste.TeilnehmerRoh(t.Seite, t.MitarbeiterId,
                    t.Mitarbeiter != null ? t.Mitarbeiter.Anzeigename : null,
                    t.Mitarbeiter != null && t.Mitarbeiter.Bereich != null ? t.Mitarbeiter.Bereich.Name : null,
                    t.AnsprechpartnerId,
                    t.Ansprechpartner != null ? t.Ansprechpartner.Anrede : null,
                    t.Ansprechpartner != null ? t.Ansprechpartner.Vorname : null,
                    t.Ansprechpartner != null ? t.Ansprechpartner.Nachname : null,
                    t.Ansprechpartner != null ? t.Ansprechpartner.Position : null,
                    t.Name)).ToList(),
                Vereinbarungen = db.Vereinbarungen.Where(v => v.AktivitaetId == a.Id).OrderBy(v => v.Id).Select(v => v.Text).ToList(),
            })
            .ToListAsync(ct);
        return Ok(roh.Select(a =>
        {
            // Ansprechpartner mit Anrede („Frau Berger"), ohne Anrede mit Vorname
            var ansprechpartner = a.ApNachname is null ? null
                : a.ApAnrede is Anrede.Herr or Anrede.Frau ? $"{a.ApAnrede} {a.ApNachname}"
                : string.Join(' ', new[] { a.ApVorname, a.ApNachname }.Where(s => !string.IsNullOrWhiteSpace(s)));
            var autorName = Domain.Shared.Absender.Anzeigename(a.AutorName);
            var seiten = Dienste.AkteTeilnehmer.Seiten(a.AutorRoh, a.ApRoh, a.AutorName, a.Richtung, a.Teilnehmer);
            // Autor = Absender: bei eingehenden Nachrichten die Kundenseite, sonst rausoft
            string? autor, bereich = null, an;
            bool kunde;
            if (a.Richtung == Domain.Shared.AktivitaetsRichtung.Eingehend)
            {
                autor = ansprechpartner ?? autorName ?? a.Mitarbeiter;
                kunde = autor is not null && autor != a.Mitarbeiter;
                if (!kunde) bereich = a.Bereich;
                an = a.Empfaenger ?? (kunde ? a.Mitarbeiter : null);
            }
            else
            {
                autor = a.Mitarbeiter ?? autorName ?? ansprechpartner;
                bereich = a.Mitarbeiter is not null ? a.Bereich : null;
                kunde = a.Mitarbeiter is null && autorName is null && ansprechpartner is not null;
                an = a.Empfaenger ?? (a.Mitarbeiter is not null ? ansprechpartner : null);
            }
            return new VerlaufEintrag(
            a.Id, a.Typ.ToString(), a.Zeitpunkt, a.Mitarbeiter, ansprechpartner,
            a.Betreff, a.Inhalt, a.VerweisTyp?.ToString(), a.VerweisId,
            a.Quelle.ToString(), a.Richtung?.ToString(), a.AutorName, a.Bezug,
            a.Anhaenge.Select(x => new VerlaufAnhang(x.Id, x.Dateiname, x.Groesse, x.MimeType,
                Lokal: x.RelativerPfad != null,
                OdooLink: odooUrl is not null && x.OdooId is not null ? $"{odooUrl}/web/content/{x.OdooId}?download=true" : null)).ToList(),
            Domain.Shared.VerlaufKategorien.Von(a.Stufe, a.Typ).ToString(),
            a.FilterGrund,
            a.StrangSchluessel ?? (a.OdooModell is not null && a.OdooResId is not null ? $"{a.OdooModell}:{a.OdooResId}" : null),
            // /mail/message/<id> springt nach dem Odoo-Login direkt zur Nachricht im Chatter des Vorgangs
            odooUrl is not null && a.OdooId is not null ? $"{odooUrl}/mail/message/{a.OdooId}" : null,
            autor, bereich, kunde, an, a.StrangName ?? a.Bezug,
            seiten.Rausoft, seiten.Kunde, a.Vereinbarungen.Count > 0 ? a.Vereinbarungen : null);
        }).ToList());
    }

    /// <summary>Anzahl Einträge je Kategorie — für die Zahlen an den Schaltern der Akte.</summary>
    [HttpGet("{id:int}/verlauf/uebersicht")]
    public async Task<ActionResult<VerlaufUebersicht>> VerlaufZaehlen(int id, [FromQuery] bool mitOdoo = true,
        [FromQuery] string? vorgang = null, [FromQuery] int? tage = null, [FromQuery] string? paar = null,
        CancellationToken ct = default)
    {
        var gruppen = await VerlaufAbfrage(id, mitOdoo, vorgang, tage, paar)
            .GroupBy(a => new { a.Stufe, a.Typ })
            .Select(g => new { g.Key.Stufe, g.Key.Typ, Anzahl = g.Count() })
            .ToListAsync(ct);
        var zaehler = Enum.GetValues<Domain.Shared.VerlaufKategorie>().ToDictionary(k => k.ToString(), _ => 0);
        foreach (var g in gruppen)
            zaehler[Domain.Shared.VerlaufKategorien.Von(g.Stufe, g.Typ).ToString()] += g.Anzahl;
        return Ok(new VerlaufUebersicht(zaehler));
    }

    /// <summary>
    /// Gemeinsame Einschränkung für Verlauf und Zähler: Odoo ein/aus, Vorgang, Zeitraum in Tagen und
    /// Gesprächspaar „m:5|a:7" (Klick in „Wer mit wem" — beide Personen beteiligt).
    /// </summary>
    private IQueryable<Domain.Shared.Aktivitaet> VerlaufAbfrage(int kundeId, bool mitOdoo, string? vorgang,
        int? tage = null, string? paar = null)
    {
        var abfrage = db.Aktivitaeten.AsNoTracking().Where(a => a.KundeId == kundeId);
        if (!mitOdoo)
            abfrage = abfrage.Where(a => a.Quelle != Domain.Shared.AktivitaetsQuelle.OdooImport);
        if (tage is > 0 and < 3650)
        {
            var seit = DateTime.UtcNow.Date.AddDays(-tage.Value);
            abfrage = abfrage.Where(a => a.Zeitpunkt >= seit);
        }
        foreach (var person in (paar ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (person.StartsWith("m:") && int.TryParse(person[2..], out var mid))
                abfrage = abfrage.Where(a => a.MitarbeiterId == mid || a.Teilnehmer.Any(t => t.MitarbeiterId == mid));
            else if (person.StartsWith("a:") && int.TryParse(person[2..], out var aid))
                abfrage = abfrage.Where(a => a.AnsprechpartnerId == aid || a.Teilnehmer.Any(t => t.AnsprechpartnerId == aid));
            else if (person.StartsWith("n:") && person.Length > 2)
            {
                var name = person[2..];
                abfrage = abfrage.Where(a => (a.AutorName != null && a.AutorName.Contains(name)) || a.Teilnehmer.Any(t => t.Name == name));
            }
        }
        if (string.IsNullOrWhiteSpace(vorgang))
            return abfrage;
        // Altbestand vor dem Neuimport: „sale.order:1198" (Modell mit Punkt) — sonst Strang-Schlüssel
        if (vorgang.Split(':') is [var modell, var resIdText] && modell.Contains('.') && int.TryParse(resIdText, out var resId))
            abfrage = abfrage.Where(a => a.OdooModell == modell && a.OdooResId == resId);
        else
            abfrage = abfrage.Where(a => a.StrangSchluessel == vorgang);
        return abfrage;
    }

    private static List<Domain.Shared.VerlaufKategorie> KategorienLesen(string? kategorien)
    {
        if (string.IsNullOrWhiteSpace(kategorien))
            return [.. Domain.Shared.VerlaufKategorien.Standard];
        return kategorien.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(k => Enum.TryParse<Domain.Shared.VerlaufKategorie>(k, ignoreCase: true, out var kat) ? kat : (Domain.Shared.VerlaufKategorie?)null)
            .OfType<Domain.Shared.VerlaufKategorie>()
            .ToList();
    }

    /// <summary>Basis-URL der Odoo-Instanz für Direktlinks, ohne abschließenden Schrägstrich; null wenn nicht gepflegt.</summary>
    private async Task<string?> OdooUrlAsync(CancellationToken ct)
    {
        var url = (await db.Einstellungen.AsNoTracking()
            .Where(e => e.Schluessel == Domain.Shared.EinstellungsSchluessel.OdooUrl)
            .Select(e => e.Wert).FirstOrDefaultAsync(ct))?.Trim().TrimEnd('/');
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? url : null;
    }

    /// <summary>
    /// Datei eines Timeline-Anhangs (z. B. aus dem Odoo-Chatter übernommene E-Mail-Anlage).
    /// Die Datei liegt in der Dokumentablage; nur Pfade unterhalb des Basisverzeichnisses werden ausgeliefert.
    /// </summary>
    [HttpGet("verlauf/anhaenge/{anhangId:int}/datei")]
    public async Task<IActionResult> VerlaufAnhangDatei(int anhangId, CancellationToken ct)
    {
        var anhang = await db.Aktivitaetsanhaenge.AsNoTracking().FirstOrDefaultAsync(a => a.Id == anhangId, ct);
        if (anhang is null)
            return NotFound();
        if (anhang.RelativerPfad is null)
            return NotFound("Dieser Anhang liegt nur in Odoo — bitte über den Odoo-Link öffnen.");

        var basis = (await db.Einstellungen.AsNoTracking()
            .Where(e => e.Schluessel == Domain.Shared.EinstellungsSchluessel.DokumentablageVerzeichnis)
            .Select(e => e.Wert).FirstOrDefaultAsync(ct))?.Trim();
        if (string.IsNullOrWhiteSpace(basis))
            return BadRequest("Das Verzeichnis der Dokumentablage ist nicht konfiguriert (Administration → Verzeichnisse).");

        var basisVoll = Path.GetFullPath(basis).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var dateiVoll = Path.GetFullPath(Path.Combine(basisVoll, anhang.RelativerPfad));
        if (!dateiVoll.StartsWith(basisVoll, StringComparison.OrdinalIgnoreCase))
            return BadRequest("Der Pfad liegt außerhalb der Dokumentablage.");
        if (!System.IO.File.Exists(dateiVoll))
            return NotFound($"Die Datei liegt nicht (mehr) in der Dokumentablage: {anhang.RelativerPfad}");

        var contentType = anhang.MimeType;
        if (string.IsNullOrWhiteSpace(contentType))
            new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider()
                .TryGetContentType(anhang.Dateiname, out contentType);

        var stream = System.IO.File.OpenRead(dateiVoll);
        return File(stream, contentType ?? "application/octet-stream", anhang.Dateiname, enableRangeProcessing: true);
    }

    /// <summary>
    /// Berechnete Kategorie (Regel V2, 09/2026): Kunde = laufendes Abo oder
    /// aktiver Vertrag MIT gepflegter Laufzeit — aktive Verträge ohne Laufzeit
    /// gelten als ungeklärt (oft unerkannte Kündigungen im Bestand).
    /// </summary>
    internal static System.Linq.Expressions.Expression<Func<Kunde, bool>> KundenRegel(ErpDbContext db) =>
        k => db.Abos.Any(a => a.KundeId == k.Id && a.Status == Domain.Subscriptions.AboStatus.Laufend)
             || db.Vertraege.Any(v => v.KundeId == k.Id
                 && v.Status == Domain.Contracts.VertragsStatus.Aktiv && v.MindestlaufzeitMonate != null);

    /// <summary>
    /// Kategorie + „laufendes Abo" für eine Kundenmenge (z. B. die Kunden eines
    /// Partners) — gleiche Regeln wie die Kundenliste.
    /// </summary>
    internal static async Task<Dictionary<int, (string Kategorie, bool AktivesAbo)>> KategorienAsync(
        ErpDbContext db, IQueryable<Kunde> kunden, CancellationToken ct) =>
        (await kunden
            .Select(k => new
            {
                k.Id,
                AktivesAbo = db.Abos.Any(a => a.KundeId == k.Id && a.Status == Domain.Subscriptions.AboStatus.Laufend),
                HatAktives = db.Abos.Any(a => a.KundeId == k.Id && a.Status == Domain.Subscriptions.AboStatus.Laufend)
                    || db.Vertraege.Any(v => v.KundeId == k.Id
                        && v.Status == Domain.Contracts.VertragsStatus.Aktiv && v.MindestlaufzeitMonate != null),
                IstEhemalig = db.Abos.Any(a => a.KundeId == k.Id
                        && (a.Status == Domain.Subscriptions.AboStatus.Abgeloest || a.Status == Domain.Subscriptions.AboStatus.Beendet))
                    || db.Vertraege.Any(v => v.KundeId == k.Id
                        && (v.Status == Domain.Contracts.VertragsStatus.Gekuendigt || v.Status == Domain.Contracts.VertragsStatus.Beendet)),
                IstEinmalbestellung = db.Vertraege.Any(v => v.KundeId == k.Id)
                    && !db.Abos.Any(a => a.KundeId == k.Id && a.Status != Domain.Subscriptions.AboStatus.Entwurf)
                    && !db.Vertraege.Any(v => v.KundeId == k.Id
                        && v.Bestandteile.Any(b => b.Typ != Domain.Contracts.BestandteilTyp.Bestellung)),
                HatArtefakte = db.Abos.Any(a => a.KundeId == k.Id && a.Status != Domain.Subscriptions.AboStatus.Entwurf)
                    || db.Vertraege.Any(v => v.KundeId == k.Id),
            })
            .ToListAsync(ct))
        .ToDictionary(k => k.Id, k => (
            KategorieText(k.HatAktives, k.IstEhemalig, k.IstEinmalbestellung, k.HatArtefakte), k.AktivesAbo));

    private static string KategorieText(bool hatAktives, bool istEhemalig, bool istEinmalbestellung, bool hatArtefakte) =>
        hatAktives ? "Kunde"
        : istEhemalig ? "Ehemaliger Kunde"
        : istEinmalbestellung ? "Einmalbestellung"
        : hatArtefakte ? "Zu klären"
        : "Interessent";

    private static string StatusText(KundenStatus status) => status switch
    {
        KundenStatus.Aktiv => "Aktiv",
        KundenStatus.Inaktiv => "Inaktiv",
        _ => status.ToString(),
    };

    private static string? BetriebsartText(Betriebsart? betriebsart) => betriebsart switch
    {
        Betriebsart.SharedCloud => "Shared Cloud",
        Betriebsart.PrivateCloud => "Private Cloud",
        Betriebsart.OnPrem => "OnPrem",
        _ => null,
    };

    private static string AnredeText(Anrede anrede) => anrede switch
    {
        Anrede.Herr => "Herr",
        Anrede.Frau => "Frau",
        _ => "",
    };

    private static string AdressTypText(AdressTyp typ) => typ switch
    {
        AdressTyp.Hauptsitz => "Hauptsitz",
        AdressTyp.Rechnung => "Rechnungsanschrift",
        _ => "Sonstige",
    };

    private static List<string> RollenTexte(AnsprechpartnerRolle rollen)
    {
        var texte = new List<string>();
        if (rollen.HasFlag(AnsprechpartnerRolle.Hauptansprechpartner)) texte.Add("Hauptansprechpartner");
        if (rollen.HasFlag(AnsprechpartnerRolle.Vertrag)) texte.Add("Vertrag");
        if (rollen.HasFlag(AnsprechpartnerRolle.Technik)) texte.Add("Technik");
        if (rollen.HasFlag(AnsprechpartnerRolle.Rechnungsempfang)) texte.Add("Rechnungsempfang");
        return texte;
    }
}
