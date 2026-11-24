using System.Globalization;
using System.Text.RegularExpressions;

namespace Rausoft.Erp.Domain.Margen;

/// <summary>Bereich und Basket, die aus einem Produktnamen gelesen wurden.</summary>
public record MargenLizenzProdukt(MargenBereich Bereich, long Cases);

/// <summary>
/// Eine verkaufte World-Check-Lizenz aus einem laufenden Abo und ihre Zuordnung
/// im Margenrechner. <see cref="Anzahl"/> = so viele Kunden zählt sie im Bestand
/// (0, wenn nicht zugeordnet); <see cref="Hinweis"/> sagt, warum nicht oder was auffiel.
/// </summary>
public record MargenLizenz(
    int KundeId, string Kundennummer, string Kunde, string Abo, string Produkt, decimal Menge,
    MargenBereich? Bereich, long? Basket, int Anzahl, string? Hinweis);

/// <summary>Verkaufte Lizenzen als Bestand: Kundenzahlen in Basket-Reihenfolge und die Positionen dahinter.</summary>
public record MargenLizenzbestand(
    List<int> KundenSanctionsOnly, List<int> KundenFullFile,
    List<MargenLizenz> Lizenzen, int Zugeordnet, int NichtZugeordnet,
    // Zeitpunkt des letzten Odoo-Imports, so wie er gespeichert ist
    string? StandImport);

/// <summary>
/// Liest aus dem Produktnamen einer Abo-Position, welche World-Check-Lizenz
/// verkauft wurde — z. B. „ID.prove SaaS World-Check Sanctions Only (5.000)"
/// → Sanctions Only, Basket 5.000. Die Namen kommen aus Odoo (erste Zeile der
/// Auftragsposition); im Programm stehen keine Produktnummern.
/// </summary>
public static partial class MargenLizenzErkennung
{
    /// <summary>Kommt die Position für den Margenrechner überhaupt in Frage? (World-Check oder einer der beiden Bereiche im Namen)</summary>
    public static bool IstWorldCheck(string? produkt) =>
        !string.IsNullOrWhiteSpace(produkt)
        && (WorldCheckMuster().IsMatch(produkt) || Bereich(produkt) is not null);

    /// <summary>Bereich und Basket; null, wenn eines von beiden nicht aus dem Namen hervorgeht.</summary>
    public static MargenLizenzProdukt? Erkennen(string? produkt)
    {
        if (string.IsNullOrWhiteSpace(produkt) || Bereich(produkt) is not { } bereich || Cases(produkt) is not { } cases)
            return null;
        return new MargenLizenzProdukt(bereich, cases);
    }

    private static MargenBereich? Bereich(string produkt)
    {
        var sanctionsOnly = SanctionsOnlyMuster().IsMatch(produkt);
        var fullFile = FullFileMuster().IsMatch(produkt);
        return sanctionsOnly == fullFile ? null : sanctionsOnly ? MargenBereich.SanctionsOnly : MargenBereich.FullFile;
    }

    /// <summary>Basket: die Zahl in der letzten Klammer, sonst die letzte Zahl mit Tausenderpunkten.</summary>
    private static long? Cases(string produkt)
    {
        var treffer = KlammerMuster().Matches(produkt);
        if (treffer.Count == 0)
            treffer = TausenderMuster().Matches(produkt);
        if (treffer.Count == 0)
            return null;
        var t = treffer[^1];
        var ziffern = TrennerMuster().Replace(t.Groups[1].Value, "");
        if (!long.TryParse(ziffern, NumberStyles.None, CultureInfo.InvariantCulture, out var zahl))
            return null;
        var faktor = t.Groups[2].Value.ToLowerInvariant() switch
        {
            "k" or "tsd" => 1_000L,
            "mio" => 1_000_000L,
            _ => 1L,
        };
        var cases = zahl * faktor;
        return cases is > 0 and <= (long)Margenrechnung.Horizont ? cases : null;
    }

    [GeneratedRegex(@"world[\s\-]?check", RegexOptions.IgnoreCase)]
    private static partial Regex WorldCheckMuster();

