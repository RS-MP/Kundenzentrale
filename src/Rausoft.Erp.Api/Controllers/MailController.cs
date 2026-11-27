using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Api.Dienste;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Dtos;
using Rausoft.Erp.OdooImport;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Kundenmail aus der Akte (ANFORDERUNGEN.md, „E-Mail aus der Akte“). Der Server stellt die
/// Mail zusammen (Baustein, Platzhalter, Signatur, Empfänger) und hält im Verlauf fest, was
/// daraus wurde. Ins Postfach kommt sie über den Browser des Mitarbeiters (Microsoft Graph,
/// <c>outlook-entwurf.js</c>) — der Server sieht nie ein Graph-Token und sendet selbst nichts.
/// Im Testsystem geht jede Mail an die Umleitungsadresse, nie an einen Kunden.
/// </summary>
[ApiController]
[Route("api/mail")]
public class MailController(ErpDbContext db, AngemeldeterMitarbeiter angemeldet, InstanzOptionen instanz) : ControllerBase
{
    private const int MaxHtml = 2_000_000;

    [HttpGet("kunde/{kundeId:int}")]
    public async Task<ActionResult<MailAuswahl>> Auswahl(int kundeId, CancellationToken ct)
    {
        var kunde = await db.Kunden.AsNoTracking().Where(k => k.Id == kundeId).Select(k => k.Firmenname).FirstOrDefaultAsync(ct);
        if (kunde is null)
            return NotFound();
        var ansprechpartner = (await db.Ansprechpartner.AsNoTracking()
                .Where(a => a.KundeId == kundeId && a.Aktiv && a.Email != null && a.Email != "")
                .OrderBy(a => a.Nachname).ThenBy(a => a.Vorname)
                .Select(a => new { a.Id, a.Vorname, a.Nachname, a.Email })
                .ToListAsync(ct))
            .Select(a => new MailAnsprechpartner(a.Id, $"{a.Vorname} {a.Nachname}".Trim(), a.Email!.Trim()))
            .ToList();
        var bausteine = await db.Textbausteine.AsNoTracking()
            .Where(t => t.Aktiv && t.EntferntAm == null)
            .OrderBy(t => t.Verwendung).ThenBy(t => t.Name)
            .Select(t => new MailBaustein(t.Id, t.Name, t.Verwendung, t.Anhaenge.Count))
            .ToListAsync(ct);
        var ich = await IchAsync(ct);
        var umleitung = await UmleitungAsync(ich, ct);
        return Ok(new MailAuswahl(kundeId, kunde, ansprechpartner, bausteine,
            !string.IsNullOrWhiteSpace(ich?.MailSignaturHtml), umleitung.Aktiv, umleitung.Adresse, umleitung.Hinweis,
            await VertragsdokumenteAsync(kundeId, ct)));

    }

