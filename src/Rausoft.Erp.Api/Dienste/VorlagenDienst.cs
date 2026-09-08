using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Füllt Word-Vorlagen (.docx) mit Kundendaten. Unterstützt zwei Mechanismen:
/// {{Platzhalter}} für neue Vorlagen und die Muster-Texte der bestehenden
/// Lizenzbedingungen-Vorlage ("Muster GmbH", "xxxxx" …). Ersetzt wird je
/// Absatz über Textlauf-Grenzen hinweg (Word zerteilt Texte intern beliebig).
/// PDF-Export folgt mit Telerik Document Processing (Stufe 1b).
/// </summary>
public static class VorlagenDienst
{
    /// <summary>
    /// Ersetzt die Schlüssel in Fließtext, Kopf- und Fußzeilen.
    /// Schlüssel werden in absteigender Länge ersetzt (längere zuerst),
    /// damit sich überlappende Muster nicht gegenseitig zerstören.
    /// Liefert alle danach noch offenen {{Platzhalter}} zurück.
    /// </summary>
    public static List<string> Fuellen(Stream dokument, IReadOnlyDictionary<string, string> werte)
    {
        using var doc = WordprocessingDocument.Open(dokument, true);
        var sortiert = werte.OrderByDescending(w => w.Key.Length).ToList();

        var wurzeln = new List<OpenXmlPartRootElement?>
        {
            doc.MainDocumentPart?.Document,
        };
        if (doc.MainDocumentPart is not null)
        {
            wurzeln.AddRange(doc.MainDocumentPart.HeaderParts.Select(p => (OpenXmlPartRootElement?)p.Header));
            wurzeln.AddRange(doc.MainDocumentPart.FooterParts.Select(p => (OpenXmlPartRootElement?)p.Footer));
        }

        var offen = new HashSet<string>();
        foreach (var wurzel in wurzeln.Where(w => w is not null))
        {
            foreach (var absatz in wurzel!.Descendants<Paragraph>())
                ErsetzeImAbsatz(absatz, sortiert, offen);
            wurzel.Save();
        }
        return offen.OrderBy(o => o).ToList();
    }

    private static void ErsetzeImAbsatz(
        Paragraph absatz, List<KeyValuePair<string, string>> werte, HashSet<string> offen)
    {
        var texte = absatz.Descendants<Text>().ToList();
        if (texte.Count == 0)
            return;

        var gesamt = string.Concat(texte.Select(t => t.Text));
        var neu = gesamt;
        foreach (var (schluessel, wert) in werte)
            neu = neu.Replace(schluessel, wert, StringComparison.Ordinal);

        foreach (System.Text.RegularExpressions.Match rest in
                 System.Text.RegularExpressions.Regex.Matches(neu, @"\{\{[^}]{1,60}\}\}"))
            offen.Add(rest.Value);

        if (neu == gesamt)
            return;

        // Gesamten Absatztext in den ersten Textlauf legen; die Zeichenformatierung
        // des ersten Laufs gilt dann für den ganzen Absatz (nur bei Treffer-Absätzen).
        texte[0].Text = neu;
        texte[0].Space = SpaceProcessingModeValues.Preserve;
        foreach (var text in texte.Skip(1))
            text.Text = string.Empty;
    }
}
