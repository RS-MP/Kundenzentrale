namespace Rausoft.Erp.Dtos;

/// <summary>
/// Suchlogik der FAQ, ohne Datenbank: Suchbegriffe zerlegen, Schlagwörter
/// bereinigen, LIKE-Muster bauen und Treffer im Speicher prüfen (für die
/// Hervorhebung im Frontend). Alle Begriffe müssen vorkommen (UND), egal in
/// welchem Feld — so findet „2fa authenticator" den Fall auch, wenn „2FA" im
/// Titel und „Authenticator" in der Ursache steht.
/// </summary>
public static class FaqSuche
{
    private static readonly char[] Trenner = [' ', '\t', '\r', '\n', ',', ';', '/', '(', ')', '"', '„', '“', '”'];

    /// <summary>Suchbegriffe: klein geschrieben, ohne Dubletten, mindestens zwei Zeichen.</summary>
    public static List<string> Begriffe(string? suche) =>
        (suche ?? "")
            .ToLowerInvariant()
            .Split(Trenner, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(b => b.Length >= 2)
            .Distinct()
            .ToList();

    /// <summary>
    /// LIKE-Muster für SQL Server mit <c>\</c> als Escape-Zeichen: die Platzhalter
    /// <c>%</c>, <c>_</c> und <c>[</c> im Suchbegriff gelten wörtlich.
    /// </summary>
    public static string LikeMuster(string begriff)
    {
        var escaped = begriff.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
        return "%" + escaped + "%";
    }

    /// <summary>Trifft der Text alle Begriffe? (Groß-/Kleinschreibung egal; leere Begriffsliste trifft immer.)</summary>
    public static bool Trifft(IEnumerable<string?> felder, IReadOnlyList<string> begriffe)
    {
        if (begriffe.Count == 0)
            return true;
        var text = string.Join('\n', felder.Where(f => f is not null)).ToLowerInvariant();
        return begriffe.All(text.Contains);
    }

    /// <summary>
    /// Schlagwörter aus Komma-/Semikolon-Liste: getrimmt, ohne Leere und Dubletten
    /// (Groß-/Kleinschreibung egal, erste Schreibweise gewinnt), höchstens 20.
    /// </summary>
    public static List<string> Schlagwoerter(string? text) =>
        (text ?? "")
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .DistinctBy(s => s.ToLowerInvariant())
            .Take(20)
            .ToList();

    /// <summary>Schlagwörter als gespeicherte Form (kommagetrennt) — null, wenn keine.</summary>
    public static string? SchlagwoerterSpeichern(string? text)
    {
        var liste = Schlagwoerter(text);
        return liste.Count == 0 ? null : string.Join(", ", liste);
    }
}
