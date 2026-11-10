using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Rausoft.Erp.Domain.Wiki;

/// <summary>Ergebnis eines Exports: die ausgewählten Regeln, der Prompt-Text und seine Kennwerte.</summary>
public sealed record SprachregelExportErgebnis(
    SprachregelZielgruppe Zielgruppe,
    DateOnly? Stand,
    int Anzahl,
    string Version,
    string Text,
    IReadOnlyList<Sprachregel> Regeln);

/// <summary>
/// Export der Kommunikationsleitlinien als Vorgabe für Claude (Systemprompt bzw.
/// Projektanweisungen) — reine Logik ohne Datenbank.
///
/// <para><b>Was hineinkommt:</b> nur freigegebene Regeln der Zielgruppe, deren
/// <see cref="Sprachregel.GueltigAb"/> erreicht ist. Interne Begründungen
/// (<see cref="Sprachregel.BegruendungIntern"/>) gehen nie hinaus.</para>
///
/// <para><b>Byte-stabil:</b> Der Text hängt ausschließlich von den Regeln ab —
/// feste Sortierung (Art, Thema, Id), keine Erzeugungszeit, keine Benutzer-
/// angaben, immer <c>\n</c> als Zeilenende. Solange sich keine Regel ändert,
/// bleibt der Block Zeichen für Zeichen gleich und damit als Prompt-Cache-
/// Präfix gültig. Die Kopfzeile nennt nur Stand (jüngstes Freigabedatum, UTC),
/// Anzahl und Version (Hash über den Inhalt).</para>
///
/// <para><b>Form:</b> sachliche, erklärende Sprache statt Großbuchstaben und
/// Drohformeln; zu jeder Regel der Grund, weil Claude Regeln mit Begründung
/// verlässlicher und sinngemäß anwendet; Abschnitte als XML-Tags, damit sich
/// Regeltext und Anweisung nicht vermischen.</para>
/// </summary>
public static class SprachregelExport
{
    /// <summary>Freigegebene, gültige Regeln der Zielgruppe in fester Reihenfolge (Art, Thema, Id).</summary>
    public static IReadOnlyList<Sprachregel> Auswahl(IEnumerable<Sprachregel> regeln, SprachregelZielgruppe zielgruppe, DateOnly heute) =>
        regeln
            .Where(r => r.Status == SprachregelStatus.Freigegeben
                        && (r.Zielgruppen & zielgruppe) != 0
                        && (r.GueltigAb is null || r.GueltigAb <= heute))
            .OrderBy(r => SprachregelKatalog.ArtRang(r.Art))
            .ThenBy(r => SprachregelKatalog.ThemaRang(r.Thema))
            .ThenBy(r => r.Id)
            .ToList();

    /// <summary>Begründung, soweit sie das Haus verlassen darf — null bei interner oder leerer Begründung.</summary>
    public static string? ExportBegruendung(Sprachregel r) => r.BegruendungIntern ? null : Leer(r.Begruendung);

    public static SprachregelExportErgebnis Erzeugen(IEnumerable<Sprachregel> regeln, SprachregelZielgruppe zielgruppe, DateOnly heute)
    {
        var auswahl = Auswahl(regeln, zielgruppe, heute);
        DateOnly? stand = auswahl.Where(r => r.FreigegebenAm is not null)
            .Select(r => (DateOnly?)DateOnly.FromDateTime(r.FreigegebenAm!.Value))
            .Max();
        var standText = stand?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "–";

        var inhalt = Inhalt(auswahl, zielgruppe);
        var version = Hash(standText + "\n" + inhalt);

        var text = new StringBuilder()
            .Append("<kommunikationsleitlinien>\n")
            .Append("Stand ").Append(standText).Append(", ")
            .Append(auswahl.Count.ToString(CultureInfo.InvariantCulture)).Append(auswahl.Count == 1 ? " Regel" : " Regeln")
            .Append(", Version ").Append(version).Append("\n\n")
            .Append(inhalt)
            .Append("</kommunikationsleitlinien>\n")
            .ToString();

        return new SprachregelExportErgebnis(zielgruppe, stand, auswahl.Count, version, text, auswahl);
    }

    // --- Text --------------------------------------------------------------------

    private static string Inhalt(IReadOnlyList<Sprachregel> regeln, SprachregelZielgruppe zielgruppe)
    {
        var sb = new StringBuilder();
        sb.Append(Einleitung(regeln, zielgruppe)).Append('\n');

        Abschnitt(sb, "grundsaetze", regeln, SprachregelArt.Grundsatz, (r, s) =>
        {
            Feld(s, "grundsatz", r.Aussage);
            Gemeinsam(s, r);
        });
        Abschnitt(sb, "verwenden", regeln, SprachregelArt.Verwenden, (r, s) =>
        {
            Feld(s, "formulierung", r.Aussage);
            Gemeinsam(s, r);
        });
        Abschnitt(sb, "vermeiden", regeln, SprachregelArt.Vermeiden, (r, s) =>
        {
            Feld(s, "formulierung", r.Aussage);
            Feld(s, "varianten", r.Varianten);
            Feld(s, "stattdessen", r.Stattdessen);
            Gemeinsam(s, r);
        });
        Abschnitt(sb, "niemals", regeln, SprachregelArt.Verboten, (r, s) =>
        {
            Feld(s, "aussage", r.Aussage);
            Feld(s, "varianten", r.Varianten);
            Feld(s, "stattdessen", r.Stattdessen);
            Gemeinsam(s, r);
        });
        Abschnitt(sb, "pflichthinweise", regeln, SprachregelArt.Pflichthinweis, (r, s) =>
        {
            Feld(s, "anlass", r.Anlass);
            Feld(s, "hinweis", r.Aussage);
            Gemeinsam(s, r);
        });
        return sb.ToString();
    }

