using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rausoft.Erp.Api.Dienste;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Offene Angebote und Aufträge aus Odoo (ANFORDERUNGEN.md): Übersicht, Ausblenden im
/// Einzelfall, Nachfass-Aufgaben auf Knopfdruck. Ohne Preise. Zugriff: jeder aktive
/// Mitarbeiter — die Liste soll gerade dafür sorgen, dass jemand den Vorgang startet.
/// </summary>
[ApiController]
[Route("api/verkauf")]
public partial class VerkaufController(VerkaufsUeberwachung dienst, AngemeldeterMitarbeiter angemeldet) : ControllerBase
{
    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9/\-_.]{1,29}$")]
    private static partial Regex NummerMuster();

    [HttpGet("offen")]
    public async Task<ActionResult<VerkaufUebersicht>> Offen(CancellationToken ct) => Ok(await dienst.UebersichtAsync(ct));

    [HttpPost("ausblenden")]
    public async Task<IActionResult> Ausblenden(VerkaufAusblenden daten, CancellationToken ct)
    {
        if (Nummer(daten.Nummer) is not { } nummer)
            return BadRequest("Das ist keine Odoo-Nummer.");
        await dienst.AusblendenAsync(nummer, daten.Grund, await angemeldet.IdAsync(ct), ct);
        return NoContent();
    }

    [HttpPost("einblenden")]
    public async Task<IActionResult> Einblenden(VerkaufAusblenden daten, CancellationToken ct)
    {
        if (Nummer(daten.Nummer) is not { } nummer)
            return BadRequest("Das ist keine Odoo-Nummer.");
        await dienst.EinblendenAsync(nummer, ct);
        return NoContent();
    }

    /// <summary>Nachfass-Aufgaben jetzt prüfen (läuft sonst stündlich).</summary>
    [HttpPost("nachfassen")]
    public async Task<ActionResult<VerkaufLaufErgebnis>> Nachfassen(CancellationToken ct) => Ok(await dienst.NachfassenAsync(ct));

    /// <summary>Wochenübersicht sofort senden — zum Prüfen der Empfänger.</summary>
    [HttpPost("bericht")]
    [Authorize(Policy = "Admin")]
    public async Task<ActionResult<string>> Bericht(CancellationToken ct) =>
        Ok(await dienst.WochenberichtAsync(erzwingen: true, ct) ?? "Wochenübersicht gesendet.");

    private static string? Nummer(string? text) =>
        text?.Trim().ToUpperInvariant() is { } n && NummerMuster().IsMatch(n) ? n : null;
}
