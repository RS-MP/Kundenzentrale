using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Customers;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.OdooImport;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Zugang für schreibende Odoo-Funktionen. Lesen und Importe nutzen weiter die
/// Produktiv-Anbindung (<see cref="OdooImportDienst.VerbindenAsync"/>); geschrieben wird
/// nur über diesen Dienst und nur an das eingestellte Ziel — siehe <see cref="OdooSchreibRegeln"/>.
/// </summary>
public class OdooSchreibZugang(ErpDbContext db, GeheimnisDienst geheimnisse, InstanzOptionen instanz)
{
    /// <summary>Gepflegte Werte beider Verbindungen, ohne die API-Keys.</summary>
    public record Stand(
        OdooSchreibZiel Ziel,
        string TestUrl, string TestDatenbank, string TestBenutzer, DateOnly? TestGueltigBis, DateTime? TestKeyGesetztAm,
        string ProdUrl, string ProdDatenbank, string ProdBenutzer)
    {
        public bool TestVollstaendig =>
            TestKeyGesetztAm is not null
            && OdooSchreibRegeln.TestZugangFehler(TestUrl, TestDatenbank, TestBenutzer, ProdUrl, ProdDatenbank) is null;
    }

    public static DateOnly Heute() => DateOnly.FromDateTime(Ortszeit.VonUtc(DateTime.UtcNow));

    public async Task<Stand> StandAsync(CancellationToken ct = default)
    {
        var werte = await db.Einstellungen.AsNoTracking()
            .Where(e => e.Schluessel.StartsWith("Odoo.") || e.Schluessel.StartsWith(EinstellungsSchluessel.OdooTestPraefix))
            .ToDictionaryAsync(e => e.Schluessel, e => e.Wert, ct);
        string Wert(string schluessel) => werte.GetValueOrDefault(schluessel, "").Trim();
        var keyGesetztAm = await db.GeheimeEinstellungen.AsNoTracking()
            .Where(g => g.Schluessel == GeheimnisSchluessel.OdooTestApiKey)
            .Select(g => (DateTime?)g.GesetztAm).FirstOrDefaultAsync(ct);

        return new Stand(
            OdooSchreibRegeln.Ziel(Wert(EinstellungsSchluessel.OdooSchreibZiel)),
            Wert(EinstellungsSchluessel.OdooTestUrl), Wert(EinstellungsSchluessel.OdooTestDatenbank),
            Wert(EinstellungsSchluessel.OdooTestBenutzer),
            OdooSchreibRegeln.GueltigBis(Wert(EinstellungsSchluessel.OdooTestGueltigBis)), keyGesetztAm,
            Wert(EinstellungsSchluessel.OdooUrl), Wert(EinstellungsSchluessel.OdooDatenbank),
            Wert(EinstellungsSchluessel.OdooBenutzer));
    }

    public string? SchreibSperre(Stand stand) =>
        OdooSchreibRegeln.SchreibSperre(stand.Ziel, instanz.Testsystem, stand.TestVollstaendig, stand.TestGueltigBis, Heute());

    /// <summary>
    /// Client für die Testdatenbank — unabhängig vom Schreibziel, z. B. für die Verbindungsprüfung.
    /// Wirft <see cref="InvalidOperationException"/> mit Klartext, wenn die Zugangsdaten fehlen
    /// oder auf die Produktiv-Datenbank zeigen.
    /// </summary>
    public async Task<OdooClient> TestClientAsync(CancellationToken ct = default)
    {
        var stand = await StandAsync(ct);
        if (OdooSchreibRegeln.TestZugangFehler(stand.TestUrl, stand.TestDatenbank, stand.TestBenutzer,
                stand.ProdUrl, stand.ProdDatenbank) is { } fehler)
            throw new InvalidOperationException(fehler);
        var key = (await geheimnisse.HolenAsync(GeheimnisSchluessel.OdooTestApiKey, ct))?.Trim();
        if (string.IsNullOrEmpty(key))
            throw new InvalidOperationException("Für die Odoo-Testdatenbank ist kein API-Key hinterlegt.");
        return new OdooClient(stand.TestUrl, stand.TestDatenbank, stand.TestBenutzer, key);
    }

    /// <summary>
    /// Client für schreibende Funktionen, je nach eingestelltem Ziel. Wirft
    /// <see cref="InvalidOperationException"/> mit Klartext, wenn nicht geschrieben werden darf
    /// (ausgeschaltet, Testdatenbank abgelaufen, Testsystem → Produktiv).
    /// </summary>
    public async Task<(OdooClient Odoo, OdooSchreibZiel Ziel)> SchreibClientAsync(CancellationToken ct = default)
    {
        var stand = await StandAsync(ct);
        if (SchreibSperre(stand) is { } sperre)
            throw new InvalidOperationException(sperre);
        if (stand.Ziel == OdooSchreibZiel.Test)
            return (await TestClientAsync(ct), OdooSchreibZiel.Test);

        var key = (await geheimnisse.HolenAsync(GeheimnisSchluessel.OdooApiKey, ct))?.Trim();
        if (stand.ProdUrl == "" || stand.ProdDatenbank == "" || stand.ProdBenutzer == "" || string.IsNullOrEmpty(key))
            throw new InvalidOperationException(
                "Odoo-Zugangsdaten unvollständig — bitte unter Administration → Odoo-Anbindung pflegen.");
        return (new OdooClient(stand.ProdUrl, stand.ProdDatenbank, stand.ProdBenutzer, key), OdooSchreibZiel.Produktiv);
    }
}
