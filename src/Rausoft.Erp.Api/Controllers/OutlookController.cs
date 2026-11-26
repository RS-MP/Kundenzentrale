using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Api.Dienste;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Customers;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Outlook-Anbindung, Durchstich (26.11.2026): Die Kundenzentrale bleibt bei der
/// Windows-Anmeldung. Für den Entwurf im eigenen Postfach meldet sich der Browser des
/// Mitarbeiters selbst bei Microsoft an (öffentlicher Client mit PKCE) — der Server sieht nie
/// ein Graph-Token und speichert keines. Das Add-in schickt nur Absender, Empfänger, Betreff
/// und Kennung der geöffneten Mail; gespeichert wird im Durchstich nichts.
/// </summary>
[ApiController]
[Route("api/outlook")]
public class OutlookController(ErpDbContext db, AngemeldeterMitarbeiter angemeldet, InstanzOptionen instanz) : ControllerBase
{
    private const int MaxEmpfaenger = 50;

    [HttpGet("konfiguration")]
    public async Task<ActionResult<OutlookKonfiguration>> Konfiguration(CancellationToken ct)
    {
        var w = await db.Einstellungen.AsNoTracking().Where(e => e.Schluessel.StartsWith("Outlook."))
            .ToDictionaryAsync(e => e.Schluessel, e => e.Wert, ct);
        // Beide Werte landen in einer Adresse bei Microsoft — nur echte Kennungen durchlassen
        var tenant = Guid.TryParse(w.GetValueOrDefault(EinstellungsSchluessel.OutlookTenantId), out var t) ? t.ToString() : null;
        var client = Guid.TryParse(w.GetValueOrDefault(EinstellungsSchluessel.OutlookClientId), out var c) ? c.ToString() : null;
        var ich = await IchAsync(ct);
        return Ok(new OutlookKonfiguration(
            tenant is not null && client is not null, tenant, client,
            ich?.Email is { } mail && !mail.EndsWith("@ad.lokal", StringComparison.OrdinalIgnoreCase) && !mail.EndsWith("@localhost") ? mail : null,
            ich?.Anzeigename ?? "(unbekannt)",
            instanz.Testsystem,
            instanz.Testsystem ? MailZuordnung.Normalisieren(instanz.MailUmleitung) ?? "" : null));
    }

    /// <summary>Manifest des Add-ins für diese Instanz — zum Hochladen in Outlook bzw. im M365 Admin Center.</summary>
    [HttpGet("manifest")]
    [Authorize(Policy = "Admin")]
    public IActionResult Manifest()
    {
        var basis = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/";
        if (Request.Scheme != Uri.UriSchemeHttps)
            return BadRequest("Outlook lädt Add-ins nur über https — bitte die Kundenzentrale über ihre https-Adresse öffnen und das Manifest dort herunterladen.");
        return File(Encoding.UTF8.GetBytes(OutlookManifest.Erzeugen(basis, instanz.Testsystem)), "application/xml",
            instanz.Testsystem ? "kundenzentrale-test-outlook.xml" : "kundenzentrale-outlook.xml");
    }

    /// <summary>Zu welchem Kunden gehört diese Mail? Nur Auskunft — der Durchstich speichert nichts.</summary>
    [HttpPost("zuordnung")]
    public async Task<ActionResult<OutlookZuordnung>> Zuordnung(OutlookMail mail, CancellationToken ct)
    {
        var ich = await IchAsync(ct);
        var name = ich?.Anzeigename ?? "(unbekannt)";
        if ((mail.Empfaenger?.Count ?? 0) > MaxEmpfaenger)
            return BadRequest("Zu viele Empfänger.");

        var eigene = await EigeneDomaenenAsync(ct);
        var fremde = MailZuordnung.Fremde(new[] { mail.Absender }.Concat(mail.Empfaenger ?? []), eigene);
        var perAdresse = fremde.Count == 0 ? [] : await db.Ansprechpartner.AsNoTracking()
            .Where(a => a.Aktiv && a.Email != null && fremde.Contains(a.Email))
            .Select(a => new { a.KundeId, Name = (a.Vorname + " " + a.Nachname).Trim() })
            .ToListAsync(ct);
        var perDomaene = new List<int>();
        if (perAdresse.Count == 0)
            foreach (var domaene in MailZuordnung.SuchDomaenen(fremde))
            {
                var ende = "@" + domaene;
                perDomaene.AddRange(await db.Ansprechpartner.AsNoTracking()
                    .Where(a => a.Aktiv && a.Email != null && a.Email.EndsWith(ende))
                    .Select(a => a.KundeId).Distinct().Take(20).ToListAsync(ct));
            }

        var (art, ids) = MailZuordnung.Entscheiden(fremde.Count > 0, perAdresse.Select(a => a.KundeId).ToList(), perDomaene);
        var kunden = ids.Count == 0 ? [] : (await db.Kunden.AsNoTracking().Where(k => ids.Contains(k.Id))
                .Select(k => new { k.Id, k.Firmenname, k.Kundennummer }).ToListAsync(ct))
            .Select(k => new OutlookKunde(k.Id, k.Firmenname, k.Kundennummer, perAdresse.FirstOrDefault(a => a.KundeId == k.Id)?.Name))
            .OrderBy(k => k.Firmenname).ToList();
        return Ok(new OutlookZuordnung(name, art, kunden, art switch
        {
            MailZuordnung.Adresse => "Die Adresse gehört zu einem Ansprechpartner dieses Kunden.",
            MailZuordnung.Domaene => "Kein Ansprechpartner mit dieser Adresse — die Domäne passt zu diesem Kunden.",
            MailZuordnung.Mehrdeutig => "Mehrere Kunden kommen in Frage — hier würde ein Mensch entscheiden.",
            MailZuordnung.NurEigene => "An dieser Mail sind nur eigene Adressen beteiligt.",
            _ => "Kein Kunde mit dieser Adresse oder Domäne — die Mail käme in die Liste „Zuzuordnen“.",
        }));
    }

    private async Task<Mitarbeiter?> IchAsync(CancellationToken ct) =>
        await angemeldet.IdAsync(ct) is { } id ? await db.Mitarbeiter.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct) : null;

    /// <summary>Domänen der eigenen Firma: aus den Mitarbeiter-Adressen und der Absenderadresse der Kundenzentrale.</summary>
    private async Task<List<string>> EigeneDomaenenAsync(CancellationToken ct)
    {
        var adressen = await db.Mitarbeiter.AsNoTracking().Where(m => m.Aktiv).Select(m => m.Email).ToListAsync(ct);
        adressen.Add(await db.Einstellungen.AsNoTracking().Where(e => e.Schluessel == EinstellungsSchluessel.MailAbsender)
            .Select(e => e.Wert).FirstOrDefaultAsync(ct) ?? "");
        return adressen.Select(MailZuordnung.Normalisieren).OfType<string>().Select(MailZuordnung.DomaeneVon)
            .Where(d => d is not ("ad.lokal" or "localhost")).Distinct().ToList();
    }
}
