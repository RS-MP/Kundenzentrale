using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rausoft.Erp.Domain.Margen;

/// <summary>
/// Liest die Zahlen aus der HTML-Kalkulationsvorlage („Margenrechner Full File
/// und Sanctions Only"): Baskets mit Verkaufspreisen, Staffeltabelle, Anker und
/// Schrittweite der linearen Staffel. So kommt der erste Datenstand ohne
/// Abtippen in die Kundenzentrale — die Vorlage selbst bleibt außerhalb des
/// Programms. Gelesen werden nur die Zahlenfelder des Skripts, nichts wird ausgeführt.
/// </summary>
public static partial class MargenVorlageLeser
{
    public const int MaxLaenge = 5_000_000;

    public static MargenDaten? Lesen(string? html, out string? fehler)
    {
        fehler = null;
        if (string.IsNullOrWhiteSpace(html))
            return Fehler("Die Datei ist leer.", out fehler);
        if (html.Length > MaxLaenge)
            return Fehler("Die Datei ist zu groß für eine Kalkulationsvorlage.", out fehler);

        var stufen = Tabelle(StaffelMuster().Match(html), 3);
        var anker = Tabelle(AnkerMuster().Match(html), 2);
        var baskets = Reihe(BasketMuster().Match(html));
        var vkSanctionsOnly = Reihe(VkSanctionsOnlyMuster().Match(html));
        var vkFullFile = Reihe(VkFullFileMuster().Match(html));
        var schritt = SchrittMuster().Match(html);

        var fehlt = new List<string>();
        if (stufen is null) fehlt.Add("Staffeltabelle (TIERS)");
        if (anker is null) fehlt.Add("Anker der linearen Staffel (ANCH)");
        if (!schritt.Success) fehlt.Add("Schrittweite (LIN_STEP)");
        if (baskets is null) fehlt.Add("Baskets (B)");
        if (vkSanctionsOnly is null) fehlt.Add("VK Sanctions Only (PS)");
        if (vkFullFile is null) fehlt.Add("VK Full File (PF)");
        if (fehlt.Count > 0)
            return Fehler("In der Datei fehlen: " + string.Join(", ", fehlt) + ". Ist es die Margenrechner-Vorlage?", out fehler);

        if (baskets!.Count != vkSanctionsOnly!.Count || baskets.Count != vkFullFile!.Count)
            return Fehler("Baskets und Verkaufspreise haben unterschiedlich viele Einträge.", out fehler);

        var daten = new MargenDaten(
            baskets.Select((b, i) => new MargenBasket((long)b, vkSanctionsOnly[i], vkFullFile[i])).ToList(),
            stufen!.Select(s => new MargenStaffelStufe((long)s[0], s[1], s[2])).ToList(),
            long.Parse(schritt.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
            anker!.Select(a => new MargenAnker((long)a[0], a[1])).ToList(),
            Text(StandMuster().Match(html), 100),
            Text(HinweisMuster().Match(html), MargenDaten.MaxTextLaenge));

        if (daten.Pruefen() is { Count: > 0 } probleme)
            return Fehler("Die Zahlen der Datei passen nicht: " + string.Join(" ", probleme), out fehler);
        return daten;
    }

    private static MargenDaten? Fehler(string text, out string? fehler)
    {
        fehler = text;
        return null;
    }

    private static List<double[]>? Tabelle(Match treffer, int spalten)
    {
        if (!treffer.Success)
            return null;
        try
        {
            var zeilen = JsonSerializer.Deserialize<List<double[]>>(treffer.Groups[1].Value);
            return zeilen is { Count: > 0 } && zeilen.All(z => z.Length == spalten) ? zeilen : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<double>? Reihe(Match treffer)
    {
        if (!treffer.Success)
            return null;
        try
        {
            return JsonSerializer.Deserialize<List<double>>(treffer.Groups[1].Value) is { Count: > 0 } werte ? werte : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Text ohne HTML-Auszeichnung, geschützte Leerzeichen als normale, auf die Höchstlänge gekürzt.</summary>
    private static string? Text(Match treffer, int maxLaenge)
    {
        if (!treffer.Success)
            return null;
        var text = WebUtility.HtmlDecode(TagMuster().Replace(treffer.Groups[1].Value, "")).Replace(' ', ' ').Trim();
        text = LeerraumMuster().Replace(text, " ");
        return text.Length == 0 ? null : text.Length > maxLaenge ? text[..maxLaenge] : text;
    }

    [GeneratedRegex(@"\bvar\s+TIERS\s*=\s*(\[\s*\[[\d\s,.\[\]]*\]\s*\])\s*;")]
    private static partial Regex StaffelMuster();

    [GeneratedRegex(@"\bvar\s+ANCH\s*=\s*(\[\s*\[[\d\s,.\[\]]*\]\s*\])\s*;")]
    private static partial Regex AnkerMuster();

    [GeneratedRegex(@"\bLIN_STEP\s*=\s*(\d{1,12})\b")]
    private static partial Regex SchrittMuster();

    [GeneratedRegex(@"\bvar\s+B\s*=\s*(\[[\d\s,.]*\])\s*;")]
    private static partial Regex BasketMuster();

    [GeneratedRegex(@"\bvar\s+PS\s*=\s*(\[[\d\s,.]*\])\s*;")]
    private static partial Regex VkSanctionsOnlyMuster();

    [GeneratedRegex(@"\bvar\s+PF\s*=\s*(\[[\d\s,.]*\])\s*;")]
    private static partial Regex VkFullFileMuster();

    // „… Staffel Stand 23. November 2026 …" in der Fußzeile
    [GeneratedRegex(@"\bStand\s+(\d{1,2}\.(?:\s|&nbsp;)*[A-Za-zäöüÄÖÜ]+\s+\d{4})")]
    private static partial Regex StandMuster();

    [GeneratedRegex(@"<li>\s*<b>\s*Stand der Regel:\s*</b>(.*?)</li>", RegexOptions.Singleline)]
    private static partial Regex HinweisMuster();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagMuster();

    [GeneratedRegex(@"\s+")]
    private static partial Regex LeerraumMuster();
}
