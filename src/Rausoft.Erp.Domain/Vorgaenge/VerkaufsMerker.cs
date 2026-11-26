using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Domain.Vorgaenge;

/// <summary>
/// Was sich die Kundenzentrale zu einem Odoo-Angebot oder -Auftrag merkt (ANFORDERUNGEN.md,
/// „Offene Angebote und Aufträge"): von Hand ausgeblendet, und welche Nachfass-Aufgabe zuletzt
/// dazu angelegt wurde. Das Angebot selbst bleibt in Odoo — hier liegt nur die Nummer.
/// </summary>
public class VerkaufsMerker
{
    public int Id { get; set; }

    /// <summary>Odoo-Nummer (S…), in Großbuchstaben.</summary>
    public required string Nummer { get; set; }

    public DateTime? AusgeblendetAm { get; set; }
    public int? AusgeblendetVonId { get; set; }
    public Mitarbeiter? AusgeblendetVon { get; set; }
    public string? AusblendGrund { get; set; }

    /// <summary>Zuletzt angelegte Nachfass-Aufgabe; leer, wenn noch keine entstand oder sie gelöscht wurde.</summary>
    public int? NachfassAufgabeId { get; set; }
    public Aufgabe? NachfassAufgabe { get; set; }

    /// <summary>Tag, an dem die letzte Nachfass-Aufgabe angelegt wurde — bleibt stehen, auch wenn die Aufgabe gelöscht wird.</summary>
    public DateOnly? NachfassZuletztAm { get; set; }

    public int NachfassAnzahl { get; set; }
}

/// <summary>
/// Wann zu einem gesendeten Angebot eine Nachfass-Aufgabe entsteht (Entscheidung MP 26.11.2026):
/// zum ersten Mal nach <see cref="StandardTage"/> Tagen ohne Reaktion, danach erneut, wenn die
/// letzte Aufgabe seit <see cref="StandardWiederholungTage"/> Tagen erledigt ist und das Angebot
/// in Odoo immer noch offen steht. Solange eine Aufgabe offen ist, entsteht keine zweite.
/// Beide Fristen sind Einstellungen (Verkauf.NachfassTage, Verkauf.NachfassWiederholungTage);
/// die Standardwerte gelten nur, wenn dort nichts Lesbares steht. 30 Tage, weil Angebote bei
/// rausoft eher einen Monat und länger laufen (MP 26.11.2026, vorher 7 und 14).
/// </summary>
public static class NachfassRegeln
{
    public const int StandardTage = 30;
    public const int StandardWiederholungTage = 30;
    public const int StandardRueckblickTage = 180;

    /// <param name="fristTage">0 oder weniger = keine Nachfass-Aufgaben.</param>
    /// <param name="letzterNachfass">Erledigt-Tag der letzten Aufgabe, sonst der Tag ihrer Anlage.</param>
    /// <param name="wiederholungTage">0 oder weniger = nur einmal nachfassen.</param>
    public static bool AufgabeFaellig(DateOnly? angebotsDatum, DateOnly heute, int fristTage,
        bool aufgabeOffen, DateOnly? letzterNachfass, int wiederholungTage)
    {
        if (fristTage <= 0 || aufgabeOffen || angebotsDatum is not { } datum)
            return false;
        if (letzterNachfass is { } letzter)
            return wiederholungTage > 0 && heute.DayNumber - letzter.DayNumber >= wiederholungTage;
        return heute.DayNumber - datum.DayNumber >= fristTage;
    }

    /// <summary>Tage seit dem Angebotsdatum; nie negativ.</summary>
    public static int? TageOffen(DateOnly? angebotsDatum, DateOnly heute) =>
        angebotsDatum is { } d ? Math.Max(0, heute.DayNumber - d.DayNumber) : null;

    /// <summary>Ganze Zahl aus einer Einstellung; leer oder unlesbar = Standard, nie über dem Höchstwert.</summary>
    public static int Zahl(string? wert, int standard, int hoechstens = 3650) =>
        int.TryParse(wert?.Trim(), out var zahl) ? Math.Clamp(zahl, 0, hoechstens) : standard;
}
