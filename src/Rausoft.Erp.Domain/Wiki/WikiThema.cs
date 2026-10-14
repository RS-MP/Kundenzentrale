namespace Rausoft.Erp.Domain.Wiki;

/// <summary>
/// Themen des Wikis — jedes Thema wird Redakteuren einzeln freigegeben
/// (FEEDBACK Nr. 22: ein Redakteur pflegt z. B. nur Sanktionslisten, ein
/// anderer Entwicklung oder Buchhaltung). Neue Themen hier ergänzen; die
/// Schlüssel landen in <see cref="Shared.Mitarbeiter.WikiThemen"/>.
/// </summary>
public static class WikiThema
{
    public const string Sanktionslisten = "sanktionslisten";

    /// <summary>Alle Themen mit Anzeigename — Reihenfolge = Anzeige in der Benutzerverwaltung.</summary>
    public static readonly (string Schluessel, string Name)[] Alle =
    [
        (Sanktionslisten, "Sanktionslisten"),
    ];

    public static bool IstBekannt(string schluessel) => Alle.Any(t => t.Schluessel == schluessel);
}
