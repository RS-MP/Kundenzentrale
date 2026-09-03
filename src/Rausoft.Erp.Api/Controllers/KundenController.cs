using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Customers;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

[ApiController]
[Route("api/kunden")]
public class KundenController(ErpDbContext db) : ControllerBase
{
    private const int MaxProSeite = 100;

    [HttpGet]
    public async Task<ActionResult<SeitenErgebnis<KundeListenEintrag>>> Liste(
        [FromQuery] string? suche, [FromQuery] string? status, [FromQuery] bool mitPartner = false,
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
                Ort = k.Adressen.Where(a => a.Typ == AdressTyp.Hauptsitz).Select(a => a.Ort).FirstOrDefault(),
                Vermittelt = k.VermitteltDurchPartner!.Name,
                RechnungAn = k.RechnungsempfaengerPartner!.Name,
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
                    : k.Vermittelt != null ? $"via {k.Vermittelt}" : ""))
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
            .FirstOrDefaultAsync(k => k.Id == id, ct);
        if (kunde is null)
            return NotFound();

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
                .ToList()));
    }

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

    /// <summary>Timeline der Kundenakte — neueste Einträge zuerst.</summary>
    [HttpGet("{id:int}/verlauf")]
    public async Task<ActionResult<List<VerlaufEintrag>>> Verlauf(int id, CancellationToken ct)
    {
        var roh = await db.Aktivitaeten
            .AsNoTracking()
            .Where(a => a.KundeId == id)
            .OrderByDescending(a => a.Zeitpunkt)
            .Take(100)
            .Select(a => new
            {
                a.Id, a.Typ, a.Zeitpunkt, a.Betreff, a.Inhalt, a.VerweisTyp, a.VerweisId,
                Mitarbeiter = a.Mitarbeiter != null ? a.Mitarbeiter.Anzeigename : null,
                Ansprechpartner = a.Ansprechpartner != null ? a.Ansprechpartner.Nachname : null,
            })
            .ToListAsync(ct);
        return Ok(roh.Select(a => new VerlaufEintrag(
            a.Id, a.Typ.ToString(), a.Zeitpunkt, a.Mitarbeiter, a.Ansprechpartner,
            a.Betreff, a.Inhalt, a.VerweisTyp?.ToString(), a.VerweisId)).ToList());
    }

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
