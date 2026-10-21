using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Domain.Servers;

/// <summary>
/// SSL-Zertifikat eines produktiven Systems — ID.prove-SaaS-Server und alle
/// weiteren Server. Die Zertifikate (z. B. bei IONOS) laufen nach kurzer
/// Laufzeit ab; die Kundenzentrale erinnert ab <see cref="VorlaufTage"/>
/// vor Ablauf per E-Mail, eskaliert kurz vor Ablauf und wiederholt die
/// Erinnerung, bis das neue Zertifikat online ist — erkannt automatisch
/// über die Live-Prüfung (<see cref="AutoErkennung"/>) oder bestätigt von
/// Hand (<see cref="Erneuern"/>).
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

    /// <summary>Liefert der Server ein neueres Zertifikat, übernimmt die Kundenzentrale das Ablaufdatum selbst und beendet die Erinnerung.</summary>
    public bool AutoErkennung { get; set; } = true;

    /// <summary>Aus der Serverliste automatisch angelegt (Hostname aus der Server-URL).</summary>
    public bool AutomatischAngelegt { get; set; }

    // --- Letzte Bestätigung / Erkennung „neues Zertifikat ist online" -----------
    public DateTime? ErneuertAm { get; set; }
    public int? ErneuertVonId { get; set; }
    public Mitarbeiter? ErneuertVon { get; set; }

    // --- Erinnerungen (seit der letzten Erneuerung) ------------------------------
    public DateTime? LetzteErinnerungAm { get; set; }
    public int ErinnerungenGesendet { get; set; }
    /// <summary>Fehlertext des letzten Versandversuchs (null = ok).</summary>
    public string? LetzterMailFehler { get; set; }

    // --- Automatische Prüfung des live ausgelieferten Zertifikats -----------------
    public DateTime? LiveGeprueftAm { get; set; }
    public DateOnly? LiveGueltigBis { get; set; }
    public string? LiveAussteller { get; set; }
    public string? LiveFehler { get; set; }
    /// <summary>Seit wann die Prüfung durchgehend scheitert (null = letzte Prüfung erfolgreich).</summary>
    public DateTime? LiveFehlerSeit { get; set; }
    /// <summary>Wann die andauernde Prüfstörung zuletzt per Mail gemeldet wurde.</summary>
    public DateTime? LiveFehlerGemeldetAm { get; set; }
    /// <summary>Ausgehandeltes Protokoll, z. B. „TLS 1.3".</summary>
    public string? LiveProtokoll { get; set; }
    /// <summary>Befunde jenseits des Ablaufdatums: Hostname passt nicht, Kette unvollständig, veraltetes TLS angeboten … (durch „ · " getrennt).</summary>
    public string? LiveWarnungen { get; set; }

    public List<ZertifikatEreignis> Ereignisse { get; set; } = [];

    /// <summary>Neues Zertifikat ist online — Ablaufdatum übernehmen, Erinnerungen zurücksetzen. vonId null = automatisch erkannt.</summary>
    public void Erneuern(DateOnly neuesGueltigBis, int? vonId, DateTime jetzt)
    {
        GueltigBis = neuesGueltigBis;
        ErneuertAm = jetzt;
        ErneuertVonId = vonId;
        LetzteErinnerungAm = null;
        ErinnerungenGesendet = 0;
        LetzterMailFehler = null;
    }

    public void Protokoll(string art, string text, int? vonId, DateTime jetzt) =>
        Ereignisse.Add(new ZertifikatEreignis { Zertifikat = this, Art = art, Text = text, VonId = vonId, Zeitpunkt = jetzt });
}

/// <summary>Verlauf je Zertifikat: angelegt, erinnert, eskaliert, erneuert (erkannt/bestätigt), Prüfstörung …</summary>
public class ZertifikatEreignis
{
    public int Id { get; set; }

    public int ZertifikatId { get; set; }
    public Zertifikat? Zertifikat { get; set; }

    public DateTime Zeitpunkt { get; set; }

    /// <summary>Siehe <see cref="ZertifikatEreignisArt"/>.</summary>
    public required string Art { get; set; }

