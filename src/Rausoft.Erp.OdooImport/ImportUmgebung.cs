using Microsoft.Data.SqlClient;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.OdooImport;

/// <summary>
/// Ziel-Instanz des Konsolen-Tools: Datenbank und Schlüsselbund. Reihenfolge je
/// Wert: Aufruf-Option (--db / --schluessel) → Umgebungsvariable
/// (KUNDENZENTRALE_DB / KUNDENZENTRALE_SCHLUESSEL) → Standard. Die Optionen
/// gibt es, weil Test- und Produktivsystem auf demselben Server liegen und die
/// maschinenweite Umgebungsvariable nur auf eine Instanz zeigen kann.
/// </summary>
public static class ImportUmgebung
{
    private const string LokaleDb =
        @"Server=(localdb)\MSSQLLocalDB;Database=Kundenzentrale;Trusted_Connection=True;Encrypt=False";

    /// <summary>Wert der Option --db (Connection String mit Windows-Auth, kein Geheimnis).</summary>
    public static string? DbOption { get; set; }

    /// <summary>Wert der Option --schluessel (Ordner des Data-Protection-Schlüsselbunds).</summary>
    public static string? SchluesselOption { get; set; }

    /// <summary>Connection String; <paramref name="vorgabe"/> gewinnt (Aufruf aus der Web-App).</summary>
    public static string Verbindung(string? vorgabe = null) =>
        vorgabe ?? Gesetzt(DbOption) ?? Gesetzt(Environment.GetEnvironmentVariable("KUNDENZENTRALE_DB")) ?? LokaleDb;

    public static string SchluesselOrdner => GeheimnisAblage.Ordner(
        Gesetzt(SchluesselOption) ?? Gesetzt(Environment.GetEnvironmentVariable("KUNDENZENTRALE_SCHLUESSEL")));

    /// <summary>„Datenbank auf Server" für die Ausgabe — zeigt, gegen welche Instanz das Tool läuft.</summary>
    public static string ZielText()
    {
        var teile = new SqlConnectionStringBuilder(Verbindung());
        return $"{teile.InitialCatalog} auf {teile.DataSource}";
    }

    private static string? Gesetzt(string? wert) => string.IsNullOrWhiteSpace(wert) ? null : wert.Trim();
}
