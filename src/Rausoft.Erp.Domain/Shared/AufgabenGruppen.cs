namespace Rausoft.Erp.Domain.Shared;

/// <summary>Gruppen der Liste „Meine Aufgaben": überfällig · heute · nächste 7 Tage · später/ohne Datum · wartet auf Kunden.</summary>
public static class AufgabenGruppen
{
    public const string Ueberfaellig = "ueberfaellig";
    public const string Heute = "heute";
    public const string Woche = "woche";
    public const string Spaeter = "spaeter";
    public const string Wartet = "wartet";

    /// <param name="datum">Frist bzw. bei „Wartet auf Kunden" die Wiedervorlage.</param>
    /// <param name="wartetNoch">Wartet auf den Kunden und die Wiedervorlage ist noch nicht erreicht.</param>
    public static string Gruppe(DateOnly? datum, bool wartetNoch, DateOnly heute) =>
        wartetNoch ? Wartet
        : datum is not { } d ? Spaeter
        : d < heute ? Ueberfaellig
        : d == heute ? Heute
        : d <= heute.AddDays(7) ? Woche
        : Spaeter;

    public static int Reihenfolge(string gruppe) => gruppe switch
    {
        Ueberfaellig => 0,
        Heute => 1,
        Woche => 2,
        Spaeter => 3,
        _ => 4,
    };
}
