using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.OdooImport;

/// <summary>
/// Ermittelt die Odoo-Zugangsdaten. Bevorzugte Quelle ist die Kundenzentrale
/// selbst (Administration → Odoo-Anbindung): URL/Datenbank/Benutzer stehen in
/// den Einstellungen, der API-Key liegt verschlüsselt in der DB und wird über
/// den gemeinsamen Data-Protection-Schlüsselbund der Web-App entschlüsselt —
/// keine Zugangsdaten in Dateien. Umgebungsvariablen (ODOO_URL, ODOO_DB,
/// ODOO_BENUTZER, ODOO_API_KEY) übersteuern einzeln, z. B. auf dem
/// Entwicklungsrechner.
/// </summary>
public static class OdooZugang
{
    public record Ergebnis(string Url, string Db, string Benutzer, string ApiKey);

    public static async Task<Ergebnis?> ErmittelnAsync(bool ausgabe = false)
    {
        var url = Umgebungsvariable("ODOO_URL");
        var db = Umgebungsvariable("ODOO_DB");
        var benutzer = Umgebungsvariable("ODOO_BENUTZER");
        var apiKey = Umgebungsvariable("ODOO_API_KEY");

        if (url is null || db is null || benutzer is null || apiKey is null)
        {
            var verbindung = Umgebungsvariable("KUNDENZENTRALE_DB")
                ?? @"Server=(localdb)\MSSQLLocalDB;Database=Kundenzentrale;Trusted_Connection=True;Encrypt=False";
            var options = new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(verbindung).Options;
            await using var kontext = new ErpDbContext(options);

            var einstellungen = await kontext.Einstellungen.AsNoTracking()
                .Where(e => e.Schluessel.StartsWith("Odoo."))
                .ToDictionaryAsync(e => e.Schluessel, e => e.Wert);
            url ??= Wert(einstellungen, EinstellungsSchluessel.OdooUrl);
            db ??= Wert(einstellungen, EinstellungsSchluessel.OdooDatenbank);
            benutzer ??= Wert(einstellungen, EinstellungsSchluessel.OdooBenutzer);
            apiKey ??= await ApiKeyEntschluesselnAsync(kontext);
        }

        if (ausgabe)
        {
            Console.WriteLine("Zugangsdaten-Prüfung (Quelle: Umgebungsvariable > Administration):");
            Console.WriteLine($"  Odoo-URL:    {(url is null ? "FEHLT" : url)}");
            Console.WriteLine($"  Datenbank:   {(db is null ? "FEHLT" : db)}");
            Console.WriteLine($"  Benutzer:    {(benutzer is null ? "FEHLT" : benutzer)}");
            Console.WriteLine($"  API-Key:     {(apiKey is null ? "FEHLT" : "vorhanden (wird nie angezeigt)")}");
        }

        return url is null || db is null || benutzer is null || apiKey is null
            ? null
            : new Ergebnis(url, db, benutzer, apiKey);
    }

    private static async Task<string?> ApiKeyEntschluesselnAsync(ErpDbContext kontext)
    {
        var eintrag = await kontext.GeheimeEinstellungen.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Schluessel == GeheimnisSchluessel.OdooApiKey);
        if (eintrag is null)
            return null;
        try
        {
#pragma warning disable CA1416 // DPAPI ist Windows-only — Betrieb ausschließlich auf Windows Server
            var schutz = DataProtectionProvider.Create(
                new DirectoryInfo(GeheimnisAblage.SchluesselOrdner),
                b => b.SetApplicationName(GeheimnisAblage.Anwendungsname)
                      .ProtectKeysWithDpapi(protectToLocalMachine: true));
#pragma warning restore CA1416
            return schutz.CreateProtector(GeheimnisAblage.Zweck).Unprotect(eintrag.WertVerschluesselt);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            Console.Error.WriteLine(
                "Der Odoo-API-Key wurde auf einer anderen Maschine verschlüsselt — " +
                "bitte in der Kundenzentrale (Administration → Odoo-Anbindung) neu eintragen.");
            return null;
        }
    }

    private static string? Wert(Dictionary<string, string> einstellungen, string schluessel)
    {
        var wert = einstellungen.GetValueOrDefault(schluessel);
        return string.IsNullOrWhiteSpace(wert) ? null : wert.Trim();
    }

    private static string? Umgebungsvariable(string name)
    {
        var wert = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(wert) ? null : wert.Trim();
    }
}