    [GeneratedRegex(@"sanctions?[\s\-]?only", RegexOptions.IgnoreCase)]
    private static partial Regex SanctionsOnlyMuster();

    [GeneratedRegex(@"full[\s\-]?file", RegexOptions.IgnoreCase)]
    private static partial Regex FullFileMuster();

    // „(5.000)", „(100 000 Cases)", „(50k)", „(1 Mio.)"
    [GeneratedRegex(@"\(\s*(\d{1,3}(?:[.  ']\d{3})+|\d{1,9})\s*(k|tsd|mio)?\b[^)]*\)", RegexOptions.IgnoreCase)]
    private static partial Regex KlammerMuster();

    [GeneratedRegex(@"(?<![\d.,])(\d{1,3}(?:\.\d{3})+)(?![\d,])()")]
    private static partial Regex TausenderMuster();

    [GeneratedRegex(@"[.  ']")]
    private static partial Regex TrennerMuster();
}

/// <summary>
/// Macht aus den World-Check-Positionen der laufenden Abos den Bestand des
/// Margenrechners: je Basket und Bereich die Zahl der verkauften Lizenzen.
/// Nichts fällt still weg — Positionen, die sich nicht zuordnen lassen, stehen
/// mit Begründung in der Liste.
/// </summary>
public static class MargenLizenzbestandErmittlung
{
    public sealed record Position(int KundeId, string Kundennummer, string Kunde, string Abo, string Produkt, decimal Menge);

    /// <summary>Über dieser Menge gilt die Angabe nicht mehr als Stückzahl von Lizenzen.</summary>
    public const int MaxMenge = 100;

    public static MargenLizenzbestand Ermitteln(IReadOnlyList<long> baskets, IEnumerable<Position> positionen, string? standImport)
    {
        var sanctionsOnly = new int[baskets.Count];
        var fullFile = new int[baskets.Count];
        var lizenzen = new List<MargenLizenz>();

        foreach (var p in positionen.Where(p => MargenLizenzErkennung.IstWorldCheck(p.Produkt)))
        {
            MargenLizenz Zeile(MargenBereich? bereich, long? basket, int anzahl, string? hinweis) =>
                new(p.KundeId, p.Kundennummer, p.Kunde, p.Abo, p.Produkt, p.Menge, bereich, basket, anzahl, hinweis);

            if (MargenLizenzErkennung.Erkennen(p.Produkt) is not { } erkannt)
            {
                lizenzen.Add(Zeile(null, null, 0, "Bereich oder Basket nicht aus dem Produktnamen erkennbar"));
                continue;
            }
            var index = -1;
            for (var i = 0; i < baskets.Count; i++)
                if (baskets[i] == erkannt.Cases)
                    index = i;
            if (index < 0)
            {
                lizenzen.Add(Zeile(erkannt.Bereich, erkannt.Cases, 0, "Diesen Basket gibt es im Margenrechner nicht (Datenpflege)"));
                continue;
            }

            // Menge = Stückzahl der Lizenz; alles andere (0,5 · 5.000 …) zählt als eine Lizenz und wird markiert
            var ganz = p.Menge == Math.Floor(p.Menge) && p.Menge is >= 1 and <= MaxMenge;
            var anzahl = ganz ? (int)p.Menge : 1;
            (erkannt.Bereich == MargenBereich.FullFile ? fullFile : sanctionsOnly)[index] += anzahl;
            lizenzen.Add(Zeile(erkannt.Bereich, erkannt.Cases, anzahl,
                ganz ? null : "Ungewöhnliche Menge — als eine Lizenz gezählt"));
        }

        var sortiert = lizenzen
            .OrderBy(l => l.Anzahl == 0)
            .ThenBy(l => l.Bereich)
            .ThenBy(l => l.Basket)
            .ThenBy(l => l.Kunde, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new MargenLizenzbestand([.. sanctionsOnly], [.. fullFile], sortiert,
            sortiert.Sum(l => l.Anzahl), sortiert.Count(l => l.Anzahl == 0), standImport);
    }
}
