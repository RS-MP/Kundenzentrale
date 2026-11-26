namespace Rausoft.Erp.Web.Komponenten;

/// <summary>
/// Die Darstellung (hell, dunkel, automatisch) lässt sich an zwei Stellen umschalten: unten im
/// Menü und unter „Meine Einstellungen“. Das Signal hält beide Schalter gleich.
/// </summary>
public static class ThemaSignal
{
    /// <summary>Neue Darstellung: null = automatisch, sonst „light“ oder „dark“.</summary>
    public static event Action<string?>? Geaendert;

    public static void Melden(string? thema) => Geaendert?.Invoke(thema);
}
