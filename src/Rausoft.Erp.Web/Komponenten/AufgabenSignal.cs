namespace Rausoft.Erp.Web.Komponenten;

/// <summary>
/// Signal „an meinen Aufgaben hat sich etwas geändert" (übernommen, erledigt, abgeglichen) —
/// die Navigation lädt daraufhin das Abzeichen an „Meine Aufgaben" neu.
/// </summary>
public static class AufgabenSignal
{
    public static event Action? Geaendert;

    public static void Melden() => Geaendert?.Invoke();
}