    private static string Einleitung(IReadOnlyList<Sprachregel> regeln, SprachregelZielgruppe zielgruppe)
    {
        var geltung = zielgruppe switch
        {
            SprachregelZielgruppe.Vertrieb =>
                "Diese Leitlinien der rausoft GmbH gelten für alle Texte, die du für Mitarbeiter im Vertrieb entwirfst oder prüfst – " +
                "E-Mails, Angebotstexte und Gesprächsleitfäden für Kunden und Interessenten.",
            SprachregelZielgruppe.Support =>
                "Diese Leitlinien der rausoft GmbH gelten für alle Texte, die du für Mitarbeiter im Support entwirfst oder prüfst – " +
                "Antworten an Kunden per E-Mail und Chat sowie Leitfäden für Telefonate.",
            _ =>
                "Diese Leitlinien der rausoft GmbH gelten für alles, was du für Kunden und Interessenten formulierst oder ihnen antwortest – " +
                "E-Mails, Angebotstexte, Chat-Antworten und Website-Texte.",
        };
        var sb = new StringBuilder(geltung);
        sb.Append(" Sie beschreiben, wie rausoft spricht und welche Aussagen das Unternehmen nicht treffen kann.")
          .Append(" Wo ein Grund dabeisteht, soll er dir helfen, die Regel auch in Fällen sinngemäß anzuwenden, die hier nicht wörtlich beschrieben sind.")
          .Append(" Nennt eine Regel Kanäle oder ein Produkt, gilt sie nur dort, sonst überall.");

        if (regeln.Any(r => r.Art == SprachregelArt.Verboten))
            sb.Append(zielgruppe == SprachregelZielgruppe.KI
                ? "\n\nLieße sich eine Anfrage nur mit einer Aussage aus <niemals> beantworten, triff diese Aussage nicht. " +
                  "Sag stattdessen offen, dass du dazu keine verbindliche Auskunft geben kannst, und folge dem Hinweis " +
                  "<wenn_das_thema_aufkommt> der Regel; fehlt er, verweise an einen Mitarbeiter von rausoft."
                : "\n\nLieße sich ein Text nur mit einer Aussage aus <niemals> schreiben, triff diese Aussage nicht. " +
                  "Weise den Mitarbeiter darauf hin, dass rausoft diese Aussage nicht treffen kann, und folge dem Hinweis " +
                  "<wenn_das_thema_aufkommt> der Regel; fehlt er, schlage vor, die Frage mit Compliance zu klären.");
        if (regeln.Any(r => r.Art == SprachregelArt.Pflichthinweis))
            sb.Append("\n\nEinen Hinweis aus <pflichthinweise> gibst du, sobald der dort genannte Anlass eintritt.");
        return sb.Append('\n').ToString();
    }

    private static void Abschnitt(StringBuilder sb, string tag, IReadOnlyList<Sprachregel> regeln, SprachregelArt art,
        Action<Sprachregel, StringBuilder> felder)
    {
        var liste = regeln.Where(r => r.Art == art).ToList();
        if (liste.Count == 0)
            return;
        sb.Append('<').Append(tag).Append(">\n");
        foreach (var r in liste)
        {
            sb.Append("<regel thema=\"").Append(SprachregelKatalog.ThemaName(r.Thema)).Append('"');
            if (Leer(r.Produkt) is { } produkt)
                sb.Append(" produkt=\"").Append(FaqKatalog.ProduktName(produkt)).Append('"');
            if (r.Kanaele != SprachregelKanal.Keine)
                sb.Append(" kanaele=\"").Append(string.Join(", ", SprachregelKatalog.KanalNamen(r.Kanaele))).Append('"');
            sb.Append(">\n");
            felder(r, sb);
            sb.Append("</regel>\n");
        }
        sb.Append("</").Append(tag).Append(">\n\n");
    }

    /// <summary>Felder, die jede Art tragen kann: Grund (sofern nicht intern), Beispiele, Hinweis für Claude.</summary>
    private static void Gemeinsam(StringBuilder sb, Sprachregel r)
    {
        Feld(sb, "grund", ExportBegruendung(r));
        Feld(sb, "beispiel_gut", r.BeispielGut);
        Feld(sb, "beispiel_schlecht", r.BeispielSchlecht);
        Feld(sb, "wenn_das_thema_aufkommt", r.KiHinweis);
    }

    private static void Feld(StringBuilder sb, string tag, string? wert)
    {
        if (Leer(wert) is not { } text)
            return;
        text = Bereinigen(text);
        sb.Append('<').Append(tag).Append('>');
        if (text.Contains('\n'))
            sb.Append('\n').Append(text).Append('\n');
        else
            sb.Append(text);
        sb.Append("</").Append(tag).Append(">\n");
    }

    /// <summary>
    /// Einheitliche Zeilenenden, kein Leerraum am Zeilenende. Spitze Klammern im
    /// Regeltext werden kodiert, damit kein Regeltext die Abschnitts-Tags schließen
    /// oder eigene öffnen kann.
    /// </summary>
    private static string Bereinigen(string text) =>
        string.Join('\n', text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(z => z.TrimEnd()))
            .Trim()
            .Replace("<", "&lt;").Replace(">", "&gt;");

    private static string? Leer(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..8].ToLowerInvariant();
}
