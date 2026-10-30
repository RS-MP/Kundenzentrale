using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Tests.Wiki;

/// <summary>Einfache Formatierung der Wiki-Texte — vor allem: eigenes HTML wirkt nie.</summary>
public class WikiFormatTests
{
    [Fact]
    public void HtmlImTextWirdKodiert()
    {
        var html = WikiFormat.ZuHtml("<script>alert(1)</script> **<b>x</b>** `<i>` https://a.de/?x=<img>");

        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<b>", html);
        Assert.DoesNotContain("<i>", html);
        Assert.DoesNotContain("<img>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void LinksSindNurHttpUndOeffnenSicher()
    {
        var html = WikiFormat.ZuHtml("Siehe https://data.trade.gov/liste.json. Und javascript:alert(1)");

        Assert.Contains("<a href=\"https://data.trade.gov/liste.json\" target=\"_blank\" rel=\"noopener noreferrer\">", html);
        Assert.Contains("</a>.", html);
        Assert.DoesNotContain("href=\"javascript", html);
    }

    [Fact]
    public void UeberschriftenAufzaehlungenUndFett()
    {
        var html = WikiFormat.ZuHtml("# Ablauf\n- **erst** laden\n- dann prüfen\n\n1. eins\n2. zwei");

        Assert.Equal("<h3>Ablauf</h3><ul><li><strong>erst</strong> laden</li><li>dann prüfen</li></ul><ol><li>eins</li><li>zwei</li></ol>", html);
    }

    [Fact]
    public void AbsaetzeUndZeilenumbrueche()
    {
        Assert.Equal("<p>Zeile 1<br>Zeile 2</p><p>Neuer Absatz</p>", WikiFormat.ZuHtml("Zeile 1\nZeile 2\n\nNeuer Absatz"));
    }

    [Fact]
    public void UngeformteJsonKonfigurationWirdCodeblock()
    {
        var text = "Ein Eintrag sieht so aus:\n {\n   \"ListProviderShortname\": \"DEB\",\n   \"FileFormat\": \"JSON\"\n }\nFehlt er, wird die Liste ignoriert.";

        var html = WikiFormat.ZuHtml(text);

        Assert.Equal(
            "<p>Ein Eintrag sieht so aus:</p>" +
            "<pre><code>{\n  &quot;ListProviderShortname&quot;: &quot;DEB&quot;,\n  &quot;FileFormat&quot;: &quot;JSON&quot;\n}</code></pre>" +
            "<p>Fehlt er, wird die Liste ignoriert.</p>", html);
    }

    [Fact]
    public void EinzelneZeileInAnfuehrungszeichenBleibtText()
    {
        Assert.Equal("<p>&quot;Zitat&quot;: so steht es da</p>", WikiFormat.ZuHtml("\"Zitat\": so steht es da"));
    }

    [Fact]
    public void CodeblockUndTabelle()
    {
        var html = WikiFormat.ZuHtml("```\na **b**\n```\n| Feld | Wert |\n|---|---|\n| URL | `x` |");

        Assert.Equal("<pre><code>a **b**</code></pre><table><tr><th>Feld</th><th>Wert</th></tr><tr><td>URL</td><td><code>x</code></td></tr></table>", html);
    }

    [Fact]
    public void LeererTextErgibtNichts() => Assert.Equal("", WikiFormat.ZuHtml("  \n "));

    [Fact]
    public void Kursiv_UndKeinFehlalarmBeiRechenzeichen()
    {
        Assert.Equal("<p>das ist <em>wichtig</em> und <strong>fett</strong></p>", WikiFormat.ZuHtml("das ist *wichtig* und **fett**"));
        Assert.Equal("<p>5 * 3 * 2 und a*b*c</p>", WikiFormat.ZuHtml("5 * 3 * 2 und a*b*c"));
        Assert.Equal("<p><strong>fett <em>und kursiv</em></strong></p>", WikiFormat.ZuHtml("**fett *und kursiv***"));
    }

    /// <summary>
    /// Der Editor (wwwroot/js/wiki-editor.js) liest das HTML von ZuHtml und schreibt beim
    /// Speichern Text zurück. Das Paar unten ist im Browser erzeugt (30.10.2026): Original →
    /// Rückwandlung. Beide müssen gleich aussehen, sonst ändert schon das Öffnen und
    /// Speichern eines Textes dessen Darstellung.
    /// </summary>
    [Fact]
    public void EditorRueckwandlung_SiehtGleichAus()
    {
        const string original = """
            # Ablauf
            Text mit **fett**, *kursiv* und `code` sowie [Link](https://a.de/x?y=1&z=2) und https://b.de.
            Zweite Zeile im Absatz.

            ## Unter
            - eins
            - **zwei** fett

            1. erstens
            2. zweitens

            | Name | Wert |
            | --- | --- |
            | a | `1` |

            {
              "Pfad": "C:\\daten",
              "Aktiv": true
            }

            ```
            code block
              eingerückt
            ```
            ### Klein
            Ende mit **fett *und kursiv***.
            """;
        const string ausDemEditor = """
            # Ablauf

            Text mit **fett**, *kursiv* und `code` sowie [Link](https://a.de/x?y=1&z=2) und https://b.de.
            Zweite Zeile im Absatz.

            ## Unter

            - eins
            - **zwei** fett

            1. erstens
            2. zweitens

            | Name | Wert |
            | --- | --- |
            | a | `1` |

            ```
            {
              "Pfad": "C:\\daten",
              "Aktiv": true
            }
            ```

            ```
            code block
              eingerückt
            ```

            ### Klein

            Ende mit **fett *und kursiv***.
            """;

        Assert.Equal(WikiFormat.ZuHtml(original), WikiFormat.ZuHtml(ausDemEditor));
    }

    [Fact]
    public void LinkMitText_NurHttpUndOhneDoppelteVerlinkung()
    {
        var html = WikiFormat.ZuHtml("Siehe [Liste der **UN**](https://un.org/a?x=1&y=2) und https://b.de.");
        Assert.Equal(
            "<p>Siehe <a href=\"https://un.org/a?x=1&amp;y=2\" target=\"_blank\" rel=\"noopener noreferrer\">Liste der <strong>UN</strong></a>" +
            " und <a href=\"https://b.de\" target=\"_blank\" rel=\"noopener noreferrer\">https://b.de</a>.</p>", html);

        // javascript: und Anführungszeichen im Link bleiben wirkungslos
        var boese = WikiFormat.ZuHtml("[x](javascript:alert(1)) [y](https://a.de\"onmouseover=\"alert(1))");
        Assert.DoesNotContain("href=\"javascript", boese);
        Assert.DoesNotContain("onmouseover=\"", boese);
    }
}
