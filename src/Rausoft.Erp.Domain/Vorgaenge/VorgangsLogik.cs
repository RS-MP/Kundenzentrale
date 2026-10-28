using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Domain.Vorgaenge;

/// <summary>Berechneter Status eines Vorgangs (Reihenfolge = Priorität der Prüfung).</summary>
public enum VorgangsStatus
{
    /// <summary>Noch kein Schritt begonnen.</summary>
    Neu = 0,
    /// <summary>Intern ist noch jemand dran.</summary>
    Laeuft = 1,
    /// <summary>Alle aktiven Schritte warten auf den Kunden, keiner überfällig.</summary>
    WartetAufKunden = 2,
    /// <summary>Mindestens ein aktiver Schritt ist überfällig.</summary>
    Haengt = 3,
    /// <summary>Alle Schritte erledigt oder entfallen.</summary>
    Abgeschlossen = 4,
}

/// <summary>Ein von Hand ergänzter Schritt beim Anlegen (wartet optional auf einen Vorlagenschritt).</summary>
public sealed record ManuellerSchritt(string Titel, int BereichId, int FristTage, int? WartetAufNr, int? ZustaendigId = null);

/// <summary>
/// Regeln der Vorgänge an einer Stelle (ANFORDERUNGEN.md, „Vorgänge"): Schritte aus der
/// Vorlage erzeugen, Abhängigkeiten auflösen, Fristen setzen, Status berechnen.
/// Arbeitet auf den Navigationen <see cref="Aufgabe.WartetAuf"/> — der Aufrufer lädt
/// immer alle Schritte eines Vorgangs zusammen.
/// </summary>
public static class VorgangsLogik
{
    public const string GrundEntfernt = "entfernt";
    public const string GrundBedingung = "bedingung";

    public const int MaxSchritte = 30;
    public const int MaxFristTage = 365;

    public static bool FertigOderWeg(Aufgabe a) => a.Status is AufgabenStatus.Erledigt or AufgabenStatus.Entfallen;

    /// <summary>Wartet auf einen Vorgänger, der noch nicht erledigt ist — liegt dann in keinem Pool.</summary>
    public static bool Gesperrt(Aufgabe a) => a.WartetAuf is { } v && !FertigOderWeg(v);

    /// <summary>Gestartet und noch nicht fertig.</summary>
    public static bool Aktiv(Aufgabe a) => !FertigOderWeg(a) && !Gesperrt(a);

    /// <summary>Wartet auf den Kunden und die Wiedervorlage ist noch nicht erreicht.</summary>
    public static bool WartetAufKunden(Aufgabe a, DateOnly heute) =>
        a.Status == AufgabenStatus.WartetAufKunden && (a.Wiedervorlage is null || a.Wiedervorlage > heute);

    /// <summary>Maßgebliches Datum: bei „Wartet auf Kunden" die Wiedervorlage, sonst die Frist.</summary>
    public static DateOnly? FaelligAm(Aufgabe a) =>
        a.Status == AufgabenStatus.WartetAufKunden ? a.Wiedervorlage ?? a.Faellig : a.Faellig;

    public static bool Ueberfaellig(Aufgabe a, DateOnly heute) =>
        Aktiv(a) && !WartetAufKunden(a, heute) && FaelligAm(a) is { } f && f < heute;

    public static VorgangsStatus Status(IReadOnlyCollection<Aufgabe> schritte, DateOnly heute)
    {
        var aktiv = schritte.Where(Aktiv).ToList();
        if (aktiv.Count == 0 && schritte.All(FertigOderWeg))
            return VorgangsStatus.Abgeschlossen;
        if (aktiv.Any(s => Ueberfaellig(s, heute)))
            return VorgangsStatus.Haengt;
        if (aktiv.Count > 0 && aktiv.All(s => WartetAufKunden(s, heute)))
            return VorgangsStatus.WartetAufKunden;
        if (schritte.All(s => s.Status is AufgabenStatus.Offen or AufgabenStatus.Entfallen))
            return VorgangsStatus.Neu;
        return VorgangsStatus.Laeuft;
    }

