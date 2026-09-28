using Rausoft.Erp.Domain.Contracts;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Anzeige der Dokumentarten (FEEDBACK Nr. 19): Name, Mehrzahl und kompaktes
/// Kürzel für Pills in Listen. Die Reihenfolge der Tabelle ist zugleich die
/// Gruppierung in der Vertragsakte — Hauptverträge zuerst, dann Ergänzungen,
/// Bestellungen, Protokolle, Kündigungen.
/// </summary>
public static class Dokumentarten
{
    private static readonly (BestandteilTyp Typ, string Name, string Mehrzahl, string Kuerzel)[] Tabelle =
    [
        (BestandteilTyp.Wartungsvertrag, "Wartungsvertrag", "Wartungsverträge", "WV"),
        (BestandteilTyp.Lizenzvertrag, "Lizenzvertrag", "Lizenzverträge", "LV"),
        (BestandteilTyp.Avv, "AVV", "AVV", "AVV"),
        (BestandteilTyp.Nda, "NDA", "NDA", "NDA"),
        (BestandteilTyp.Sondervereinbarung, "Sondervereinbarung", "Sondervereinbarungen", "SV"),
        (BestandteilTyp.Nachtrag, "Nachtrag", "Nachträge", "NT"),
        (BestandteilTyp.Bestellung, "Bestellung", "Bestellungen", "BE"),
        (BestandteilTyp.Installationsprotokoll, "Installationsprotokoll", "Installationsprotokolle", "IP"),
        (BestandteilTyp.Kuendigung, "Kündigung", "Kündigungen", "KÜ"),
        (BestandteilTyp.Sonstiges, "Sonstiges", "Sonstige", "SO"),
    ];

    private static (BestandteilTyp Typ, string Name, string Mehrzahl, string Kuerzel) Eintrag(BestandteilTyp typ) =>
        Tabelle.FirstOrDefault(t => t.Typ == typ) is { Name: not null } e ? e : Tabelle[^1];

    public static string Name(BestandteilTyp typ) => Eintrag(typ).Name;

    public static string Mehrzahl(BestandteilTyp typ) => Eintrag(typ).Mehrzahl;

    public static string Kuerzel(BestandteilTyp typ) => Eintrag(typ).Kuerzel;

    /// <summary>Position in der Gruppierung (Hauptverträge zuerst).</summary>
    public static int Rang(BestandteilTyp typ) => Array.FindIndex(Tabelle, t => t.Typ == typ);

    public static BestandteilTyp? Wert(string? name) =>
        Tabelle.FirstOrDefault(t => string.Equals(t.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase))
            is { Name: not null } e ? e.Typ : null;

    public static IEnumerable<string> AlleNamen => Tabelle.Select(t => t.Name);
}
