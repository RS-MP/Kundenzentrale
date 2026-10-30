namespace Rausoft.Erp.Domain.Shared;

/// <summary>
/// Wer eine Aufgabe ändern und löschen darf (Entscheidung 30.10.2026): nur in der
/// Kundenzentrale angelegte Aufgaben ohne Vorgang. Aus Odoo übernommene Aufgaben
/// bleiben unangetastet — der Odoo-Import gleicht sie ab und erledigt sie, sobald sie
/// in Odoo nicht mehr offen sind. Vorgangsschritte pflegt der Vorgang.
/// Ändern darf jeder aktive Mitarbeiter (wie Erledigen und Übernehmen); löschen nur,
/// wer die Aufgabe angelegt hat, wer zuständig ist, oder ein Admin. Das Audit-Log hält
/// jede Löschung fest.
/// </summary>
public static class AufgabenRegeln
{
    public static bool DarfAendern(int? vorgangId, int? odooId) => vorgangId is null && odooId is null;

    public static bool DarfLoeschen(int? vorgangId, int? odooId, int? erstelltVonId, int? zustaendigId, int? ich, bool istAdmin) =>
        DarfAendern(vorgangId, odooId)
        && (istAdmin || (ich is not null && (erstelltVonId == ich || zustaendigId == ich)));
}
