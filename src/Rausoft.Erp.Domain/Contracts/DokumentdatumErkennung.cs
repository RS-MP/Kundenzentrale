using System.Globalization;
using System.Text.RegularExpressions;

namespace Rausoft.Erp.Domain.Contracts;

/// <summary>
/// Leitet das Dokumentdatum aus dem Dateinamen ab (FEEDBACK Nr. 18), z. B.
/// „Bestellung 2026-09-08.pdf", „4228_08.09.2026.pdf", „BE_20260908.pdf".
/// Rein, ohne Dateisystem — das Datei-Änderungsdatum als Rückfall ergänzt
/// der Aufrufer.
/// </summary>
public static partial class DokumentdatumErkennung
{
    // Reihenfolge = Priorität; Ziffern davor/dahinter dürfen nicht angrenzen
    // (sonst würde z. B. eine Kundennummer als Datum gelesen).
    [GeneratedRegex(@"(?<!\d)(?<j>(19|20)\d{2})[-_.](?<m>\d{1,2})[-_.](?<t>\d{1,2})(?!\d)")]
    private static partial Regex Iso();

    [GeneratedRegex(@"(?<!\d)(?<t>\d{1,2})\.(?<m>\d{1,2})\.(?<j>(19|20)\d{2}|\d{2})(?!\d)")]
    private static partial Regex Deutsch();

    [GeneratedRegex(@"(?<!\d)(?<j>(19|20)\d{2})(?<m>\d{2})(?<t>\d{2})(?!\d)")]
    private static partial Regex Kompakt();

    // Nicht als Folge lesen, wenn davor ein Datumsteil steht („… 2026-09-08")
    [GeneratedRegex(@"(?<!\d[-_.]\d{1,2})-(\d{1,2})$")]
    private static partial Regex FolgeSuffix();

    public static DateOnly? AusDateiname(string dateiname)
    {
        var name = Path.GetFileNameWithoutExtension(dateiname);
        foreach (var muster in new[] { Iso(), Deutsch(), Kompakt() })
        foreach (Match treffer in muster.Matches(name))
        {
            if (Plausibel(treffer) is { } datum)
                return datum;
        }
        return null;
    }

    /// <summary>
    /// Folgenummer aus dem gewachsenen Ablage-Prinzip: „4228.pdf" = 0,
    /// „4228-1.pdf" = 1, „4228-2.pdf" = 2 … — Reihenfolge bei gleichem Datum.
    /// </summary>
    public static int Folge(string dateiname)
    {
        var treffer = FolgeSuffix().Match(Path.GetFileNameWithoutExtension(dateiname));
        return treffer.Success ? int.Parse(treffer.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    private static DateOnly? Plausibel(Match treffer)
    {
        var jahr = int.Parse(treffer.Groups["j"].Value, CultureInfo.InvariantCulture);
        if (jahr < 100)
            jahr += 2000;
        var monat = int.Parse(treffer.Groups["m"].Value, CultureInfo.InvariantCulture);
        var tag = int.Parse(treffer.Groups["t"].Value, CultureInfo.InvariantCulture);
        if (jahr < 1990 || monat is < 1 or > 12 || tag < 1 || tag > DateTime.DaysInMonth(jahr, monat))
            return null;
        var datum = new DateOnly(jahr, monat, tag);
        // Datum in der Zukunft (über ein Jahr) ist eher eine Nummer als ein Stand
        return datum > DateOnly.FromDateTime(DateTime.Today).AddYears(1) ? null : datum;
    }
}
