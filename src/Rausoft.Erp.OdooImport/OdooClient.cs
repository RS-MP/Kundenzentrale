using System.Text;
using System.Text.Json;

namespace Rausoft.Erp.OdooImport;

/// <summary>
/// Ein Aufruf an ein Odoo-Modell (execute_kw). Schreibende Funktionen arbeiten gegen diese
/// Schnittstelle, damit ihre Regeln ohne echtes Odoo prüfbar sind.
/// </summary>
public interface IOdooAufruf
{
    Task<JsonElement> AusfuehrenAsync(string modell, string methode, object[] args, Dictionary<string, object>? kwargs = null);
}

/// <summary>
/// Odoo hat einen Aufruf mit einem Fehler beantwortet. <see cref="Art"/> ist der Name der
/// Odoo-Ausnahme (z. B. <c>odoo.exceptions.AccessError</c>) — er lässt sich gefahrlos
/// protokollieren und einer festen Meldung zuordnen. Die <see cref="Exception.Message"/>
/// enthält Odoos ganze Antwort und gehört weder in die Oberfläche noch ins Log.
/// </summary>
public class OdooFehlerException(string? art, string meldung) : InvalidOperationException(meldung)
{
    public string? Art { get; } = art;
}

/// <summary>
/// Minimaler JSON-RPC-Client für die Odoo External API (/jsonrpc).
/// Authentifizierung per Benutzer + API-Key (der Key wirkt als Passwort-Ersatz).
/// Der Key kommt ausschließlich aus einer Umgebungsvariablen — nie aus Dateien.
/// </summary>
public class OdooClient(string url, string db, string benutzer, string apiKey) : IOdooAufruf
{
    private static readonly HttpClient Http = new();
    private int _uid;
    private int _requestId;

    public async Task<Dictionary<string, object?>> VersionAsync()
    {
        var result = await RpcAsync("common", "version", []);
        return result.Deserialize<Dictionary<string, object?>>() ?? [];
    }

    public async Task<int> AnmeldenAsync()
    {
        var result = await RpcAsync("common", "authenticate", [db, benutzer, apiKey, new Dictionary<string, object>()]);
        if (result.ValueKind != JsonValueKind.Number)
            throw new InvalidOperationException(
                "Odoo-Anmeldung fehlgeschlagen — Benutzer/API-Key/Datenbankname prüfen " +
                $"(Odoo antwortete: {(result.ValueKind == JsonValueKind.Undefined ? "kein Ergebnis" : result.GetRawText())}; " +
                $"Benutzer '{benutzer}', Datenbank '{db}', Key-Länge {apiKey.Length}).");
        _uid = result.GetInt32();
        return _uid;
    }

    /// <summary>execute_kw auf einem Odoo-Modell, z. B. res.partner / search_read.</summary>
    public async Task<JsonElement> AusfuehrenAsync(
        string modell, string methode, object[] args, Dictionary<string, object>? kwargs = null)
    {
        if (_uid == 0)
            await AnmeldenAsync();
        return await RpcAsync("object", "execute_kw",
            [db, _uid, apiKey, modell, methode, args, kwargs ?? []]);
    }

    private async Task<JsonElement> RpcAsync(string service, string methode, object[] args)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            method = "call",
            @params = new { service, method = methode, args },
            id = ++_requestId,
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await Http.PostAsync($"{url.TrimEnd('/')}/jsonrpc", content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("error", out var fehler))
            throw new OdooFehlerException(
                fehler.ValueKind == JsonValueKind.Object && fehler.TryGetProperty("data", out var daten)
                && daten.ValueKind == JsonValueKind.Object && daten.TryGetProperty("name", out var name)
                && name.ValueKind == JsonValueKind.String
                    ? name.GetString()
                    : null,
                $"Odoo-Fehler: {fehler.GetRawText()}");

        return doc.RootElement.TryGetProperty("result", out var result)
            ? result.Clone()
            : default;
    }
}
