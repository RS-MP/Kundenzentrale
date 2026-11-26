using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Rausoft.Erp.Domain.Zahlungen;

/// <summary>Ein gebuchter Zahlungseingang aus der Datei.</summary>
public sealed record UmsatzZeile(
    string Fingerabdruck, string? Auftragskonto, DateOnly Buchungstag, DateOnly? Valuta, string? Buchungstext,
    string Verwendungszweck, string? Zahler, decimal Betrag, string Waehrung);

/// <param name="Abgaenge">Zeilen mit negativem oder Nullbetrag — gezählt, nicht übernommen.</param>
/// <param name="Vorgemerkt">Noch nicht gebuchte Umsätze — kommen mit dem nächsten Auszug.</param>
/// <param name="Fehler">Gesetzt = die Datei wurde nicht gelesen.</param>
public sealed record KontoauszugInhalt(
    List<UmsatzZeile> Eingaenge, int Abgaenge, int Vorgemerkt, List<string> Warnungen, string? Fehler);

/// <summary>
/// Liest Kontoauszüge im Format „CSV-MT940" (Export des Online-Bankings, Semikolon-getrennt,
/// Spalten Auftragskonto · Buchungstag · Valutadatum · Buchungstext · Verwendungszweck ·
/// Beguenstigter/Zahlungspflichtiger · Kontonummer · BLZ · Betrag · Waehrung · Info). Die
/// Spalten werden an ihrer Überschrift erkannt, daher wird auch „CSV-CAMT" gelesen.
/// Reine Textverarbeitung ohne Dateizugriff — die Regeln sind so ohne Bankdatei prüfbar.
/// </summary>
public static class KontoauszugLeser
{
    public const int MaxBytes = 5 * 1024 * 1024;

    private static readonly CultureInfo Deutsch = CultureInfo.GetCultureInfo("de-DE");
    private static readonly string[] DatumsFormate = ["dd.MM.yy", "dd.MM.yyyy", "d.M.yy", "d.M.yyyy", "yyyy-MM-dd"];

