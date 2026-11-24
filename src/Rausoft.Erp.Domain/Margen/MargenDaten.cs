using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Domain.Margen;

/// <summary>Die beiden World-Check-Bereiche, für die der Margenrechner rechnet.</summary>
public enum MargenBereich
{
    SanctionsOnly = 0,
    FullFile = 1,
}

/// <summary>Ein Basket: Screening Cases pro Jahr je Kunde und der Verkaufspreis pro Monat je Bereich.</summary>
public record MargenBasket(long Cases, double VkSanctionsOnly, double VkFullFile);

/// <summary>Eine Stufe der Einkaufsstaffel: gilt bis zu diesem Gesamtvolumen (Cases pro Jahr), Monatsgebühr je Bereich.</summary>
public record MargenStaffelStufe(long BisCases, double GebuehrFullFile, double GebuehrSanctionsOnly);

/// <summary>Stützpunkt der linearen Staffel oberhalb der Tabelle: Monatsgebühr bei diesem Gesamtvolumen.</summary>
public record MargenAnker(long Cases, double Gebuehr);

/// <summary>
/// Alles, womit der Margenrechner rechnet — Verkaufspreise je Basket und die
/// Einkaufsstaffel des Anbieters. Wird von Hand gepflegt (Entscheidung MP
/// 24.11.2026) und liegt verschlüsselt in <see cref="MargenrechnerStand"/>;
/// im Programm selbst stehen keine Preise.
///
/// Staffel: bis zur letzten Tabellenstufe gilt die Tabelle (zwischen zwei
/// Stufen die höhere). Darüber laufen Stufen im Abstand
/// <see cref="LinearSchritt"/> weiter; die Gebühr steigt linear von Anker zu
/// Anker und oberhalb des letzten Ankers mit dessen Preis je Case. Beide
/// Bereiche kosten dort gleich viel.
/// </summary>
public record MargenDaten(
    List<MargenBasket> Baskets,
    List<MargenStaffelStufe> Staffel,
    long LinearSchritt,
    List<MargenAnker> LinearAnker,
    string? Stand = null,
    string? Hinweis = null)
{
    public const int MaxBaskets = 40;
    public const int MaxStufen = 500;
    public const int MaxAnker = 20;
    /// <summary>Untergrenzen, damit die Schleifen über Stufen und Kundenzahlen beschränkt bleiben.</summary>
    public const long MinBasket = 100;
    public const long MinSchritt = 10_000;
    public const double MaxBetrag = 100_000_000;
    public const int MaxTextLaenge = 500;

    /// <summary>Prüft die Daten; leere Liste = in Ordnung. Texte für die Anzeige beim Speichern.</summary>
    public List<string> Pruefen()
    {
        var f = new List<string>();

        // Aus einer Anfrage können Listen fehlen oder leere Einträge enthalten
        if (Baskets is null || Staffel is null || LinearAnker is null
            || Baskets.Any(b => b is null) || Staffel.Any(s => s is null) || LinearAnker.Any(a => a is null))
            return ["Baskets, Einkaufsstaffel oder Anker fehlen."];

        if (Baskets.Count == 0)
            f.Add("Es ist kein Basket eingetragen.");
        else
        {
            if (Baskets.Count > MaxBaskets)
                f.Add($"Höchstens {MaxBaskets} Baskets.");
            if (Baskets.Any(b => b.Cases < MinBasket || b.Cases > Margenrechnung.Horizont))
                f.Add($"Baskets müssen zwischen {MinBasket:N0} und {Margenrechnung.Horizont:N0} Cases liegen.");
            if (!Aufsteigend(Baskets.Select(b => b.Cases)))
                f.Add("Baskets müssen aufsteigend und eindeutig sein.");
            if (Baskets.Any(b => !Betrag(b.VkSanctionsOnly) || !Betrag(b.VkFullFile)))
                f.Add("Verkaufspreise dürfen nicht negativ sein.");
        }

        if (Staffel.Count == 0)
            f.Add("Die Einkaufsstaffel ist leer.");
        else
        {
            if (Staffel.Count > MaxStufen)
                f.Add($"Höchstens {MaxStufen} Stufen in der Einkaufsstaffel.");
            if (Staffel.Any(s => s.BisCases <= 0 || s.BisCases > Margenrechnung.Horizont))
                f.Add($"Stufen der Einkaufsstaffel müssen zwischen 1 und {Margenrechnung.Horizont:N0} Cases liegen.");
            if (!Aufsteigend(Staffel.Select(s => s.BisCases)))
                f.Add("Die Einkaufsstaffel muss nach Cases aufsteigend und eindeutig sein.");
            if (Staffel.Any(s => !Betrag(s.GebuehrFullFile) || !Betrag(s.GebuehrSanctionsOnly)))
                f.Add("Gebühren der Einkaufsstaffel dürfen nicht negativ sein.");
        }

        if (LinearSchritt < MinSchritt || LinearSchritt > Margenrechnung.Horizont)
            f.Add($"Die Schrittweite der linearen Staffel muss mindestens {MinSchritt:N0} Cases betragen.");

        if (LinearAnker.Count == 0)
            f.Add("Die lineare Staffel braucht mindestens einen Anker (Gebühr am Ende der Tabelle).");
        else
        {
            if (LinearAnker.Count > MaxAnker)
                f.Add($"Höchstens {MaxAnker} Anker.");
            if (!Aufsteigend(LinearAnker.Select(a => a.Cases)))
                f.Add("Die Anker müssen nach Cases aufsteigend und eindeutig sein.");
            if (LinearAnker.Any(a => a.Cases <= 0 || !Betrag(a.Gebuehr) || a.Gebuehr <= 0))
                f.Add("Anker brauchen Cases und eine Gebühr größer null.");
            if (Staffel is { Count: > 0 } && LinearAnker[0].Cases != Staffel[^1].BisCases)
                f.Add("Der erste Anker muss bei der letzten Stufe der Tabelle liegen (dort beginnt die lineare Staffel).");
        }

        if (Stand is { Length: > 100 })
            f.Add("Der Stand darf höchstens 100 Zeichen lang sein.");
        if (Hinweis is { Length: > MaxTextLaenge })
            f.Add($"Der Hinweis darf höchstens {MaxTextLaenge} Zeichen lang sein.");

        return f;
    }

    private static bool Betrag(double wert) => double.IsFinite(wert) && wert >= 0 && wert <= MaxBetrag;

    private static bool Aufsteigend(IEnumerable<long> werte)
    {
        long? vorher = null;
        foreach (var w in werte)
        {
            if (vorher is not null && w <= vorher)
                return false;
            vorher = w;
        }
        return true;
    }
}

/// <summary>
/// Gespeicherter Stand der Margenrechner-Daten. Jede Änderung ist eine neue
/// Zeile, der jüngste Stand gilt. Die Daten (Einkaufspreise!) liegen nur
/// verschlüsselt in der Datenbank und werden nicht ins Audit-Log geschrieben.
/// </summary>
public class MargenrechnerStand
{
    public int Id { get; set; }

    /// <summary><see cref="MargenDaten"/> als JSON, per Data Protection verschlüsselt.</summary>
    public required string DatenVerschluesselt { get; set; }

    public DateTime GeaendertAm { get; set; }
    public int? GeaendertVonId { get; set; }
    public Mitarbeiter? GeaendertVon { get; set; }
}

/// <summary>
/// Wer darf den Margenrechner sehen und seine Daten pflegen? Nur die Rolle
/// <see cref="MitarbeiterRolle.Margenrechner"/> — Admin, Vertrieb und
/// Compliance genügen bewusst nicht (Entscheidung MP 24.11.2026: nur
/// ausgewählte Personen).
/// </summary>
public static class MargenrechnerZugang
{
    public static bool Darf(MitarbeiterRolle rollen) => rollen.HasFlag(MitarbeiterRolle.Margenrechner);
}
