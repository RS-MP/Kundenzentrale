using System.Globalization;

namespace Rausoft.Erp.Domain.Shared;

/// <summary>Wohin schreibende Odoo-Funktionen (z. B. Anlegen einer Website-Anfrage) gehen.</summary>
public enum OdooSchreibZiel
{
    /// <summary>Es wird nichts nach Odoo geschrieben.</summary>
    Aus = 0,
    /// <summary>In die Odoo-Testdatenbank (eigene Zugangsdaten, läuft nach 14 Tagen ab).</summary>
    Test = 1,
    /// <summary>In die Produktiv-Datenbank (Zugangsdaten der Odoo-Anbindung).</summary>
    Produktiv = 2,
}

/// <summary>
/// Regeln für die Odoo-Testdatenbank und das Schreiben nach Odoo (Entscheidung MP 17.11.2026).
/// Odoo stellt Testdatenbanken nur für <see cref="TestGueltigkeitTage"/> Tage bereit; Adresse,
/// Datenbankname und API-Key wechseln dann. Die Testverbindung ist deshalb ein eigener Satz
/// Zugangsdaten neben der Produktiv-Anbindung, die davon unberührt bleibt:
/// - Lesen und Importe nutzen immer die Produktiv-Anbindung.
/// - Schreiben geht nur an das eingestellte Ziel; Standard ist „Aus".
/// - Die Testverbindung darf nie auf die Produktiv-Datenbank zeigen.
/// - Ein Testsystem der Kundenzentrale schreibt nie in die Produktiv-Datenbank.
/// - Ist die Testdatenbank abgelaufen, wird nicht geschrieben.
/// </summary>
public static class OdooSchreibRegeln
{
    public const int TestGueltigkeitTage = 14;

    private static readonly string[] DatumsFormate = ["yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy"];

    public static OdooSchreibZiel Ziel(string? wert) =>
        Enum.TryParse<OdooSchreibZiel>(wert?.Trim(), ignoreCase: true, out var ziel) && Enum.IsDefined(ziel)
            ? ziel
            : OdooSchreibZiel.Aus;

    public static DateOnly? GueltigBis(string? wert) =>
        DateOnly.TryParseExact(wert?.Trim(), DatumsFormate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var tag)
            ? tag
            : null;

    public static string GueltigBisText(DateOnly tag) => tag.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Abgelaufen ist die Testdatenbank ab dem Tag nach „gültig bis"; ohne Datum gilt sie als nicht abgelaufen.</summary>
    public static bool Abgelaufen(DateOnly? gueltigBis, DateOnly heute) => gueltigBis is { } tag && tag < heute;

    public static int? TageRest(DateOnly? gueltigBis, DateOnly heute) =>
        gueltigBis is { } tag ? tag.DayNumber - heute.DayNumber : null;

    /// <summary>An diese Adresse geht der API-Key — nur eine vollständige https-Adresse ist zulässig.</summary>
    public static bool IstHttpsAdresse(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.Host.Length > 0 && string.IsNullOrEmpty(uri.UserInfo);

    /// <summary>Gleicher Server und gleiche Datenbank wie die Produktiv-Anbindung?</summary>
    public static bool IstProduktivDatenbank(string? url, string? datenbank, string? prodUrl, string? prodDatenbank)
    {
        if (string.IsNullOrWhiteSpace(prodUrl) || string.IsNullOrWhiteSpace(prodDatenbank)
            || !Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var test)
            || !Uri.TryCreate(prodUrl.Trim(), UriKind.Absolute, out var prod))
            return false;
        return string.Equals(test.Host, prod.Host, StringComparison.OrdinalIgnoreCase)
               && string.Equals(datenbank?.Trim(), prodDatenbank.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Prüft die Zugangsdaten der Testdatenbank; <c>null</c> = in Ordnung.</summary>
    public static string? TestZugangFehler(string? url, string? datenbank, string? benutzer,
        string? prodUrl, string? prodDatenbank)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(datenbank) || string.IsNullOrWhiteSpace(benutzer))
            return "Adresse, Datenbank und Benutzer der Odoo-Testdatenbank müssen ausgefüllt sein.";
        if (!IstHttpsAdresse(url))
            return "Die Adresse muss eine vollständige https://-URL sein (der API-Key geht an dieses Ziel).";
        if (IstProduktivDatenbank(url, datenbank, prodUrl, prodDatenbank))
            return "Das ist die Produktiv-Datenbank. Als Testdatenbank ist nur eine andere Adresse oder ein anderer Datenbankname zulässig.";
        return null;
    }

    /// <summary>
    /// Warum gerade nicht nach Odoo geschrieben werden darf; <c>null</c> = es darf geschrieben werden.
    /// </summary>
    public static string? SchreibSperre(OdooSchreibZiel ziel, bool kundenzentraleTestsystem,
        bool testZugangVollstaendig, DateOnly? testGueltigBis, DateOnly heute) => ziel switch
    {
        OdooSchreibZiel.Aus =>
            "Das Schreiben nach Odoo ist ausgeschaltet (Administration → Odoo-Testdatenbank).",
        OdooSchreibZiel.Produktiv when kundenzentraleTestsystem =>
            "Ein Testsystem der Kundenzentrale schreibt nie in die Odoo-Produktiv-Datenbank.",
        OdooSchreibZiel.Test when !testZugangVollstaendig =>
            "Die Odoo-Testdatenbank ist nicht vollständig eingerichtet (Administration → Odoo-Testdatenbank).",
        OdooSchreibZiel.Test when Abgelaufen(testGueltigBis, heute) =>
            $"Die Odoo-Testdatenbank ist seit dem {testGueltigBis:dd.MM.yyyy} abgelaufen. " +
            "Bitte die neue Testdatenbank unter Administration → Odoo-Testdatenbank eintragen.",
        _ => null,
    };
}
