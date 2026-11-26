using Rausoft.Erp.Domain.Servers;

namespace Rausoft.Erp.Domain.Shared;

/// <summary>
/// Gemeinsame Regeln der wöchentlichen Übersichten per Mail (offene Angebote, offene Posten):
/// Wochentag aus der Einstellung, höchstens ein Bericht je Woche, Empfänger nur in den
/// erlaubten Domänen (dieselbe Liste wie bei den Zertifikats-Mails, SICHERHEIT.md N1) —
/// die Berichte enthalten Kundennamen und dürfen das Haus nicht verlassen.
/// </summary>
public static class BerichtsRegeln
{
    /// <summary>„Montag" … „Sonntag" oder „täglich"; leer, „aus" und „nie" schalten den Bericht ab.</summary>
    public static bool WochentagPasst(string? einstellung, DateOnly heute)
    {
        var tag = (einstellung ?? "").Trim().ToLowerInvariant();
        if (tag is "" or "aus" or "nie")
            return false;
        var heutiger = heute.DayOfWeek switch
        {
            DayOfWeek.Monday => "montag", DayOfWeek.Tuesday => "dienstag", DayOfWeek.Wednesday => "mittwoch",
            DayOfWeek.Thursday => "donnerstag", DayOfWeek.Friday => "freitag", DayOfWeek.Saturday => "samstag", _ => "sonntag",
        };
        return tag == heutiger || tag == "täglich";
    }

    /// <summary>Heute fällig: Wochentag passt und heute ging noch keiner raus („täglich") bzw. seit sechs Tagen keiner.</summary>
    public static bool Faellig(string? wochentag, string? letzterBericht, DateOnly heute)
    {
        if (!WochentagPasst(wochentag, heute))
            return false;
        if (!DateOnly.TryParseExact(letzterBericht?.Trim(), "yyyy-MM-dd", out var letzter))
            return true;
        var abstand = heute.DayNumber - letzter.DayNumber;
        return (wochentag ?? "").Trim().Equals("täglich", StringComparison.OrdinalIgnoreCase) ? abstand >= 1 : abstand >= 6;
    }

    /// <summary>Erlaubte Domänen: die Einstellung, sonst die Domäne der Absenderadresse (eigene Firma) — wie bei den Zertifikats-Mails.</summary>
    public static string DomaenenText(string? einstellung, string? absender) =>
        ZertifikatPruefregeln.Domaenen(einstellung).Count > 0 ? einstellung! : ZertifikatPruefregeln.Domaene(absender) ?? "";

    /// <summary>Zulässige Empfänger aus dem Einstellungstext (Komma oder Semikolon getrennt).</summary>
    public static List<string> Empfaenger(string? empfaengerText, string? erlaubteDomaenenText) =>
        ZertifikatPruefregeln.ZulaessigeEmpfaenger(
            ZertifikatErinnerung.Empfaenger(empfaengerText), ZertifikatPruefregeln.Domaenen(erlaubteDomaenenText));
}
