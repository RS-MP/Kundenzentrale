namespace Rausoft.Erp.Web.Komponenten;

/// <summary>
/// Anzeigenamen der Kommunikationsleitlinien im Frontend: Bereiche (Arten),
/// Zielgruppen und Kanäle. Die Schlüssel kommen aus der API; das Frontend hat
/// keine Domain-Referenz.
/// </summary>
public static class LeitlinienTexte
{
    /// <summary>Bereiche der Seite in Anzeigereihenfolge: Art, Überschrift, Zeichen, Erklärung.</summary>
    public static readonly (string Art, string Titel, string Zeichen, string Erklaerung)[] Bereiche =
    [
        ("Verwenden", "So sagen wir es", "✓", "Begriffe und Formulierungen, die wir verwenden"),
        ("Vermeiden", "Besser vermeiden", "≈", "lieber nicht — mit der Formulierung, die wir stattdessen wählen"),
        ("Verboten", "Niemals", "✕", "Aussagen, die wir nie treffen — Lesebestätigung ist Pflicht"),
        ("Pflichthinweis", "Pflichthinweise", "!", "muss in der genannten Situation gesagt werden — Lesebestätigung ist Pflicht"),
        ("Grundsatz", "Grundsätze", "§", "Ton, Anrede und Stil"),
    ];

    public static readonly (string Schluessel, string Name)[] Zielgruppen =
    [
        ("vertrieb", "Vertrieb"),
        ("support", "Support"),
        ("ki", "KI (Claude)"),
    ];

    public static readonly (string Schluessel, string Name)[] Kanaele =
    [
        ("email", "E-Mail"),
        ("telefon", "Telefon"),
        ("angebot", "Angebot"),
        ("website", "Website"),
        ("chat", "Chat"),
    ];

    public static string Zielgruppe(string schluessel) =>
        Zielgruppen.FirstOrDefault(z => z.Schluessel == schluessel).Name ?? schluessel;

    public static string Kanal(string schluessel) =>
        Kanaele.FirstOrDefault(k => k.Schluessel == schluessel).Name ?? schluessel;

    /// <summary>Beschriftung des Felds „Aussage" je Art.</summary>
    public static string AussageLabel(string art) => art switch
    {
        "Verwenden" => "Begriff oder Formulierung, die wir verwenden",
        "Vermeiden" => "Begriff oder Formulierung, die wir vermeiden",
        "Verboten" => "Aussage, die nie getroffen werden darf",
        "Pflichthinweis" => "Wortlaut des Hinweises",
        _ => "Grundsatz",
    };

    /// <summary>Erste Zeilen eines WikiFormat-Textes als einzeilige Vorschau.</summary>
    public static string Kurz(string text)
    {
        var einzeilig = text.Replace("\r", "").Replace("\n", " ").Replace("**", "").Replace("`", "");
        return einzeilig.Length <= 200 ? einzeilig : einzeilig[..197].TrimEnd() + " …";
    }
}
