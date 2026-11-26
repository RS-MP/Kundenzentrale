namespace Rausoft.Erp.Domain.Shared;

/// <summary>
/// Schutz der Ablagen im Testsystem: Die Verzeichnis-Einstellungen liegen in
/// der Datenbank — eine Testdatenbank aus einer Produktiv-Sicherung zeigt also
/// zunächst auf das echte Kunden Archiv. Im Testsystem dürfen alle Verzeichnisse
/// nur unterhalb der Ablage-Wurzel der Instanz (Instanz:AblageWurzel) liegen;
/// alles andere wird beim Start auf einen Unterordner der Wurzel umgestellt.
/// </summary>
public static class TestsystemSchutz
{
    /// <summary>Verzeichnis-Einstellungen und ihr Unterordner unter der Ablage-Wurzel.</summary>
    public static readonly IReadOnlyDictionary<string, string> Verzeichnisse = new Dictionary<string, string>
    {
        [EinstellungsSchluessel.VertragsablageVerzeichnis] = "Vertragsablage",
        [EinstellungsSchluessel.DokumentablageVerzeichnis] = "Dokumentablage",
        [EinstellungsSchluessel.VorlagenVerzeichnis] = "Vorlagen",
        [EinstellungsSchluessel.VertragserzeugungZielverzeichnis] = "Erzeugt",
        [EinstellungsSchluessel.VertraulicheAblageVerzeichnis] = "Vertraulich",
        [EinstellungsSchluessel.PreislisteVerzeichnis] = "Preisliste",
        [EinstellungsSchluessel.ZahlungenEingangVerzeichnis] = "Kontoauszuege",
    };

    /// <summary>Gilt die Einstellung als Pfad (Verzeichnis oder Datei), der im Testsystem geprüft wird?</summary>
    public static bool IstPfadEinstellung(string schluessel) =>
        Verzeichnisse.ContainsKey(schluessel)
        || schluessel == EinstellungsSchluessel.VertragserzeugungSignaturZertifikat;

    /// <summary>True, wenn <paramref name="pfad"/> die Wurzel selbst ist oder darunter liegt.</summary>
    public static bool LiegtUnter(string pfad, string wurzel)
    {
        try
        {
            var voll = Path.GetFullPath(pfad.Trim()).TrimEnd(Path.DirectorySeparatorChar);
            var basis = Path.GetFullPath(wurzel.Trim()).TrimEnd(Path.DirectorySeparatorChar);
            return voll.Equals(basis, StringComparison.OrdinalIgnoreCase)
                || voll.StartsWith(basis + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false; // nicht auflösbar = nicht nachweislich innerhalb
        }
    }

    /// <summary>
    /// Ersatzwert für eine Pfad-Einstellung im Testsystem; null, wenn der Wert
    /// bleiben darf (leer oder bereits unterhalb der Wurzel). Verzeichnisse
    /// wandern in ihren Unterordner der Wurzel, das Signatur-Zertifikat wird
    /// geleert (PDFs entstehen dann ohne Signatur).
    /// </summary>
    public static string? Ersatz(string schluessel, string? wert, string wurzel)
    {
        if (!IstPfadEinstellung(schluessel) || string.IsNullOrWhiteSpace(wert) || LiegtUnter(wert, wurzel))
            return null;
        return Verzeichnisse.TryGetValue(schluessel, out var unterordner)
            ? Path.Combine(wurzel.Trim(), unterordner)
            : string.Empty;
    }
}
