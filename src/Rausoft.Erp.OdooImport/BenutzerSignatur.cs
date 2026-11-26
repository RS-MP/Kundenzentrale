using System.Text.Json;

namespace Rausoft.Erp.OdooImport;

/// <summary>
/// E-Mail-Signatur eines Odoo-Benutzers (res.users.signature, HTML) — gesucht über die
/// Anmeldung, die bei rausoft die E-Mail-Adresse ist. Nur lesend; gesäubert wird beim Speichern.
/// </summary>
public static class BenutzerSignatur
{
    public static async Task<string?> LadenAsync(IOdooAufruf odoo, string login)
    {
        if (string.IsNullOrWhiteSpace(login))
            return null;
        var benutzer = await odoo.AusfuehrenAsync("res.users", "search_read",
            [new object[] { new object[] { "login", "=ilike", login.Trim() } }],
            new Dictionary<string, object> { ["fields"] = new[] { "signature" }, ["limit"] = 1 });
        return benutzer.ValueKind == JsonValueKind.Array && benutzer.GetArrayLength() > 0
            ? OdooJson.Text(benutzer[0], "signature")
            : null;
    }
}
