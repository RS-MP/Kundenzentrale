namespace Rausoft.Erp.Dtos;

/// <summary>
/// Suchlogik der Kommunikationsleitlinien, ohne Datenbank. Die Suchbegriffe und
/// LIKE-Muster kommen aus <see cref="FaqSuche"/> (alle Begriffe müssen vorkommen,
/// Platzhalter gelten wörtlich); hier liegen die Varianten einer Regel und die
/// Trefferprüfung über ihre Textfelder.
/// </summary>
public static class LeitlinienSuche
{
    /// <summary>
    /// Varianten (Schreibweisen, Wortformen) aus einer Komma-/Semikolon-Liste:
    /// getrimmt, ohne Leere und Dubletten (Groß-/Kleinschreibung egal, erste
    /// Schreibweise gewinnt), höchstens 30.
    /// </summary>
    public static List<string> Varianten(string? text) =>
        (text ?? "")
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .DistinctBy(s => s.ToLowerInvariant())
            .Take(30)
            .ToList();

    /// <summary>Varianten als gespeicherte Form (kommagetrennt) — null, wenn keine.</summary>
    public static string? VariantenSpeichern(string? text)
    {
        var liste = Varianten(text);
        return liste.Count == 0 ? null : string.Join(", ", liste);
    }

    /// <summary>Die Textfelder einer Regel, in denen gesucht wird — dieselben wie in der Datenbankabfrage.</summary>
    public static string?[] Suchfelder(LeitlinieDto r) =>
        [r.Aussage, r.Stattdessen, r.Anlass, r.Begruendung, r.BeispielGut, r.BeispielSchlecht,
         string.Join(", ", r.Varianten), r.KiHinweis];

    /// <summary>Trifft die Regel alle Suchbegriffe? (leere Suche trifft immer)</summary>
    public static bool Trifft(LeitlinieDto r, string? suche) =>
        FaqSuche.Trifft(Suchfelder(r), FaqSuche.Begriffe(suche));
}