    /// <summary>
    /// Gibt gestarteten Schritten ohne Frist ihr Fälligkeitsdatum (heute + Frist in Tagen)
    /// und liefert die Schritte, die dadurch gerade in ihren Pool gekommen sind.
    /// </summary>
    public static List<Aufgabe> FristenSetzen(IEnumerable<Aufgabe> schritte, DateOnly heute)
    {
        var freigegeben = new List<Aufgabe>();
        foreach (var s in schritte)
        {
            if (s.Faellig is not null || !Aktiv(s))
                continue;
            s.Faellig = heute.AddDays(s.FristTage ?? 0);
            freigegeben.Add(s);
        }
        return freigegeben;
    }

    /// <summary>
    /// Schritte eines neuen Vorgangs aus der Vorlage: entfernte und nicht bestellte Schritte
    /// entfallen (bleiben aber sichtbar), manuelle Schritte kommen hinten dazu. Wer auf einen
    /// entfallenen Schritt wartet, hängt sich an dessen Vorgänger bzw. startet sofort.
    /// </summary>
    public static List<Aufgabe> SchritteErzeugen(
        IEnumerable<VorgangstypSchritt> vorlage,
        IReadOnlySet<int> entfernteNr,
        IEnumerable<string> erfuellteBedingungen,
        IEnumerable<ManuellerSchritt> manuelle,
        DateOnly start)
    {
        var erfuellt = new HashSet<string>(erfuellteBedingungen.Select(b => b.Trim()), StringComparer.OrdinalIgnoreCase);
        var vorlagenSchritte = vorlage.OrderBy(s => s.Nr).ToList();
        var jeNr = new Dictionary<int, Aufgabe>();
        var liste = new List<Aufgabe>();

        foreach (var s in vorlagenSchritte)
        {
            var a = new Aufgabe
            {
                Titel = s.Titel,
                ZustaendigBereichId = s.BereichId,
                FristTage = s.FristTage,
                Herkunft = SchrittHerkunft.Vorlage,
                Benachrichtigung = s.Benachrichtigung,
                AutoAbhaken = s.AutoAbhaken,
                Pflichtfeld = string.IsNullOrWhiteSpace(s.Pflichtfeld) ? null : s.Pflichtfeld.Trim(),
            };
            if (entfernteNr.Contains(s.Nr))
            {
                a.Status = AufgabenStatus.Entfallen;
                a.EntfallenGrund = GrundEntfernt;
            }
            else if (!string.IsNullOrWhiteSpace(s.Bedingung) && !erfuellt.Contains(s.Bedingung.Trim()))
            {
                a.Status = AufgabenStatus.Entfallen;
                a.EntfallenGrund = GrundBedingung;
            }
            jeNr[s.Nr] = a;
            liste.Add(a);
        }

        foreach (var s in vorlagenSchritte)
            if (s.WartetAufNr is { } nr && nr < s.Nr && jeNr.TryGetValue(nr, out var vorgaenger))
                jeNr[s.Nr].WartetAuf = vorgaenger;

        foreach (var m in manuelle)
        {
            liste.Add(new Aufgabe
            {
                Titel = m.Titel.Trim(),
                ZustaendigBereichId = m.BereichId,
                ZustaendigId = m.ZustaendigId,
                FristTage = m.FristTage,
                Herkunft = SchrittHerkunft.Manuell,
                WartetAuf = m.WartetAufNr is { } nr && jeNr.TryGetValue(nr, out var v) ? v : null,
            });
        }

        // Entfallene Vorgänger überspringen
        foreach (var a in liste)
            a.WartetAuf = NichtEntfallenerVorgaenger(a.WartetAuf);

        for (var i = 0; i < liste.Count; i++)
            liste[i].Reihenfolge = i + 1;

        FristenSetzen(liste, start);
        return liste;
    }

    /// <summary>
    /// Entfernt einen offenen Schritt aus dem laufenden Vorgang. Wer auf ihn wartet,
    /// wartet ab jetzt auf seinen Vorgänger (oder startet sofort). Liefert die dadurch
    /// freigegebenen Schritte.
    /// </summary>
    public static List<Aufgabe> Entfernen(Aufgabe schritt, IReadOnlyCollection<Aufgabe> alle, DateOnly heute)
    {
        if (FertigOderWeg(schritt))
            throw new InvalidOperationException("Der Schritt ist schon erledigt oder entfallen.");
        schritt.Status = AufgabenStatus.Entfallen;
        schritt.EntfallenGrund = GrundEntfernt;
        schritt.Wiedervorlage = null;
        foreach (var t in alle.Where(t => t.WartetAuf == schritt))
        {
            t.WartetAuf = schritt.WartetAuf;
            t.WartetAufId = schritt.WartetAuf?.Id;
        }
        return FristenSetzen(alle, heute);
    }

