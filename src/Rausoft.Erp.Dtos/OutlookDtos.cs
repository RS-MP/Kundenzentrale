namespace Rausoft.Erp.Dtos;

// ---------------------------------------------------------------------------
// Outlook-Anbindung, Durchstich (ANFORDERUNGEN.md, E-Mail): Entwurf im eigenen
// Postfach über Microsoft Graph und Add-in „In Kundenakte".
// ---------------------------------------------------------------------------

/// <param name="TenantId">Verzeichnis-ID (Mandant) der App-Registrierung in Entra ID.</param>
/// <param name="ClientId">Anwendungs-ID der App-Registrierung — keine Geheimnisse, die Anmeldung läuft im Browser des Mitarbeiters.</param>
/// <param name="AnmeldeHinweis">E-Mail-Adresse des Mitarbeiters, damit Microsoft das richtige Konto vorschlägt.</param>
/// <param name="EmpfaengerErzwungen">Testsystem: Entwürfe gehen nur an diese Adresse.</param>
public record OutlookKonfiguration(
    bool Eingerichtet, string? TenantId, string? ClientId, string? AnmeldeHinweis, string Angemeldet,
    bool Testsystem, string? EmpfaengerErzwungen);

public record OutlookMail(string? Absender, List<string>? Empfaenger, string? Betreff, string? MessageId);

/// <param name="Art">"adresse" · "domaene" · "mehrdeutig" · "keine" · "intern"</param>
public record OutlookZuordnung(string Angemeldet, string Art, List<OutlookKunde> Kunden, string Hinweis);

public record OutlookKunde(int Id, string Firmenname, string Kundennummer, string? Ansprechpartner);
