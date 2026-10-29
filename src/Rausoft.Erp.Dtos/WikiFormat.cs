using System.Text;
using System.Text.RegularExpressions;

namespace Rausoft.Erp.Dtos;

/// <summary>
/// Einfache Formatierung für Wiki-Texte (Prozessbeschreibungen u. ä.), angelehnt an Markdown:
/// <c># Überschrift</c>, <c>- Aufzählung</c>, <c>1. Nummeriert</c>, <c>**fett**</c>, <c>`Code`</c>,
/// Codeblöcke mit <c>```</c>, Tabellen mit <c>| a | b |</c> und Links (http/https).
/// Ungeformte JSON-Konfiguration (mehrere Zeilen mit <c>{</c>, <c>"Schlüssel": …</c>) wird
/// automatisch als Codeblock gesetzt, damit bestehende Texte ohne Nacharbeit lesbar sind.
/// Sicherheit: jeder Text wird zuerst HTML-kodiert; eigenes HTML im Text ist nie wirksam.
/// Liegt in den DTOs, weil das Frontend keine Domain-Referenz hat und es so testbar bleibt.
/// </summary>
public static partial class WikiFormat
{
    public static string ZuHtml(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var zeilen = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var html = new StringBuilder();
        var absatz = new List<string>();

        void AbsatzSchliessen()
        {
            if (absatz.Count == 0)
                return;
            html.Append("<p>").Append(string.Join("<br>", absatz.Select(Inline))).Append("</p>");
            absatz.Clear();
        }

        for (var i = 0; i < zeilen.Length; i++)
        {
            var zeile = zeilen[i];
            var bereinigt = zeile.Trim();

            if (bereinigt.StartsWith("```"))
            {
                AbsatzSchliessen();
                var code = new List<string>();
                for (i++; i < zeilen.Length && !zeilen[i].Trim().StartsWith("```"); i++)
                    code.Add(zeilen[i]);
                html.Append(CodeBlock(code));
                continue;
            }

            if (JsonBlockLaenge(zeilen, i) is var json and > 0)
            {
                AbsatzSchliessen();
                html.Append(CodeBlock(zeilen.Skip(i).Take(json).ToList()));
                i += json - 1;
                continue;
            }

            if (UeberschriftMuster().Match(zeile) is { Success: true } u)
            {
                AbsatzSchliessen();
                var ebene = Math.Min(u.Groups[1].Length + 2, 5);   // # → h3 … ### → h5 (Seite hat schon h1/h2)
                html.Append($"<h{ebene}>").Append(Inline(u.Groups[2].Value.Trim())).Append($"</h{ebene}>");
                continue;
            }

            if (AufzaehlungMuster().IsMatch(zeile) || NummerMuster().IsMatch(zeile))
            {
                AbsatzSchliessen();
                var nummeriert = NummerMuster().IsMatch(zeile);
                var muster = nummeriert ? NummerMuster() : AufzaehlungMuster();
                html.Append(nummeriert ? "<ol>" : "<ul>");
                for (; i < zeilen.Length && muster.Match(zeilen[i]) is { Success: true } m; i++)
                    html.Append("<li>").Append(Inline(zeilen[i][m.Length..].Trim())).Append("</li>");
                i--;
                html.Append(nummeriert ? "</ol>" : "</ul>");
                continue;
            }

            if (bereinigt.StartsWith('|'))
            {
                AbsatzSchliessen();
                var tabelle = new List<string>();
                for (; i < zeilen.Length && zeilen[i].Trim().StartsWith('|'); i++)
                    tabelle.Add(zeilen[i].Trim());
                i--;
                html.Append(Tabelle(tabelle));
                continue;
            }

            if (bereinigt.Length == 0)
                AbsatzSchliessen();
            else
                absatz.Add(bereinigt);
        }
        AbsatzSchliessen();
        return html.ToString();
    }

