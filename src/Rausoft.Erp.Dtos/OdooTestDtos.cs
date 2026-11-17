namespace Rausoft.Erp.Dtos;

/// <summary>Stand der Odoo-Testdatenbank und Ziel schreibender Odoo-Funktionen (Administration).</summary>
public record OdooTestStatus(
    string Url,
    string Datenbank,
    string Benutzer,
    DateOnly? GueltigBis,
    /// <summary>Tage bis zum Ablauf; negativ = abgelaufen; fehlt ohne Datum.</summary>
    int? TageRest,
    bool Abgelaufen,
    /// <summary>Wann der API-Key der Testdatenbank zuletzt gesetzt wurde; der Key selbst wird nie ausgegeben.</summary>
    DateTime? ApiKeyGesetztAm,
    /// <summary>„Aus", „Test" oder „Produktiv".</summary>
    string SchreibZiel,
    /// <summary>Diese Kundenzentrale ist selbst ein Testsystem — „Produktiv" ist dann nicht wählbar.</summary>
    bool KundenzentraleTestsystem,
    /// <summary>Warum gerade nicht geschrieben wird; fehlt, wenn geschrieben werden darf.</summary>
    string? SchreibSperre);

/// <summary>
/// Neue Testdatenbank eintragen. Der API-Key darf nur leer bleiben, wenn Adresse und
/// Datenbank unverändert sind; ohne Datum gilt heute + 14 Tage.
/// </summary>
public record OdooTestSpeichern(string? Url, string? Datenbank, string? Benutzer, string? ApiKey, DateOnly? GueltigBis);

public record OdooSchreibZielSetzen(string? Ziel);

public record OdooTestPruefung(bool Erfolg, string Meldung);
