using System.Text.Json;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.OdooImport;

/// <summary>Mailvorlage aus Odoo (mail.template) im Rohzustand — Umsetzung: <c>OdooMailvorlage</c>.</summary>
/// <param name="Anhaenge">Dateien, die Odoo mit dieser Vorlage verschickt (ohne Inhalt).</param>
public sealed record OdooMailVorlage(
    int Id, string Name, string? Betreff, string? BodyHtml, string? Modell, DateTime? Stand,
    IReadOnlyList<OdooAnhangInfo> Anhaenge);

/// <param name="AnhaengeLesbar">Falsch, wenn der Odoo-Benutzer der Anbindung die Anhänge nicht lesen darf — dann fehlen sie, die Texte sind trotzdem da.</param>
public sealed record OdooMailVorlagen(List<OdooMailVorlage> Vorlagen, bool AnhaengeLesbar);

/// <summary>
/// Liest alle Mailvorlagen aus Odoo, in der deutschen Fassung. Nur lesend. Der Text kommt als
/// HTML (body_html) und behält damit seine Formatierung. Feste Anhänge der Vorlage
/// (attachment_ids, z. B. eine Infomappe) werden mit Name, Größe und Prüfsumme gelesen; den
/// Inhalt holt erst die Übernahme (<see cref="InhaltAsync"/>). Berichte, die Odoo beim Senden
/// frisch erzeugt (das Angebot als PDF), sind keine festen Anhänge und bleiben in Odoo.
/// </summary>
public static class MailVorlagenLeser
{
    public const int MaxVorlagen = 1000;

    public static async Task<OdooMailVorlagen> LadenAsync(IOdooAufruf odoo)
    {
        var vorlagen = await odoo.AusfuehrenAsync("mail.template", "search_read", [Array.Empty<object>()],
            new Dictionary<string, object>
            {
                ["fields"] = new[] { "id", "name", "subject", "body_html", "model", "write_date", "attachment_ids" },
                ["order"] = "model, name, id",
                ["limit"] = MaxVorlagen,
                ["context"] = new Dictionary<string, object> { ["lang"] = "de_DE" },
            });
        if (vorlagen.ValueKind != JsonValueKind.Array)
            return new OdooMailVorlagen([], true);
        var zeilen = vorlagen.EnumerateArray().ToList();

        var (anhaenge, lesbar) = await AnhaengeAsync(odoo, zeilen.SelectMany(v => OdooJson.Ids(v, "attachment_ids")).Distinct().ToArray());
        return new OdooMailVorlagen(zeilen
            .Select(v => new OdooMailVorlage(
                v.GetProperty("id").GetInt32(),
                OdooJson.Text(v, "name") ?? "(ohne Namen)",
                OdooJson.Text(v, "subject"),
                OdooJson.Text(v, "body_html"),
                OdooJson.Text(v, "model"),
                OdooJson.ZeitpunktUtc(v, "write_date"),
                OdooJson.Ids(v, "attachment_ids").Where(anhaenge.ContainsKey).Select(id => anhaenge[id]).ToList()))
            .ToList(), lesbar);
    }

    /// <summary>
    /// Beschreibung der Anhänge. Gesucht statt gelesen: Ein einzelner nicht lesbarer Anhang
    /// ließe sonst den ganzen Aufruf scheitern. Verweigert Odoo den Zugriff insgesamt, kommen
    /// die Vorlagen ohne Anhänge zurück.
    /// </summary>
    private static async Task<(Dictionary<int, OdooAnhangInfo> Anhaenge, bool Lesbar)> AnhaengeAsync(IOdooAufruf odoo, int[] ids)
    {
        var anhaenge = new Dictionary<int, OdooAnhangInfo>();
        if (ids.Length == 0)
            return (anhaenge, true);
        JsonElement antwort;
        try
        {
            antwort = await odoo.AusfuehrenAsync("ir.attachment", "search_read", [new object[] { new object[] { "id", "in", ids } }],
                new Dictionary<string, object> { ["fields"] = new[] { "id", "name", "mimetype", "file_size", "checksum", "type" } });
        }
        catch (OdooFehlerException)
        {
            return (anhaenge, false);
        }
        if (antwort.ValueKind == JsonValueKind.Array)
            foreach (var a in antwort.EnumerateArray())
            {
                var id = a.GetProperty("id").GetInt32();
                anhaenge[id] = new OdooAnhangInfo(
                    id,
                    OdooJson.Text(a, "name") ?? $"Anhang {id}",
                    OdooJson.Text(a, "mimetype"),
                    a.TryGetProperty("file_size", out var g) && g.ValueKind == JsonValueKind.Number ? g.GetInt64() : 0,
                    OdooJson.Text(a, "checksum"),
                    // type „url“ ist nur ein Link; alles andere (binary) ist eine Datei
                    OdooJson.Text(a, "type") != "url");
            }
        return (anhaenge, true);
    }

    /// <summary>Inhalt eines Anhangs (Odoo liefert Binärfelder Base64-kodiert); leer, wenn Odoo keinen Inhalt hat.</summary>
    public static async Task<byte[]> InhaltAsync(IOdooAufruf odoo, int anhangId)
    {
        var liste = await odoo.AusfuehrenAsync("ir.attachment", "read", [new[] { anhangId }],
            new Dictionary<string, object> { ["fields"] = new[] { "datas" } });
        if (liste.ValueKind != JsonValueKind.Array || liste.GetArrayLength() == 0
            || !liste[0].TryGetProperty("datas", out var daten) || daten.ValueKind != JsonValueKind.String)
            return [];
        try
        {
            return Convert.FromBase64String(daten.GetString()!);
        }
        catch (FormatException)
        {
            return [];
        }
    }
}
