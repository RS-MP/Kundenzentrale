namespace Rausoft.Erp.Domain.Shared;

/// <summary>
/// Dateinamen aus fremden Werten (Odoo-Referenzen, Kundennummern, Mail-Anhänge)
/// bereinigen, bevor sie in einen Pfad eingehen: Pfadtrenner und ungültige
/// Zeichen werden ersetzt, führende/abschließende Punkte entfernt. Damit kann
/// ein Wert wie „..\..\x" nie aus dem Zielverzeichnis herausführen (SICHERHEIT.md D2/N5).
/// </summary>
public static class Dateinamen
{
    public static string Sicher(string? name, int maxLaenge = 150, string ersatz = "datei")
    {
        if (string.IsNullOrWhiteSpace(name))
            return ersatz;
        var ungueltig = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(ungueltig.Contains(c) || c is '/' or '\\' or ':' ? '_' : c);
        var s = sb.ToString().Trim().Trim('.').Trim();
        if (s.Length == 0 || s.All(c => c == '_'))
            return ersatz;
        return s.Length > maxLaenge ? s[..maxLaenge] : s;
    }
}