    /// <summary>
    /// Erledigt einen Schritt und liefert die Schritte, die dadurch in ihren Pool kommen.
    /// <paramref name="trotzSperre"/>: für das automatische Abhaken — ist die Rechnung in Odoo
    /// gebucht, ist der Schritt erledigt, auch wenn sein Vorgänger noch offen ist.
    /// </summary>
    public static List<Aufgabe> Erledigen(Aufgabe schritt, IReadOnlyCollection<Aufgabe> alle, DateOnly heute, DateTime jetzt, int? von,
        bool trotzSperre = false)
    {
        if (!trotzSperre && Gesperrt(schritt))
            throw new InvalidOperationException("Der Schritt wartet noch auf einen anderen Schritt.");
        schritt.Status = AufgabenStatus.Erledigt;
        schritt.ErledigtAm = jetzt;
        schritt.ErledigtVonId = von;
        schritt.Wiedervorlage = null;
        return FristenSetzen(alle, heute);
    }

    /// <summary>Hält <see cref="Vorgang.AbgeschlossenAm"/> aktuell; true, wenn der Vorgang gerade abgeschlossen wurde.</summary>
    public static bool AbschlussAktualisieren(Vorgang vorgang, DateOnly heute, DateTime jetzt)
    {
        var fertig = Status(vorgang.Schritte, heute) == VorgangsStatus.Abgeschlossen;
        if (fertig && vorgang.AbgeschlossenAm is null)
        {
            vorgang.AbgeschlossenAm = jetzt;
            return true;
        }
        if (!fertig)
            vorgang.AbgeschlossenAm = null;
        return false;
    }

    /// <summary>Bedingungen einer Vorlage (für die Häkchen beim Anlegen), in Schrittreihenfolge.</summary>
    public static List<string> Bedingungen(IEnumerable<VorgangstypSchritt> schritte) =>
        schritte.OrderBy(s => s.Nr)
            .Select(s => s.Bedingung?.Trim())
            .Where(b => !string.IsNullOrEmpty(b))
            .Select(b => b!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Prüft Name und Schritte einer Vorlage; null = in Ordnung, sonst die Fehlermeldung.</summary>
    public static string? VorlagePruefen(string? name, IReadOnlyList<VorgangstypSchritt> schritte)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            return "Der Vorgangstyp braucht einen Namen (höchstens 100 Zeichen).";
        if (schritte.Count == 0)
            return "Der Vorgangstyp braucht mindestens einen Schritt.";
        if (schritte.Count > MaxSchritte)
            return $"Höchstens {MaxSchritte} Schritte je Vorgangstyp.";
        foreach (var s in schritte)
        {
            var nr = $"Schritt {s.Nr}";
            if (string.IsNullOrWhiteSpace(s.Titel) || s.Titel.Trim().Length > 300)
                return $"{nr} braucht einen Titel (höchstens 300 Zeichen).";
            if (s.BereichId <= 0)
                return $"{nr} braucht einen Bereich.";
            if (s.FristTage is < 0 or > MaxFristTage)
                return $"{nr}: Die Frist muss zwischen 0 und {MaxFristTage} Tagen liegen.";
            if (s.WartetAufNr is { } w && (w < 1 || w >= s.Nr))
                return $"{nr} kann nur auf einen Schritt davor warten.";
            if (s.Bedingung is { Length: > 100 } || s.Pflichtfeld is { Length: > 100 })
                return $"{nr}: Bedingung und Pflichtfeld höchstens 100 Zeichen.";
        }
        return null;
    }

    private static Aufgabe? NichtEntfallenerVorgaenger(Aufgabe? v)
    {
        while (v is { Status: AufgabenStatus.Entfallen })
            v = v.WartetAuf;
        return v;
    }
}
