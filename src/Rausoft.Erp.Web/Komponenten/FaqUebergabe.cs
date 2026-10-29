namespace Rausoft.Erp.Web.Komponenten;

/// <summary>
/// Übergabe „als FAQ-Fall übernehmen" aus der Kundenakte: Die Akte legt nach
/// dem Speichern einer Notiz bzw. eines Telefonats Kunde, Betreff und Inhalt
/// hier ab und öffnet die FAQ-Erfassung, die die Vorbelegung einmalig abholt.
/// Bewusst im Speicher statt in der URL — Gesprächsinhalte gehören nicht in
/// Adresszeile und Browserverlauf.
/// </summary>
public static class FaqUebergabe
{
    private static (int KundeId, string Titel, string? Symptom)? _vorbelegung;

    public static void Ablegen(int kundeId, string titel, string? symptom) => _vorbelegung = (kundeId, titel, symptom);

    /// <summary>Vorbelegung abholen — danach ist sie weg.</summary>
    public static (int KundeId, string Titel, string? Symptom)? Abholen()
    {
        var v = _vorbelegung;
        _vorbelegung = null;
        return v;
    }
}
