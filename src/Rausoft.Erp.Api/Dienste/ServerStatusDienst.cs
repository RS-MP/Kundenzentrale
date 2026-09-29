using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Server-Status-Meldung ans ID.prove Service Portal (Anleitung siehe
/// ANLEITUNG-KUNDENZENTRALE-SERVERSTATUS.md): Healthchecks der internen
/// Server abfragen und NUR die Ergebnisse (Alias + Status) pushen.
/// Sicherheitsmodell: interne URLs, Mandanten-Kennungen und Kundenbezüge
/// verlassen die Kundenzentrale nie — Fehlertexte sind feste, generische Texte.
/// </summary>
public class ServerStatusDienst(
    ErpDbContext db,
    IHttpClientFactory httpFactory,
    GeheimnisDienst geheimnisse,
    ILogger<ServerStatusDienst> log)
{
    /// <summary>Fragt die Healthchecks aller aktiven Server ab und speichert die Ergebnisse.</summary>
    public async Task<int> PruefeAlleAsync(CancellationToken ct = default)
    {
        var server = await db.Server.Where(s => s.Aktiv).OrderBy(s => s.Alias).ToListAsync(ct);
        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(15);

        foreach (var eintrag in server)
        {
            eintrag.GeprueftAm = DateTime.UtcNow;
            try
            {
                var url = $"{eintrag.Url.TrimEnd('/')}/api/HealthCheck?customer={Uri.EscapeDataString(eintrag.PruefMandant)}";
                using var antwort = await http.GetAsync(url, ct);
                if (!antwort.IsSuccessStatusCode)
                {
                    Setze(eintrag, false, false, null, $"HTTP {(int)antwort.StatusCode}");
                    continue;
                }
                using var json = JsonDocument.Parse(await antwort.Content.ReadAsStringAsync(ct));
                var wurzel = json.RootElement;
                Setze(eintrag,
                    wurzel.TryGetProperty("isAlive", out var alive) && alive.ValueKind == JsonValueKind.True,
                    wurzel.TryGetProperty("iDproveConnectorActive", out var conn) && conn.ValueKind == JsonValueKind.True,
                    wurzel.TryGetProperty("sanctionListDate", out var datum) && datum.ValueKind == JsonValueKind.String
                        ? Kuerzen(datum.GetString(), 50) : null,
                    wurzel.TryGetProperty("errorMessage", out var fehler) && fehler.ValueKind == JsonValueKind.String
                        ? Kuerzen(fehler.GetString(), 500) : null);
            }
            // Nur generische Texte — Exception-Meldungen könnten interne Hostnamen enthalten
            catch (TaskCanceledException) { Setze(eintrag, false, false, null, "Timeout nach 15s"); }
            catch (HttpRequestException) { Setze(eintrag, false, false, null, "Verbindungsfehler"); }
            catch (JsonException) { Setze(eintrag, false, false, null, "Ungültige Antwort"); }
        }

        await db.SaveChangesAsync(ct);
        return server.Count;

        static void Setze(Domain.Servers.Server s, bool erreichbar, bool connector, string? liste, string? fehler)
        {
            s.IstErreichbar = erreichbar;
            s.ConnectorAktiv = connector;
            s.SanktionslistenStand = liste;
            s.Fehlertext = string.IsNullOrWhiteSpace(fehler) ? null : fehler;
        }
    }

    /// <summary>Pusht die zuletzt ermittelten Status ALLER aktiven Server als Batch ans Portal.</summary>
    public async Task<(List<string> Verarbeitet, List<string> Unbekannt)> PushAsync(CancellationToken ct = default)
    {
        var token = await geheimnisse.HolenAsync(GeheimnisSchluessel.ServicePortalPushToken, ct)
            ?? throw new InvalidOperationException(
                "Der Push-Token des Service Portals ist nicht gesetzt (Administration → Geheimnisse). " +
                "Er steht im Portal unter Admin → Einstellungen → Server-Status (Push-API).");

        var portalUrl = (await db.Einstellungen.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Schluessel == EinstellungsSchluessel.ServicePortalUrl, ct))?.Wert;
        if (string.IsNullOrWhiteSpace(portalUrl))
            throw new InvalidOperationException("Die Service-Portal-URL ist nicht konfiguriert.");

        var server = await db.Server.AsNoTracking()
            .Where(s => s.Aktiv).OrderBy(s => s.Alias).ToListAsync(ct);
        if (server.Count == 0)
            throw new InvalidOperationException("Keine aktiven Server angelegt (Administration → Server).");

        // Es verlassen NUR Alias + Statuswerte das Haus
        var meldungen = server.Select(s => new
        {
            server = s.Alias,
            isAlive = s.IstErreichbar ?? false,
            iDproveConnectorActive = s.ConnectorAktiv ?? false,
            sanctionListDate = s.SanktionslistenStand ?? "",
            errorMessage = s.Fehlertext ?? "",
        });

        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);
        // Basis-URL erwartet — wurde versehentlich der volle Endpunkt eingetragen,
        // nicht doppelt anhängen (sonst 404, das wie ein falscher Token aussieht)
        var basisUrl = portalUrl.Trim().TrimEnd('/');
        const string endpunkt = "/api/server-status";
        if (basisUrl.EndsWith(endpunkt, StringComparison.OrdinalIgnoreCase))
            basisUrl = basisUrl[..^endpunkt.Length];
        using var request = new HttpRequestMessage(HttpMethod.Post, basisUrl + endpunkt);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(JsonSerializer.Serialize(meldungen), Encoding.UTF8, "application/json");

        using var antwort = await http.SendAsync(request, ct);
        if (antwort.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException(
                "Das Portal meldet 404 — meist ist der Push-Token falsch (das Portal antwortet bei ungültigem Token bewusst mit 404). " +
                "Außerdem prüfen: ServicePortal.Url muss die Basis-Adresse sein, z. B. https://serviceportal.idprove.de.");
        antwort.EnsureSuccessStatusCode();

        using var ergebnis = JsonDocument.Parse(await antwort.Content.ReadAsStringAsync(ct));
        var verarbeitet = Liste(ergebnis.RootElement, "verarbeitet");
        var unbekannt = Liste(ergebnis.RootElement, "unbekannt");
        if (unbekannt.Count > 0)
            log.LogWarning("Service Portal kennt folgende Server-Aliase nicht: {Aliase}", string.Join(", ", unbekannt));

        // Optional (siehe ANLEITUNG-SERVICEPORTAL-RELEASE-ANTWORT.md): das Portal
        // liefert je Alias den dort gepflegten Release-Stand zurück — übernehmen.
        if (ergebnis.RootElement.TryGetProperty("releases", out var releases)
            && releases.ValueKind == JsonValueKind.Object)
        {
            var nachAlias = await db.Server.Where(s => s.Aktiv).ToDictionaryAsync(s => s.Alias, ct);
            foreach (var eintrag in releases.EnumerateObject())
            {
                if (nachAlias.TryGetValue(eintrag.Name, out var passend)
                    && eintrag.Value.ValueKind == JsonValueKind.String)
                    passend.ReleaseStand = Kuerzen(eintrag.Value.GetString(), 50);
            }
        }

        await SchreibeLetztenPush(verarbeitet.Count, unbekannt, ct);
        return (verarbeitet, unbekannt);

        static List<string> Liste(JsonElement wurzel, string name) =>
            wurzel.TryGetProperty(name, out var feld) && feld.ValueKind == JsonValueKind.Array
                ? feld.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList()
                : [];
    }

    private async Task SchreibeLetztenPush(int verarbeitet, List<string> unbekannt, CancellationToken ct)
    {
        var text = $"{DateTime.Now:dd.MM.yyyy HH:mm} · verarbeitet: {verarbeitet}" +
                   (unbekannt.Count > 0 ? $" · UNBEKANNT: {string.Join(", ", unbekannt)}" : "");
        var eintrag = await db.Einstellungen
            .FirstOrDefaultAsync(e => e.Schluessel == EinstellungsSchluessel.ServicePortalLetzterPush, ct);
        if (eintrag is null)
        {
            db.Einstellungen.Add(new Einstellung
            {
                Schluessel = EinstellungsSchluessel.ServicePortalLetzterPush,
                Wert = text,
                Beschreibung = "Letzte Server-Status-Meldung ans Portal (wird automatisch gepflegt).",
            });
        }
        else
        {
            eintrag.Wert = text;
        }
        await db.SaveChangesAsync(ct);
    }

    private static string? Kuerzen(string? text, int max) =>
        text is null ? null : text.Length <= max ? text : text[..max];
}
