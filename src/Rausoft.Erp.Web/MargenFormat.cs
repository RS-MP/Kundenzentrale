using System.Globalization;

namespace Rausoft.Erp.Web;

/// <summary>
/// Zahlendarstellung des Margenrechners — deutsch, unabhängig von der
/// Browser-Sprache (eigenes Zahlenformat statt Kultur, damit die Anzeige auch
/// ohne geladene Kulturdaten stimmt). Beträge kommen pro Monat vom Server;
/// <c>per</c> ist 1 (pro Monat) oder 12 (pro Jahr).
/// </summary>
public static class MargenFormat
{
    private const string Schmal = " ";

    private static readonly NumberFormatInfo De = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ",",
        NumberGroupSizes = [3],
        NegativeSign = "−",
    };

    /// <summary>
    /// Rundet wie die Kalkulationsvorlage im Browser: auf Basis der kürzesten Dezimaldarstellung
    /// der Zahl, halbe aufwärts. Das Format „N“ allein rundet genaue Hälften zur geraden Ziffer
    /// (1,625 → 1,62) und träfe die Anzeige der Vorlage (1,63) nicht.
    /// </summary>
    private static string Z(double wert, int stellen)
    {
        if (double.IsFinite(wert) && Math.Abs(wert) < 1e15
            && decimal.TryParse(wert.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var dezimal))
            return Math.Round(dezimal, stellen, MidpointRounding.AwayFromZero).ToString("N" + stellen, De);
        return wert.ToString("N" + stellen, De);
    }

    /// <summary>Ganze Zahl, halbe aufwärts.</summary>
    public static string Zahl(double wert) => Z(Math.Floor(wert + 0.5), 0);

    /// <summary>Summen: volle Euro.</summary>
    public static string Eur(double wert, int per = 1) => Zahl(wert * per) + Schmal + "€";

    /// <summary>Je Kunde: unter 100 € mit Cent.</summary>
    public static string EurGenau(double wert, int per = 1)
    {
        var x = wert * per;
        return Z(x, Math.Abs(x) < 100 ? 2 : 0) + Schmal + "€";
    }

    public static string EurMitVorzeichen(double wert, int per = 1) => (wert > 0 ? "+" : "") + Eur(wert, per);

    public static string EurGenauMitVorzeichen(double wert, int per = 1) => (wert > 0 ? "+" : "") + EurGenau(wert, per);

    /// <summary><paramref name="anteil"/> als Bruchteil (0,25 → „25,0 %").</summary>
    public static string Prozent(double anteil, int stellen = 1) => Z(anteil * 100, stellen) + Schmal + "%";

    /// <summary>Euro je Case als Cent.</summary>
    public static string Cent(double euro, int per = 1)
    {
        var x = euro * 100 * per;
        return Z(x, x < 1 ? 3 : 2) + Schmal + "ct";
    }

    /// <summary>Cases: ab einer Million als „1,25 Mio.".</summary>
    public static string Volumen(double cases) => cases >= 1e6 ? Z(cases / 1e6, 2) + Schmal + "Mio." : Zahl(cases);

    /// <summary>Glatte Grenzen ohne Nachkommastellen: „5 Mio.", „130.000".</summary>
    public static string VolumenGlatt(double cases) =>
        cases >= 1e6 && cases % 1e6 == 0 ? Z(cases / 1e6, 0) + Schmal + "Mio." : Volumen(cases);

    public static string Zeitraum(int per) => per == 1 ? "pro Monat" : "pro Jahr";

    /// <summary>Zahl für SVG- und Stil-Attribute (Punkt als Dezimaltrenner).</summary>
    public static string Roh(double wert) => wert.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// Liest eine von Hand eingegebene Zahl: „130.000", „8.002,5", „8002.5", „130000".
    /// Punkt gilt als Tausenderpunkt, wenn ein Komma vorkommt oder genau Dreiergruppen folgen.
    /// </summary>
    public static bool Lesen(string? text, out double wert)
    {
        wert = 0;
        var t = (text ?? "").Replace("€", "").Replace(Schmal, "").Replace(" ", "").Trim();
        if (t.Length == 0)
            return false;
        if (t.Contains(','))
            t = t.Replace(".", "").Replace(',', '.');
        else if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d{1,3}(\.\d{3})+$"))
            t = t.Replace(".", "");
        return double.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out wert)
               && double.IsFinite(wert);
    }

    /// <summary>Zahl für die Textfelder der Datenpflege: Tausenderpunkte, Komma nur bei Bedarf.</summary>
    public static string Eingabe(double wert) =>
        wert == Math.Floor(wert) ? Z(wert, 0) : wert.ToString("#,##0.####", De);
}
