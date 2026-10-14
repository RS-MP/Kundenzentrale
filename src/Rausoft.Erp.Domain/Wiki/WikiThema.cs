namespace Rausoft.Erp.Domain.Wiki;

/// <summary>
/// Themen des Wikis — jedes Thema wird Redakteuren einzeln freigegeben
/// (FEEDBACK Nr. 22: ein Redakteur pflegt z. B. nur Sanktionslisten, ein
/// anderer Vertrieb oder Buchhaltung). Neue Themen hier ergänzen; die
/// Schlüssel landen in <see cref="Shared.Mitarbeiter.WikiThemen"/>.
/// Bereich = Überschrift auf der Wiki-Startseite (z. B. „Vertrieb").
/// </summary>
public static class WikiThema
{
    public const string Sanktionslisten = "sanktionslisten";
    public const string Preisliste = "preisliste";

    /// <summary>Alle Themen mit Anzeigename und Bereich — Reihenfolge = Anzeige.</summary>
    public static readonly (string Schluessel, string Name, string Bereich)[] Alle =
    [
        (Sanktionslisten, "Sanktionslisten", "Compliance"),
        (Preisliste, "Preisliste", "Vertrieb"),
    ];

    public static bool IstBekannt(string schluessel) => Alle.Any(t => t.Schluessel == schluessel);
}