    /// <summary>
    /// Vertrags-PDFs des Kunden zum Anhängen: je Bestandteil die neueste Fassung, laufende und
    /// neue Verträge zuerst. Vertrauliche Bestandteile nur mit der Rolle dafür — dieselbe Regel
    /// wie in der Vertragsakte; die Datei selbst liefert weiterhin nur der Vertrags-Endpunkt aus.
    /// </summary>
    private async Task<List<MailVertragsdokument>> VertragsdokumenteAsync(int kundeId, CancellationToken ct)
    {
        var darfVertrauliche = (await angemeldet.RollenAsync(ct)).HasFlag(MitarbeiterRolle.VertraulicheVertraege);
        var alle = await db.Vertragsdokumente.AsNoTracking()
            .Where(d => d.Vertragsbestandteil!.Vertrag!.KundeId == kundeId
                        && d.Vertragsbestandteil.Vertrag.Status != Rausoft.Erp.Domain.Contracts.VertragsStatus.Beendet
                        && (darfVertrauliche || !d.Vertragsbestandteil.Vertraulich))
            .Select(d => new
            {
                d.Id, d.Version, d.DateinameOriginal, d.ErstelltAm, d.VertragsbestandteilId,
                d.Vertragsbestandteil!.Titel, d.Vertragsbestandteil.UnterzeichnetAm, d.Vertragsbestandteil.VertragId,
                d.Vertragsbestandteil.Vertrag!.Vertragsnummer, d.Vertragsbestandteil.Vertrag.Bezeichnung, d.Vertragsbestandteil.Vertrag.Status,
            })
            .ToListAsync(ct);
        return alle
            .Where(d => d.DateinameOriginal.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .GroupBy(d => d.VertragsbestandteilId)
            .Select(g => g.OrderByDescending(d => d.Version).First())
            .OrderByDescending(d => d.ErstelltAm)
            .Take(40)
            .Select(d => new MailVertragsdokument(d.Id, d.VertragId, d.Vertragsnummer, d.Bezeichnung, d.Titel, d.DateinameOriginal,
                d.ErstelltAm, d.Status == Rausoft.Erp.Domain.Contracts.VertragsStatus.Entwurf, d.UnterzeichnetAm))
            .ToList();
    }

    /// <summary>Stellt die Mail zusammen, ohne etwas zu speichern — die Vorschau ist genau das, was ins Postfach geht.</summary>
    [HttpPost("zusammenstellen")]
    public async Task<ActionResult<MailFertig>> Zusammenstellen(MailZusammenstellen daten, CancellationToken ct)
    {
        if ((daten.Freitext ?? "").Length > MailRegeln.MaxFreitext)
            return BadRequest("Der eigene Text ist zu lang.");
        if ((daten.Betreff ?? "").Trim().Length > 500)
            return BadRequest("Der Betreff ist zu lang (höchstens 500 Zeichen).");
        var ich = await IchAsync(ct);
        if (ich is null)
            return Forbid();
        var kunde = await db.Kunden.AsNoTracking().Where(k => k.Id == daten.KundeId)
            .Select(k => new { k.Firmenname, k.Kundennummer }).FirstOrDefaultAsync(ct);
        if (kunde is null)
            return NotFound();
        var ansprechpartner = daten.AnsprechpartnerId is not { } apId ? null
            : await db.Ansprechpartner.AsNoTracking().Where(a => a.Id == apId && a.KundeId == daten.KundeId)
                .Select(a => new { a.Vorname, a.Nachname, a.Email }).FirstOrDefaultAsync(ct);
        if (daten.AnsprechpartnerId is not null && ansprechpartner is null)
            return BadRequest("Der Ansprechpartner gehört nicht zu diesem Kunden.");
        var baustein = daten.TextbausteinId is not { } bausteinId ? null
            : await db.Textbausteine.AsNoTracking().Where(t => t.Id == bausteinId && t.Aktiv && t.EntferntAm == null)
                .Select(t => new { t.Id, t.Betreff, t.InhaltHtml }).FirstOrDefaultAsync(ct);
        if (daten.TextbausteinId is not null && baustein is null)
            return BadRequest("Diesen Textbaustein gibt es nicht (mehr).");

        var hinweise = new List<string>();
        var (an, anFalsch) = MailRegeln.Adressen(daten.An);
        var (cc, ccFalsch) = MailRegeln.Adressen(daten.Cc);
        if (anFalsch.Count + ccFalsch.Count > 0)
            hinweise.Add($"Keine gültige Adresse: {string.Join(", ", anFalsch.Concat(ccFalsch))}");
        if (an.Count + cc.Count > MailRegeln.MaxEmpfaenger)
            return BadRequest($"Zu viele Empfänger (höchstens {MailRegeln.MaxEmpfaenger}).");

        var signatur = daten.MitSignatur && !string.IsNullOrWhiteSpace(ich.MailSignaturHtml) ? ich.MailSignaturHtml : null;
        if (daten.MitSignatur && signatur is null)
            hinweise.Add("Es ist noch keine Signatur hinterlegt — die Mail geht ohne.");
        var werte = MailRegeln.Werte(kunde.Firmenname, kunde.Kundennummer,
            ansprechpartner is null ? null : $"{ansprechpartner.Vorname} {ansprechpartner.Nachname}", ansprechpartner?.Email,
            ich.Anzeigename, EchteAdresse(ich.Email), signatur);

        var betreff = MailRegeln.Fuellen(string.IsNullOrWhiteSpace(daten.Betreff) ? baustein?.Betreff : daten.Betreff, werte, html: false).Trim();
        // Setzt der Baustein die Signatur selbst ein, kommt sie nicht noch einmal ans Ende
        var html = OdooMailvorlage.Saeubern(MailRegeln.Zusammensetzen(
            daten.Freitext,
            MailRegeln.Fuellen(baustein?.InhaltHtml, werte, html: true),
            MailRegeln.EnthaeltSignatur(baustein?.InhaltHtml) ? null : signatur));

        var umleitung = await UmleitungAsync(ich, ct);
        var anWirklich = MailRegeln.Empfaenger(an, umleitung.Aktiv, umleitung.Adresse);
        if (umleitung.Aktiv && an.Count > 0 && anWirklich.Count > 0)
            hinweise.Add($"Diese Mail geht an {anWirklich[0]} statt an {string.Join(", ", an.Concat(cc))}.");

        var anhaenge = baustein is null ? [] : await db.TextbausteinAnhaenge.AsNoTracking()
            .Where(a => a.TextbausteinId == baustein.Id)
            .OrderBy(a => a.Dateiname)
            .Select(a => new MailAnhang(a.Id, a.Dateiname, a.Groesse))
            .ToListAsync(ct);
        return Ok(new MailFertig(anWirklich, umleitung.Aktiv ? [] : cc, betreff, html,
            MailRegeln.Offene(betreff, html), anhaenge, hinweise));
    }

    /// <summary>
    /// Der Browser meldet: Die Mail liegt im Postfach. Erst jetzt entstehen Ausgangsmail und
    /// Eintrag im Verlauf — „E-Mail vorbereitet“ für einen Entwurf, die Mail selbst, wenn sie
    /// schon gesendet ist.
    /// </summary>
    [HttpPost("ausgang")]
    public async Task<ActionResult<int>> Angelegt(MailAngelegt daten, CancellationToken ct)
    {
        var ich = await IchAsync(ct);
        if (ich is null)
            return Forbid();
        if (Pruefen(daten.An, daten.Cc, daten.Betreff, daten.Html, await UmleitungAsync(ich, ct)) is { } fehler)
            return BadRequest(fehler);
        if (string.IsNullOrWhiteSpace(daten.GraphId) || daten.GraphId.Length > 1000)
            return BadRequest("Die Kennung der Nachricht im Postfach fehlt.");
        if (!await db.Kunden.AnyAsync(k => k.Id == daten.KundeId, ct))
            return NotFound();
        if (daten.AnsprechpartnerId is { } apId && !await db.Ansprechpartner.AnyAsync(a => a.Id == apId && a.KundeId == daten.KundeId, ct))
            return BadRequest("Der Ansprechpartner gehört nicht zu diesem Kunden.");
        if (await db.Ausgangsmails.AnyAsync(m => m.MitarbeiterId == ich.Id && m.GraphId == daten.GraphId, ct))
            return Conflict("Diese Mail ist bereits festgehalten.");

        var jetzt = DateTime.UtcNow;
        var html = OdooMailvorlage.Saeubern(daten.Html);
        var anhaenge = (daten.Anhaenge ?? []).Select(a => Dateinamen.Sicher(a, 200, "anhang")).Take(50).ToList();
        var mail = new Ausgangsmail
        {
            KundeId = daten.KundeId,
            MitarbeiterId = ich.Id,
            AnsprechpartnerId = daten.AnsprechpartnerId,
            An = Liste(daten.An)!,
            Cc = Liste(daten.Cc),
            Betreff = daten.Betreff.Trim(),
            InhaltHtml = html,
            Anhaenge = anhaenge.Count > 0 ? VorgangsHilfen.Kuerzen(string.Join("; ", anhaenge), 2000) : null,
            TextbausteinId = daten.TextbausteinId,
            Kanal = MailKanal.Postfach,
            GraphId = daten.GraphId,
            NachrichtenKennung = Kennung(daten.NachrichtenKennung),
            Status = daten.Gesendet ? AusgangsmailStatus.Gesendet : AusgangsmailStatus.Entwurf,
            AngelegtAm = jetzt,
            GesendetAm = daten.Gesendet ? jetzt : null,
            Aktivitaet = new Aktivitaet
            {
                KundeId = daten.KundeId,
                Typ = AktivitaetsTyp.EMail,
                Zeitpunkt = jetzt,
                MitarbeiterId = ich.Id,
                AnsprechpartnerId = daten.AnsprechpartnerId,
                Richtung = AktivitaetsRichtung.Ausgehend,
                Betreff = "",
            },
        };
        InVerlauf(mail);
        db.Ausgangsmails.Add(mail);
        await db.SaveChangesAsync(ct);
        return Ok(mail.Id);
    }

    /// <summary>Eigene Entwürfe, von denen die Kundenzentrale noch nicht weiß, ob sie gesendet sind.</summary>
    [HttpGet("ausgang/offen")]
    public async Task<ActionResult<List<MailOffen>>> Offen([FromQuery] int? kundeId, CancellationToken ct)
    {
        if (await angemeldet.IdAsync(ct) is not { } ich)
            return Forbid();
        return Ok(await db.Ausgangsmails.AsNoTracking()
            .Where(m => m.MitarbeiterId == ich && m.Status == AusgangsmailStatus.Entwurf && m.GraphId != null
                        && (kundeId == null || m.KundeId == kundeId))
            .OrderByDescending(m => m.AngelegtAm)
            .Take(50)
            .Select(m => new MailOffen(m.Id, m.GraphId!, m.KundeId, m.Kunde!.Firmenname, m.Betreff, m.An, m.AngelegtAm))
            .ToListAsync(ct));
    }

    /// <summary>
    /// Der Browser hat im Postfach nachgesehen, was aus dem Entwurf wurde. Gesendet: Die wirklich
    /// gesendete Fassung (mit den letzten Änderungen aus Outlook) ersetzt „E-Mail vorbereitet“.
    /// Gelöscht: Der Eintrag verschwindet — ein nie gesendeter Entwurf steht nicht im Verlauf.
    /// </summary>
    [HttpPost("ausgang/{id:int}/stand")]
    public async Task<ActionResult<string>> Stand(int id, MailStand daten, CancellationToken ct)
    {
        if (await angemeldet.IdAsync(ct) is not { } ich)
            return Forbid();
        var mail = await db.Ausgangsmails.Include(m => m.Aktivitaet).FirstOrDefaultAsync(m => m.Id == id && m.MitarbeiterId == ich, ct);
        if (mail is null)
            return NotFound();
        if (mail.Status != AusgangsmailStatus.Entwurf)
            return Ok(mail.Status.ToString());

        switch (daten.Stand)
        {
            case "gesendet":
                if (daten.An is { Count: > 0 } && Pruefen(daten.An, daten.Cc, daten.Betreff ?? mail.Betreff, daten.Html ?? mail.InhaltHtml, null) is { } fehler)
                    return BadRequest(fehler);
                mail.Status = AusgangsmailStatus.Gesendet;
                mail.GesendetAm = daten.GesendetAm is { } am && am > mail.AngelegtAm.AddMinutes(-5) && am < DateTime.UtcNow.AddMinutes(5)
                    ? am.ToUniversalTime() : DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(daten.Betreff)) mail.Betreff = VorgangsHilfen.Kuerzen(daten.Betreff.Trim(), 500);
                if (!string.IsNullOrWhiteSpace(daten.Html)) mail.InhaltHtml = OdooMailvorlage.Saeubern(daten.Html);
                if (daten.An is { Count: > 0 })
                {
                    mail.An = Liste(daten.An)!;
                    mail.Cc = Liste(daten.Cc);
                }
                mail.NachrichtenKennung = Kennung(daten.NachrichtenKennung) ?? mail.NachrichtenKennung;
                mail.Aktivitaet ??= new Aktivitaet
                {
                    KundeId = mail.KundeId, Typ = AktivitaetsTyp.EMail, MitarbeiterId = mail.MitarbeiterId,
                    AnsprechpartnerId = mail.AnsprechpartnerId, Richtung = AktivitaetsRichtung.Ausgehend, Betreff = "",
                };
                InVerlauf(mail);
                break;
            case "geloescht":
                mail.Status = AusgangsmailStatus.Verworfen;
                if (mail.Aktivitaet is { } eintrag)
                {
                    mail.Aktivitaet = null;
                    db.Aktivitaeten.Remove(eintrag);
                }
                break;
            case "entwurf":
                return Ok(mail.Status.ToString());
            default:
                return BadRequest("Unbekannter Stand.");
        }
        await db.SaveChangesAsync(ct);
        return Ok(mail.Status.ToString());
    }