    /// <summary>UTF-8 (mit oder ohne Kennung), sonst Latin-1 — so exportieren die Banken Umlaute.</summary>
    public static string Text(byte[] inhalt)
    {
        if (inhalt.Length >= 3 && inhalt[0] == 0xEF && inhalt[1] == 0xBB && inhalt[2] == 0xBF)
            return Encoding.UTF8.GetString(inhalt, 3, inhalt.Length - 3);
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(inhalt);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(inhalt);
        }
    }

    public static KontoauszugInhalt Lesen(string text)
    {
        var anfang = text.TrimStart();
        if (anfang.StartsWith(":20:", StringComparison.Ordinal) || anfang.StartsWith("{1:", StringComparison.Ordinal))
            return Fehler("Das ist eine MT940-Datei im SWIFT-Format. Bitte den Auszug im Online-Banking als „CSV-MT940“ exportieren.");
        if (anfang.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) || anfang.StartsWith("<Document", StringComparison.Ordinal))
            return Fehler("Das ist eine XML-Datei (CAMT). Bitte den Auszug im Online-Banking als „CSV-MT940“ exportieren.");

        var trenner = Trenner(anfang);
        var zeilen = Zeilen(text, trenner);
        var kopfIndex = zeilen.FindIndex(z => z.Any(f => Norm(f) == "buchungstag") && z.Any(f => Norm(f) == "betrag"));
        if (kopfIndex < 0)
            return Fehler("Die Spalten „Buchungstag“ und „Betrag“ fehlen — erwartet wird ein Export im Format „CSV-MT940“.");

        var kopf = zeilen[kopfIndex].Select(Norm).ToList();
        int Spalte(params string[] namen) => kopf.FindIndex(k => namen.Contains(k));
        var (sKonto, sTag, sValuta, sText, sZweck, sName, sBetrag, sWaehrung, sInfo) = (
            Spalte("auftragskonto"), Spalte("buchungstag"), Spalte("valutadatum", "valuta", "wertstellung"),
            Spalte("buchungstext"), Spalte("verwendungszweck"),
            Spalte("beguenstigter/zahlungspflichtiger", "name", "auftraggeber/empfaenger"),
            Spalte("betrag"), Spalte("waehrung"), Spalte("info"));
        if (sZweck < 0)
            return Fehler("Die Spalte „Verwendungszweck“ fehlt — erwartet wird ein Export im Format „CSV-MT940“.");

        var eingaenge = new List<UmsatzZeile>();
        var warnungen = new List<string>();
        var gesehen = new Dictionary<string, int>(StringComparer.Ordinal);
        int abgaenge = 0, vorgemerkt = 0;
        for (var i = kopfIndex + 1; i < zeilen.Count; i++)
        {
            var z = zeilen[i];
            if (z.All(string.IsNullOrWhiteSpace))
                continue;
            string? Feld(int s) => s >= 0 && s < z.Count && !string.IsNullOrWhiteSpace(z[s]) ? Glatt(z[s]) : null;

            if (!Tag(Feld(sTag), out var tag) || !Betrag(Feld(sBetrag), out var betrag))
            {
                // Vorgemerkte Umsätze haben oft noch kein Datum — das ist kein Fehler der Datei
                if (Feld(sInfo)?.Contains("vorgemerkt", StringComparison.OrdinalIgnoreCase) == true)
                    vorgemerkt++;
                else
                    warnungen.Add($"Zeile {i + 1}: Buchungstag oder Betrag nicht lesbar — übersprungen.");
                continue;
            }
            if (Feld(sInfo)?.Contains("vorgemerkt", StringComparison.OrdinalIgnoreCase) == true)
            {
                vorgemerkt++;
                continue;
            }
            if (betrag <= 0)
            {
                abgaenge++;
                continue;
            }

            var valuta = Tag(Feld(sValuta), out var v) ? v : (DateOnly?)null;
            var zweck = Feld(sZweck) ?? "";
            var schluessel = string.Join('|', Feld(sKonto), tag.ToString("yyyy-MM-dd"), valuta?.ToString("yyyy-MM-dd"),
                betrag.ToString("0.00", CultureInfo.InvariantCulture), zweck, Feld(sName));
            // Zwei wirklich gleiche Buchungen am selben Tag bleiben zwei: die laufende Nummer geht mit in den Fingerabdruck
            var nr = gesehen[schluessel] = gesehen.GetValueOrDefault(schluessel) + 1;
            eingaenge.Add(new UmsatzZeile(
                Hash($"{schluessel}|{nr}"), Kurz(Feld(sKonto), 40), tag, valuta, Kurz(Feld(sText), 100),
                Kurz(zweck, 1000)!, Kurz(Feld(sName), 200), betrag,
                Feld(sWaehrung)?.ToUpperInvariant() is { Length: 3 } w ? w : "EUR"));
        }
        return new KontoauszugInhalt(eingaenge, abgaenge, vorgemerkt, warnungen, null);
    }

    private static KontoauszugInhalt Fehler(string text) => new([], 0, 0, [], text);

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string? Kurz(string? text, int max) => text is null || text.Length <= max ? text : text[..max];

    /// <summary>Mehrfache Leerzeichen und Zeilenumbrüche innerhalb eines Feldes zu einem Leerzeichen.</summary>
    private static string Glatt(string feld) => string.Join(' ', feld.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Norm(string kopf) => Glatt(kopf).ToLowerInvariant()
        .Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");

    private static bool Tag(string? text, out DateOnly tag) =>
        DateOnly.TryParseExact(text, DatumsFormate, CultureInfo.InvariantCulture, DateTimeStyles.None, out tag);

    /// <summary>„1.234,56", „-12,00", „1234.56" — ein Komma ist immer das Dezimalzeichen.</summary>
    private static bool Betrag(string? text, out decimal betrag)
    {
        betrag = 0;
        if (text is null)
            return false;
        var t = text.Replace(" ", "").Replace("€", "");
        return t.Contains(',')
            ? decimal.TryParse(t, NumberStyles.Number, Deutsch, out betrag)
            : decimal.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out betrag);
    }

    private static char Trenner(string anfang)
    {
        var ersteZeile = anfang.Split('\n')[0];
        return new[] { ';', '\t', ',' }.MaxBy(t => ersteZeile.Count(c => c == t));
    }

    /// <summary>CSV mit Anführungszeichen („" = Anführungszeichen im Feld) und Zeilenumbrüchen innerhalb eines Feldes.</summary>
    private static List<List<string>> Zeilen(string text, char trenner)
    {
        var zeilen = new List<List<string>>();
        var zeile = new List<string>();
        var feld = new StringBuilder();
        var inAnfuehrung = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inAnfuehrung)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { feld.Append('"'); i++; }
                else if (c == '"') inAnfuehrung = false;
                else feld.Append(c);
            }
            else if (c == '"') inAnfuehrung = true;
            else if (c == trenner) { zeile.Add(feld.ToString()); feld.Clear(); }
            else if (c == '\n')
            {
                zeile.Add(feld.ToString());
                feld.Clear();
                zeilen.Add(zeile);
                zeile = [];
            }
            else if (c != '\r') feld.Append(c);
        }
        if (feld.Length > 0 || zeile.Count > 0)
        {
            zeile.Add(feld.ToString());
            zeilen.Add(zeile);
        }
        return zeilen;
    }
}