    /// <summary>
    /// Länge eines ungeformten JSON-Blocks ab Zeile <paramref name="start"/> (0 = keiner):
    /// mindestens zwei Zeilen, die mit Klammer oder Anführungszeichen beginnen, davon eine „"Schlüssel": …".
    /// </summary>
    private static int JsonBlockLaenge(string[] zeilen, int start)
    {
        var laenge = 0;
        var mitSchluessel = false;
        while (start + laenge < zeilen.Length && zeilen[start + laenge].Trim() is { Length: > 0 } z && "{}[]\"".Contains(z[0]))
        {
            mitSchluessel |= JsonSchluesselMuster().IsMatch(z);
            laenge++;
        }
        return laenge >= 2 && mitSchluessel ? laenge : 0;
    }

    private static string CodeBlock(List<string> zeilen)
    {
        // Gemeinsame Einrückung entfernen, damit eingefügte Blöcke nicht nach rechts wandern
        var einzug = zeilen.Where(z => z.Trim().Length > 0)
            .Select(z => z.Length - z.TrimStart().Length).DefaultIfEmpty(0).Min();
        var code = string.Join("\n", zeilen.Select(z => z.Length >= einzug ? z[einzug..] : z.TrimStart()));
        return $"<pre><code>{Kodieren(code)}</code></pre>";
    }

    private static string Tabelle(List<string> zeilen)
    {
        var html = new StringBuilder("<table>");
        var kopf = true;
        foreach (var zeile in zeilen)
        {
            if (TrennzeileMuster().IsMatch(zeile))
                continue;
            var zellen = zeile.Trim('|').Split('|').Select(z => Inline(z.Trim()));
            var tag = kopf ? "th" : "td";
            html.Append("<tr>").Append(string.Concat(zellen.Select(z => $"<{tag}>{z}</{tag}>"))).Append("</tr>");
            kopf = false;
        }
        return html.Append("</table>").ToString();
    }

    /// <summary>Inline-Formatierung auf bereits kodiertem Text; in `Code` wird nichts weiter ersetzt.</summary>
    private static string Inline(string text)
    {
        var teile = text.Split('`');
        var html = new StringBuilder();
        for (var i = 0; i < teile.Length; i++)
        {
            var kodiert = Kodieren(teile[i]);
            // Ungerade Teile liegen zwischen Backticks — außer ein schließender fehlt
            if (i % 2 == 1 && i < teile.Length - 1)
            {
                html.Append("<code>").Append(kodiert).Append("</code>");
                continue;
            }
            if (i % 2 == 1)
                html.Append('`');
            kodiert = FettMuster().Replace(kodiert, "<strong>$1</strong>");
            kodiert = LinkMuster().Replace(kodiert, m =>
            {
                var url = m.Value.TrimEnd('.', ',', ';', ':', ')');
                return $"<a href=\"{url}\" target=\"_blank\" rel=\"noopener noreferrer\">{url}</a>{m.Value[url.Length..]}";
            });
            html.Append(kodiert);
        }
        return html.ToString();
    }

    /// <summary>Kodiert die HTML-Sonderzeichen; Umlaute bleiben lesbar.</summary>
    private static string Kodieren(string text) => text
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");

    [GeneratedRegex(@"^\s{0,3}(#{1,3})\s+(.+)$")]
    private static partial Regex UeberschriftMuster();

    [GeneratedRegex(@"^\s*[-*•]\s+")]
    private static partial Regex AufzaehlungMuster();

    [GeneratedRegex(@"^\s*\d{1,3}[.)]\s+")]
    private static partial Regex NummerMuster();

    [GeneratedRegex(@"^""[^""]+""\s*:")]
    private static partial Regex JsonSchluesselMuster();

    [GeneratedRegex(@"^\|?[\s:|-]+\|?$")]
    private static partial Regex TrennzeileMuster();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex FettMuster();

    // Auf kodiertem Text: &amp; gehört zur URL, &quot; / &lt; beenden sie
    [GeneratedRegex(@"https?://(?:[^\s<&]|&amp;)+")]
    private static partial Regex LinkMuster();
}
