using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rausoft.Erp.Api.Dienste;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Zahlungseingänge aus Kontoauszügen und offene Posten (ANFORDERUNGEN.md, „Kontoauszüge").
/// Zugriff: Rolle Buchhaltung oder Admin — die Liste enthält Beträge und Zahler.
/// </summary>
[ApiController]
[Route("api/zahlungen")]
[Authorize(Policy = BuchhaltungAnforderung.Policy)]
public class ZahlungenController(ZahlungsDienst dienst, AngemeldeterMitarbeiter angemeldet) : ControllerBase
{
    /// <summary>Für den Menüpunkt: Darf der Angemeldete die Zahlungen sehen? Offen für jeden aktiven Mitarbeiter.</summary>
    [HttpGet("/api/zahlungen-zugang")]
    [Authorize]
    public async Task<ActionResult<ZahlungenZugang>> Zugang(CancellationToken ct) =>
        Ok(new ZahlungenZugang(await angemeldet.HatRolleAsync(MitarbeiterRolle.Buchhaltung | MitarbeiterRolle.Admin, ct)));

    [HttpGet]
    public async Task<ActionResult<ZahlungenUebersicht>> Uebersicht(CancellationToken ct) => Ok(await dienst.UebersichtAsync(ct));

    /// <summary>Offene Rechnungen live aus Odoo — eigener Aufruf, damit die Seite nicht auf Odoo wartet.</summary>
    [HttpGet("offene-posten")]
    public async Task<ActionResult<OffenePostenListe>> OffenePosten(CancellationToken ct) => Ok(await dienst.OffenePostenAsync(ct));

    /// <summary>Eingangsordner jetzt einlesen, zuordnen und buchen (läuft sonst alle zwei Minuten).</summary>
    [HttpPost("einlesen")]
    public async Task<ActionResult<ZahlungsLauf>> Einlesen(CancellationToken ct) => Ok(await dienst.EinlesenAsync(ct));

    [HttpPost("{id:int}/zuordnen")]
    public async Task<ActionResult<string>> Zuordnen(int id, UmsatzZuordnen daten, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(daten.Rechnung) || daten.Rechnung.Length > 60)
            return BadRequest("Bitte eine Rechnung wählen.");
        var (ok, meldung) = await dienst.ZuordnenAsync(id, daten.Rechnung, await angemeldet.IdAsync(ct), ct);
        return ok ? Ok(meldung) : BadRequest(meldung);
    }

    [HttpPost("{id:int}/ignorieren")]
    public async Task<IActionResult> Ignorieren(int id, UmsatzIgnorieren daten, CancellationToken ct) =>
        await dienst.IgnorierenAsync(id, daten.Grund, await angemeldet.IdAsync(ct), ct) is { } fehler ? BadRequest(fehler) : NoContent();

    [HttpPost("{id:int}/zuruecksetzen")]
    public async Task<IActionResult> Zuruecksetzen(int id, CancellationToken ct) =>
        await dienst.ZuruecksetzenAsync(id, ct) is { } fehler ? BadRequest(fehler) : NoContent();

    /// <summary>Die Zahlung steht schon in Odoo (von Hand gebucht oder nach einer unklaren Buchung geprüft).</summary>
    [HttpPost("{id:int}/als-gebucht")]
    public async Task<IActionResult> AlsGebucht(int id, CancellationToken ct) =>
        await dienst.AlsGebuchtAsync(id, await angemeldet.IdAsync(ct), ct) is { } fehler ? BadRequest(fehler) : NoContent();

    /// <summary>Übersicht der ausstehenden Zahlungen sofort senden — zum Prüfen der Empfänger.</summary>
    [HttpPost("bericht")]
    public async Task<ActionResult<string>> Bericht(CancellationToken ct) =>
        Ok(await dienst.WochenberichtAsync(erzwingen: true, ct) ?? "Übersicht gesendet.");
}
