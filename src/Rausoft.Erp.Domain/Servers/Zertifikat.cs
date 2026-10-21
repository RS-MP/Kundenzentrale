using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Domain.Servers;

/// <summary>
/// SSL-Zertifikat eines produktiven Systems — ID.prove-SaaS-Server und alle
/// weiteren Server. Die Zertifikate (z. B. bei IONOS) laufen nach kurzer
/// Laufzeit ab; die Kundenzentrale erinnert ab <see cref="VorlaufTage"/>
/// vor Ablauf per E-Mail und wiederholt die Erinnerung, bis jemand
/// bestätigt, dass das neue Zertifikat online ist (<see cref="Erneuern"/>).
/// </summary>
public class Zertifikat
{
    public int Id { get; set; }

    /// <summary>Anzeigename, z. B. „idprove.rausoft.de" oder „Serviceportal".</summary>
    public required string Bezeichnung { get; set; }

    /// <summary>Hostname für die automatische Prüfung des live ausgelieferten Zertifikats (leer = keine Prüfung).</summary>
    public string? Hostname { get; set; }

    public int Port { get; set; } = 443;

    /// <summary>Optionale Verknüpfung mit einem ID.prove-Server.</summary>
    public int? ServerId { get; set; }
    public Server? Server { get; set; }

    /// <summary>Wo das Zertifikat verlängert wird, z. B. IONOS.</summary>
    public string Anbieter { get; set; } = "IONOS";

    /// <summary>Laufzeit eines neu ausgestellten Zertifikats — Vorschlag für das nächste Ablaufdatum beim Bestätigen.</summary>
    public int LaufzeitMonate { get; set; } = 6;

    /// <summary>Ablaufdatum des aktuell eingesetzten Zertifikats.</summary>
    public DateOnly GueltigBis { get; set; }

    /// <summary>So viele Tage vor Ablauf beginnt die Erinnerung.</summary>
    public int VorlaufTage { get; set; } = 14;

    /// <summary>E-Mail-Empfänger der Erinnerung (mehrere durch Komma oder Semikolon). Leer = Standardempfänger aus den Einstellungen.</summary>
    public string? Empfaenger { get; set; }

    public string? Notiz { get; set; }

    public bool Aktiv { get; set; } = true;

    // --- Letzte Bestätigung „neues Zertifikat ist online" ---------------------
    public DateTime? ErneuertAm { get; set; }
    public int? ErneuertVonId { get; set; }
    public Mitarbeiter? ErneuertVon { get; set; }

    // --- Erinnerungen (seit der letzten Bestätigung) ---------------------------
    public DateTime? LetzteErinnerungAm { get; set; }
    public int ErinnerungenGesendet { get; set; }
    /// <summary>Fehlertext des letzten Versandversuchs (null = ok).</summary>
    public string? LetzterMailFehler { get; set; }

    // --- Automatische Prüfung des live ausgelieferten Zertifikats --------------
    public DateTime? LiveGeprueftAm { get; set; }
    public DateOnly? LiveGueltigBis { get; set; }
    public string? LiveAussteller { get; set; }
    public string? LiveFehler { get; set; }

    /// <summary>Bestätigung: neues Zertifikat ist online — Ablaufdatum übernehmen, Erinnerungen zurücksetzen.</summary>
    public void Erneuern(DateOnly neuesGueltigBis, int? vonId, DateTime jetzt)
    {
        GueltigBis = neuesGueltigBis;
        ErneuertAm = jetzt;
        ErneuertVonId = vonId;
        LetzteErinnerungAm = null;
        ErinnerungenGesendet = 0;
        LetzterMailFehler = null;
    }
}

/// <summary>
/// Reine Erinnerungslogik (ohne DB/Mail), damit sie testbar ist: Wann ist eine
/// Erinnerung fällig, welcher Status gilt, welches Ablaufdatum wird beim
/// Bestätigen vorgeschlagen?
/// </summary>
public static class ZertifikatErinnerung
{
    /// <summary>Tage bis zum Ablauf (negativ = abgelaufen).</summary>
    public static int TageBisAblauf(DateOnly gueltigBis, DateOnly heute) => gueltigBis.DayNumber - heute.DayNumber;

    /// <summary>„ok" · „bald" (Vorlauf erreicht) · „abgelaufen".</summary>
    public static string Status(DateOnly gueltigBis, int vorlaufTage, DateOnly heute)
    {
        var tage = TageBisAblauf(gueltigBis, heute);
        return tage < 0 ? "abgelaufen" : tage <= vorlaufTage ? "bald" : "ok";
    }

    /// <summary>
    /// Fällig, wenn der Vorlauf erreicht ist und seit der letzten Erinnerung
    /// mindestens <paramref name="intervallTage"/> vergangen sind. Nach einer
    /// Bestätigung ist die letzte Erinnerung null → sofort wieder fällig, sobald
    /// der nächste Vorlauf erreicht ist.
    /// </summary>
    public static bool ErinnerungFaellig(DateOnly gueltigBis, int vorlaufTage, DateTime? letzteErinnerungAm,
        int intervallTage, DateTime jetzt)
    {
        var heute = DateOnly.FromDateTime(jetzt);
        if (TageBisAblauf(gueltigBis, heute) > vorlaufTage)
            return false;
        if (letzteErinnerungAm is null)
            return true;
        return (jetzt - letzteErinnerungAm.Value).TotalDays >= Math.Max(1, intervallTage);
    }

    /// <summary>
    /// Vorschlag für das neue Ablaufdatum: das live erkannte Datum, wenn es
    /// später als das bisherige ist; sonst bisheriges Datum + Laufzeit (bei
    /// bereits abgelaufenem Zertifikat ab heute gerechnet).
    /// </summary>
    public static DateOnly VorschlagNeuesGueltigBis(DateOnly bisher, int laufzeitMonate, DateOnly? live, DateOnly heute)
    {
        if (live is { } l && l > bisher)
            return l;
        var basis = bisher < heute ? heute : bisher;
        return basis.AddMonths(Math.Max(1, laufzeitMonate));
    }

    /// <summary>Empfängerliste aus Komma-/Semikolon-Text — leere Einträge fallen weg.</summary>
    public static List<string> Empfaenger(string? text) =>
        (text ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(e => e.Contains('@')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
