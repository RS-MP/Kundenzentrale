using System.Text;
using System.Text.Json;

namespace Rausoft.Erp.OdooImport;

/// <summary>
/// Minimaler JSON-RPC-Client für die Odoo External API (/jsonrpc).
/// Authentifizierung per Benutzer + API-Key (der Key wirkt als Passwort-Ersatz).
/// Der Key kommt ausschließlich aus einer Umgebungsvariablen — nie aus Dateien.
/// </summary>
public class OdooClient(string url, string db, string benutzer, string apiKey)
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
                "Odoo-Anmeldung fehlgeschlagen — Benutzer/API-Key/Datenbankname prüfen.");
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
            throw new InvalidOperationException($"Odoo-Fehler: {fehler.GetRawText()}");

        return doc.RootElement.TryGetProperty("result", out var result)
            ? result.Clone()
            : default;
    }
}
