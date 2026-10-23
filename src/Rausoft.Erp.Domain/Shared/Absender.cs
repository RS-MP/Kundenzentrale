namespace Rausoft.Erp.Domain.Shared;

/// <summary>Anzeige von E-Mail-Absendern aus fremden Systemen (z. B. Odoo email_from).</summary>
public static class Absender
{
    /// <summary>
    /// „"Daniel Stauch" &lt;stauch@example.de&gt;" → „Daniel Stauch"; ohne Namen bleibt die
    /// Adresse („stauch@example.de"). Leer → null.
    /// </summary>
    public static string? Anzeigename(string? absender)
    {
        if (string.IsNullOrWhiteSpace(absender))
            return null;
        var text = absender.Trim();
        var spitz = text.IndexOf('<');
        if (spitz > 0)
        {
            var name = text[..spitz].Trim().Trim('"', '\'').Trim();
            if (name.Length > 0)
                return name;
        }
        return text.Trim('<', '>', '"', '\'', ' ');
    }
}
