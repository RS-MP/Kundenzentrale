namespace Rausoft.Erp.Domain.Zahlungen;

/// <summary>Offene Ausgangsrechnung aus Odoo — Grundlage der Zuordnung und der Liste offener Posten.</summary>
public sealed record OffeneRechnung(
    string Nummer, string? Kunde, int? FirmaId, DateOnly? Datum, DateOnly? Faellig,
    decimal Betrag, decimal Offen, string Waehrung, string? Zahlungsreferenz);

public enum ZuordnungsArt
{
    /// <summary>Nichts passt.</summary>
    Keine = 0,
    /// <summary>Genau eine Rechnungsnummer im Verwendungszweck, offener Betrag exakt — wird ohne Rückfrage gebucht.</summary>
    Eindeutig = 1,
    /// <summary>Rechnungsnummer gefunden, der Betrag weicht ab (Teilzahlung, Skonto, Überzahlung).</summary>
    BetragWeichtAb = 2,
    /// <summary>Mehrere Rechnungsnummern im Verwendungszweck (Sammelüberweisung).</summary>
    Mehrere = 3,
    /// <summary>Keine Rechnungsnummer, aber genau eine offene Rechnung mit diesem Betrag.</summary>
    NurBetrag = 4,
}

public sealed record Zuordnung(ZuordnungsArt Art, IReadOnlyList<OffeneRechnung> Rechnungen, string Grund);

/// <summary>
/// Ordnet einen Zahlungseingang den offenen Rechnungen zu (Entscheidung MP 26.11.2026):
/// automatisch gebucht wird nur der eindeutige Fall — Rechnungsnummer im Verwendungszweck,
/// Betrag stimmt exakt mit dem offenen Betrag überein, Währung Euro. Alles andere ist ein
/// Vorschlag für die Klärungsliste; die Entscheidung trifft ein Mensch.
/// </summary>
public static class ZahlungsZuordnung
{
    /// <summary>Kürzere Nummern (z. B. „12") stünden zufällig in jedem Verwendungszweck.</summary>
    private const int MinNummerLaenge = 5;

    public static Zuordnung Pruefen(string verwendungszweck, string? zahler, decimal betrag, string waehrung,
        IReadOnlyCollection<OffeneRechnung> offen)
    {
        if (!string.Equals(waehrung, "EUR", StringComparison.OrdinalIgnoreCase))
            return new(ZuordnungsArt.Keine, [], $"Eingang in {waehrung} — Fremdwährung wird nicht automatisch zugeordnet.");

        var zweck = Kern(verwendungszweck);
        var genannt = offen.Where(r => Enthaelt(zweck, r.Nummer) || Enthaelt(zweck, r.Zahlungsreferenz))
            .OrderBy(r => r.Nummer, StringComparer.Ordinal).ToList();

        if (genannt.Count == 1)
        {
            var r = genannt[0];
            if (!string.Equals(r.Waehrung, "EUR", StringComparison.OrdinalIgnoreCase))
                return new(ZuordnungsArt.BetragWeichtAb, genannt, $"Rechnung {r.Nummer} lautet auf {r.Waehrung}.");
            return r.Offen == betrag
                ? new(ZuordnungsArt.Eindeutig, genannt, $"Rechnung {r.Nummer} im Verwendungszweck, Betrag stimmt.")
                : new(ZuordnungsArt.BetragWeichtAb, genannt,
                    $"Rechnung {r.Nummer} im Verwendungszweck, offen sind aber {r.Offen:N2} € — Teilzahlung, Skonto oder Überzahlung?");
        }
        if (genannt.Count > 1)
            return new(ZuordnungsArt.Mehrere, genannt,
                $"Mehrere Rechnungen im Verwendungszweck ({string.Join(", ", genannt.Select(r => r.Nummer))}), zusammen offen " +
                $"{genannt.Sum(r => r.Offen):N2} € — Sammelüberweisung bitte von Hand in Odoo buchen.");

        var gleicherBetrag = offen.Where(r => r.Offen == betrag && string.Equals(r.Waehrung, "EUR", StringComparison.OrdinalIgnoreCase)).ToList();
        if (gleicherBetrag.Count == 1)
        {
            var r = gleicherBetrag[0];
            return new(ZuordnungsArt.NurBetrag, gleicherBetrag, NameAehnlich(zahler, r.Kunde)
                ? $"Keine Rechnungsnummer im Verwendungszweck; Betrag und Zahler passen zu Rechnung {r.Nummer}."
                : $"Keine Rechnungsnummer im Verwendungszweck; nur der Betrag passt zu Rechnung {r.Nummer} ({r.Kunde}).");
        }
        return new(ZuordnungsArt.Keine, [], gleicherBetrag.Count > 1
            ? $"Keine Rechnungsnummer im Verwendungszweck; {gleicherBetrag.Count} offene Rechnungen haben diesen Betrag."
            : "Keine offene Rechnung passt zu Verwendungszweck oder Betrag.");
    }

    /// <summary>Nur Buchstaben und Ziffern, groß — Banken brechen den Verwendungszweck um und streuen Leerzeichen ein.</summary>
    public static string Kern(string? text) =>
        new((text ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>
    /// Steht die Nummer als Ganzes im Verwendungszweck? „RE/2026/0012" trifft nicht
    /// „RE 2026 00123": vor und hinter der Fundstelle darf keine weitere Ziffer der Nummer stehen.
    /// </summary>
    public static bool Enthaelt(string zweckKern, string? nummer)
    {
        var n = Kern(nummer);
        if (n.Length < MinNummerLaenge || !n.Any(char.IsDigit))
            return false;
        for (var i = zweckKern.IndexOf(n, StringComparison.Ordinal); i >= 0; i = zweckKern.IndexOf(n, i + 1, StringComparison.Ordinal))
        {
            var davorZiffer = char.IsDigit(n[0]) && i > 0 && char.IsDigit(zweckKern[i - 1]);
            var danachZiffer = i + n.Length < zweckKern.Length && char.IsDigit(zweckKern[i + n.Length]);
            if (!davorZiffer && !danachZiffer)
                return true;
        }
        return false;
    }

    /// <summary>Ein gemeinsames Namenswort mit mindestens vier Zeichen („Muster GmbH" ↔ „MUSTER GMBH &amp; CO KG").</summary>
    public static bool NameAehnlich(string? zahler, string? kunde)
    {
        static HashSet<string> Worte(string? text) => (text ?? "")
            .Split([' ', ',', '.', '-', '/', '&', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToUpperInvariant())
            .Where(w => w.Length >= 4 && w is not ("GMBH" or "MBH" or "GESELLSCHAFT" or "UND"))
            .ToHashSet();
        var a = Worte(zahler);
        return a.Count > 0 && a.Overlaps(Worte(kunde));
    }
}