    // --- Signatur ----------------------------------------------------------------------

    [HttpGet("signatur")]
    public async Task<ActionResult<MailSignatur>> Signatur(CancellationToken ct) =>
        await IchAsync(ct) is { } ich ? Ok(new MailSignatur(ich.MailSignaturHtml ?? "")) : Forbid();

    /// <summary>Eigene Signatur speichern — gesäubert wie jeder fremde Text.</summary>
    [HttpPut("signatur")]
    public async Task<IActionResult> SignaturSpeichern(MailSignatur daten, CancellationToken ct)
    {
        if ((daten.Html ?? "").Length > MailRegeln.MaxSignatur)
            return BadRequest("Die Signatur ist zu groß — eingebettete Bilder bitte weglassen oder verlinken.");
        if (await angemeldet.IdAsync(ct) is not { } id || await db.Mitarbeiter.FirstOrDefaultAsync(m => m.Id == id, ct) is not { } ich)
            return Forbid();
        var sauber = OdooMailvorlage.Saeubern(daten.Html).Trim();
        ich.MailSignaturHtml = sauber.Length == 0 ? null : sauber;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Holt die in Odoo hinterlegte Signatur des angemeldeten Mitarbeiters — zum Ansehen, gespeichert wird erst mit „Speichern“.</summary>
    [HttpPost("signatur/odoo")]
    public async Task<ActionResult<MailSignaturAusOdoo>> SignaturAusOdoo(
        [FromServices] IServiceScopeFactory scopeFactory, [FromServices] IConfiguration konfiguration,
        [FromServices] ILogger<MailController> log, CancellationToken ct)
    {
        var ich = await IchAsync(ct);
        if (EchteAdresse(ich?.Email) is not { } adresse)
            return Ok(new MailSignaturAusOdoo(null, "An deinem Benutzer ist keine E-Mail-Adresse hinterlegt — darüber wird der Odoo-Benutzer gesucht."));
        try
        {
            var (odoo, _) = await OdooImportDienst.VerbindenAsync(scopeFactory, konfiguration);
            var html = OdooMailvorlage.Saeubern(await BenutzerSignatur.LadenAsync(odoo, adresse).WaitAsync(TimeSpan.FromSeconds(25), ct)).Trim();
            return Ok(html.Length == 0
                ? new MailSignaturAusOdoo(null, $"In Odoo ist für {adresse} keine Signatur hinterlegt.")
                : html.Length > MailRegeln.MaxSignatur
                    ? new MailSignaturAusOdoo(null, "Die Signatur in Odoo ist zu groß (vermutlich ein eingebettetes Bild).")
                    : new MailSignaturAusOdoo(html, null));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Odoo-Zugangsdaten unvollständig"))
        {
            return Ok(new MailSignaturAusOdoo(null, "Odoo ist nicht eingerichtet (Administration → Odoo-Anbindung)."));
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TimeoutException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            log.LogWarning("Signatur: Odoo nicht lesbar ({Art})", ex is OdooFehlerException o ? o.Art : ex.GetType().Name);
            return Ok(new MailSignaturAusOdoo(null, "Odoo ist nicht erreichbar oder gibt die Signatur nicht heraus."));
        }
    }

    // --- Hilfen ------------------------------------------------------------------------

    private sealed record Umleitung(bool Aktiv, string? Adresse, string? Hinweis);

    /// <summary>
    /// Gehen Mails an den Kunden — oder woandershin? Das Testsystem leitet immer an seine feste
    /// Adresse um. Sonst gilt der Schalter „Kundenmails freigegeben“ (Administration →
    /// Outlook-Anbindung): Solange er nicht auf „Ja“ steht, geht jede Mail an den Mitarbeiter selbst.
    /// </summary>
    private async Task<Umleitung> UmleitungAsync(Mitarbeiter? ich, CancellationToken ct)
    {
        if (instanz.Testsystem)
        {
            var fest = MailRegeln.Adressen(instanz.MailUmleitung).Gueltig.FirstOrDefault();
            return new Umleitung(true, fest, fest is null
                ? "Testsystem: Es ist keine Umleitungsadresse eingetragen — es kann nichts angelegt werden."
                : $"Testsystem: Mails gehen an {fest}, nie an den Kunden.");
        }
        var schalter = await db.Einstellungen.AsNoTracking()
            .Where(e => e.Schluessel == EinstellungsSchluessel.OutlookKundenmailsFreigegeben)
            .Select(e => e.Wert).FirstOrDefaultAsync(ct);
        if (MailRegeln.Freigegeben(schalter))
            return new Umleitung(false, null, null);
        var eigene = EchteAdresse(ich?.Email);
        return new Umleitung(true, eigene, eigene is null
            ? "Probebetrieb: Kundenmails sind noch nicht freigegeben, und an deinem Benutzer ist keine E-Mail-Adresse hinterlegt — es kann nichts angelegt werden."
            : $"Probebetrieb: Kundenmails sind noch nicht freigegeben. Jede Mail geht an dich selbst ({eigene}), nicht an den Kunden. Freigeben kann ein Administrator unter Administration → Outlook-Anbindung.");
    }

    /// <summary>Schreibt den Stand der Mail in ihren Verlaufseintrag.</summary>
    private static void InVerlauf(Ausgangsmail mail)
    {
        var eintrag = mail.Aktivitaet!;
        var gesendet = mail.Status == AusgangsmailStatus.Gesendet;
        eintrag.Zeitpunkt = mail.GesendetAm ?? mail.AngelegtAm;
        eintrag.Betreff = VorgangsHilfen.Kuerzen(gesendet ? mail.Betreff : $"E-Mail vorbereitet: {mail.Betreff}", 300);
        eintrag.Empfaenger = VorgangsHilfen.Kuerzen(mail.Cc is null ? mail.An : $"{mail.An}; Cc: {mail.Cc}", 300);
        eintrag.Inhalt =
            (gesendet ? "" : "Der Entwurf liegt in Outlook und ist noch nicht gesendet.\n\n") +
            ChatterFilter.Klartext(mail.InhaltHtml) +
            (mail.Anhaenge is null ? "" : $"\n\nAnhänge: {mail.Anhaenge.Replace("; ", ", ")}");
    }

    /// <param name="umleitung">Gesetzt beim Anlegen: Mit aktiver Umleitung hält die Kundenzentrale keine Mail an eine andere Adresse fest.</param>
    private static string? Pruefen(List<string>? an, List<string>? cc, string? betreff, string? html, Umleitung? umleitung)
    {
        var alle = (an ?? []).Concat(cc ?? []).ToList();
        if (an is not { Count: > 0 })
            return "Es fehlt ein Empfänger.";
        if (alle.Count > MailRegeln.MaxEmpfaenger)
            return $"Zu viele Empfänger (höchstens {MailRegeln.MaxEmpfaenger}).";
        if (alle.Any(a => MailRegeln.Adressen(a).Gueltig.Count != 1))
            return "Mindestens eine Empfängeradresse ist ungültig.";
        // Testsystem und Probebetrieb halten nie eine Mail an einen Kunden fest — dort gibt es nur die Umleitung
        if (umleitung is { Aktiv: true }
            && (umleitung.Adresse is null || alle.Any(a => !a.Trim().Equals(umleitung.Adresse, StringComparison.OrdinalIgnoreCase))))
            return "Kundenmails sind hier nicht freigegeben — Mails gehen nur an die Umleitungsadresse.";
        if (string.IsNullOrWhiteSpace(betreff))
            return "Der Betreff fehlt.";
        if (betreff.Trim().Length > 500)
            return "Der Betreff ist zu lang (höchstens 500 Zeichen).";
        if ((html ?? "").Length > MaxHtml)
            return "Der Text der Mail ist zu groß.";
        return null;
    }

    /// <summary>Adressen als Text für die Ablage — nie länger als die Spalte.</summary>
    private static string? Liste(List<string>? adressen) =>
        adressen is { Count: > 0 } ? VorgangsHilfen.Kuerzen(string.Join("; ", adressen.Select(a => a.Trim())), 2000) : null;

    private static string? Kennung(string? wert) =>
        string.IsNullOrWhiteSpace(wert) ? null : VorgangsHilfen.Kuerzen(wert.Trim(), 1000);

    /// <summary>Automatisch angelegte Konten haben eine Platzhalter-Adresse — die gehört in keine Mail.</summary>
    private static string? EchteAdresse(string? adresse) =>
        MailRegeln.Adressen(adresse).Gueltig.FirstOrDefault() is { } a
        && !a.EndsWith("@ad.lokal", StringComparison.Ordinal) && !a.EndsWith("@localhost", StringComparison.Ordinal) ? a : null;

    private async Task<Mitarbeiter?> IchAsync(CancellationToken ct) =>
        await angemeldet.IdAsync(ct) is { } id ? await db.Mitarbeiter.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct) : null;
}
