namespace Rausoft.Erp.Domain.Shared;

/// <summary>
/// Wer eine Aufgabe (ohne Vorgang) ändern und löschen darf. Ändern darf jeder aktive
/// Mitarbeiter — wie Erledigen und Übernehmen. Löschen nur, wer sie angelegt hat, wer
/// zuständig ist, oder ein Admin; das Audit-Log hält jede Löschung fest.
/// Vorgangsschritte pflegt der Vorgang (Schritt entfernen), nicht diese Regeln.
/// </summary>
public static class AufgabenRegeln
{
    public static bool DarfLoeschen(int? erstelltVonId, int? zustaendigId, int? ich, bool istAdmin) =>
        istAdmin || (ich is not null && (erstelltVonId == ich || zustaendigId == ich));

    /// <summary>
    /// Grund in <see cref="Aufgabe.EntfallenGrund"/>, wenn eine aus Odoo übernommene Aufgabe gelöscht wird:
    /// Sie bleibt als „entfallen“ stehen, damit der Odoo-Abgleich sie nicht wieder anlegt.
    /// </summary>
    public const string GrundGeloescht = "geloescht";
}
