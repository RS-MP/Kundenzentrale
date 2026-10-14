namespace Rausoft.Erp.Web.Komponenten;

/// <summary>
/// Signal „im Wiki hat sich etwas für mich geändert" (Neuerung bestätigt,
/// Meldung erfasst/erledigt) — die Navigation lädt daraufhin ihr Abzeichen neu.
/// </summary>
public static class WikiHinweisSignal
{
    public static event Action? Geaendert;

    public static void Melden() => Geaendert?.Invoke();
}