    public required string Text { get; set; }

    /// <summary>Auslösender Mitarbeiter; null = automatisch.</summary>
    public int? VonId { get; set; }
    public Mitarbeiter? Von { get; set; }
}

public static class ZertifikatEreignisArt
{
    public const string Angelegt = "Angelegt";
    public const string Geaendert = "Geaendert";
    public const string Erinnerung = "Erinnerung";
    public const string Eskalation = "Eskalation";
    public const string Abgelaufen = "Abgelaufen";
    public const string ErneuertBestaetigt = "ErneuertBestaetigt";
    public const string ErneuertErkannt = "ErneuertErkannt";
    public const string Pruefstoerung = "Pruefstoerung";
    public const string Warnung = "Warnung";
    public const string Versandfehler = "Versandfehler";
}

/// <summary>
/// Reine Erinnerungslogik (ohne DB/Mail), damit sie testbar ist: Wann ist eine
/// Erinnerung fällig, welche Stufe gilt, welches Ablaufdatum wird beim
/// Bestätigen vorgeschlagen, wann gilt eine Erneuerung als erkannt?
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
    /// Eskalationsstufe der Erinnerung: „keine" (vor dem Vorlauf), „erinnerung",
    /// „eskalation" (ab <paramref name="eskalationTage"/> vor Ablauf, zusätzliche
    /// Empfänger), „abgelaufen" (weitere Empfänger, z. B. Geschäftsführung).
    /// </summary>
    public static string Stufe(DateOnly gueltigBis, int vorlaufTage, int eskalationTage, DateOnly heute)
    {
        var tage = TageBisAblauf(gueltigBis, heute);
        return tage < 0 ? "abgelaufen"
            : tage <= Math.Min(eskalationTage, vorlaufTage) ? "eskalation"
            : tage <= vorlaufTage ? "erinnerung"
            : "keine";
    }

    /// <summary>
    /// Fällig, wenn der Vorlauf erreicht ist und seit der letzten Erinnerung
    /// mindestens <paramref name="intervallTage"/> vergangen sind. Nach einer
    /// Erneuerung ist die letzte Erinnerung null → sofort wieder fällig, sobald
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

    /// <summary>Erneuerung erkannt: der Server liefert ein Zertifikat, das später abläuft als das eingetragene.</summary>
    public static bool ErneuerungErkannt(DateOnly bisher, DateOnly? live) => live is { } l && l > bisher;

    /// <summary>
    /// Vorschlag für das neue Ablaufdatum: das live erkannte Datum, wenn es
    /// später als das bisherige ist; sonst bisheriges Datum + Laufzeit (bei
    /// bereits abgelaufenem Zertifikat ab heute gerechnet).
    /// </summary>
    public static DateOnly VorschlagNeuesGueltigBis(DateOnly bisher, int laufzeitMonate, DateOnly? live, DateOnly heute)
    {
        if (ErneuerungErkannt(bisher, live))
            return live!.Value;
        var basis = bisher < heute ? heute : bisher;
        return basis.AddMonths(Math.Max(1, laufzeitMonate));
    }

    /// <summary>Prüfstörung melden, wenn sie seit <paramref name="stoerungTage"/> Tagen andauert und seit 7 Tagen nicht gemeldet wurde.</summary>
    public static bool PruefstoerungMelden(DateTime? fehlerSeit, DateTime? gemeldetAm, int stoerungTage, DateTime jetzt) =>
        fehlerSeit is { } seit
        && (jetzt - seit).TotalDays >= Math.Max(1, stoerungTage)
        && (gemeldetAm is null || (jetzt - gemeldetAm.Value).TotalDays >= 7);

    /// <summary>Empfängerliste aus Komma-/Semikolon-Text — leere Einträge fallen weg.</summary>
    public static List<string> Empfaenger(string? text) =>
        (text ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(e => e.Contains('@')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Hostname aus einer Server-URL (null, wenn keine gültige http(s)-URL).</summary>
    public static string? HostAusUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.Host : null;
}
