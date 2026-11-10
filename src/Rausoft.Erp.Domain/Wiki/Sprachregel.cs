using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Domain.Wiki;

/// <summary>
/// Kommunikationsleitlinie (Wiki-Thema „leitlinien"): eine Sprachregel, die
/// Vertrieb und Support lesen und bestätigen und die Claude – das eingesetzte
/// KI-System – als Vorgabe bekommt. Entscheidungen MP 10.11.2026: ein
/// gemeinsamer Regelbestand mit Zielgruppen-Kennzeichen; freigeben dürfen nur
/// Compliance und Admin; Lesebestätigung ist Pflicht für verbotene Aussagen
/// und Pflichthinweise (<see cref="SprachregelRegeln"/>).
/// Eine freigegebene Regel wird nie überschrieben: Ändern heißt neu fassen
/// (<see cref="VorgaengerId"/>) — der freigegebene Wortlaut bleibt nachvollziehbar.
/// </summary>
public class Sprachregel
{
    public int Id { get; set; }

    public SprachregelArt Art { get; set; }

    /// <summary>Begriff, Formulierung oder Grundsatz — der Kern der Regel.</summary>
    public required string Aussage { get; set; }

    /// <summary>Was stattdessen gesagt wird (Pflicht bei <see cref="SprachregelArt.Vermeiden"/>).</summary>
    public string? Stattdessen { get; set; }

    /// <summary>In welcher Situation der Hinweis fällig ist (Pflicht bei <see cref="SprachregelArt.Pflichthinweis"/>, sonst leer).</summary>
    public string? Anlass { get; set; }

    /// <summary>Warum die Regel gilt (WikiFormat; Pflicht bei <see cref="SprachregelArt.Verboten"/>).</summary>
    public string? Begruendung { get; set; }

    /// <summary>true: Die Begründung ist nur für Mitarbeiter — sie geht in keinen Export.</summary>
    public bool BegruendungIntern { get; set; }

    public string? BeispielGut { get; set; }
    public string? BeispielSchlecht { get; set; }

    /// <summary>
    /// Schreibweisen und Wortformen, kommagetrennt und bereinigt (z. B.
    /// „garantieren, Garantie") — Andockstelle für einen späteren Textprüfer.
    /// </summary>
    public string? Varianten { get; set; }

    public SprachregelZielgruppe Zielgruppen { get; set; }

    /// <summary>Schlüssel aus <see cref="SprachregelKatalog.Themen"/>.</summary>
    public required string Thema { get; set; }

    /// <summary>Schlüssel aus <see cref="FaqKatalog.Produkte"/>; leer = gilt produktübergreifend.</summary>
    public string? Produkt { get; set; }

    /// <summary>Kanäle, für die die Regel gilt; <see cref="SprachregelKanal.Keine"/> = alle.</summary>
    public SprachregelKanal Kanaele { get; set; }

    /// <summary>Was Claude tun soll, wenn das Thema aufkommt (z. B. „an den Vertrieb verweisen").</summary>
    public string? KiHinweis { get; set; }

    public SprachregelStatus Status { get; set; } = SprachregelStatus.Entwurf;

    /// <summary>Ab wann die Regel gilt; leer = ab Freigabe. Vorher erscheint sie in keinem Export.</summary>
    public DateOnly? GueltigAb { get; set; }

    /// <summary>
    /// Regel, deren Neufassung dieser Eintrag ist. Bewusst ohne Fremdschlüssel:
    /// der Verweis ist nur Herkunftshinweis, der Vorgänger darf (zurückgezogen)
    /// gelöscht werden.
    /// </summary>
    public int? VorgaengerId { get; set; }

    public DateTime ErstelltAm { get; set; }
    public int? ErstelltVonId { get; set; }
    public Mitarbeiter? ErstelltVon { get; set; }

    public DateTime? GeaendertAm { get; set; }
    public int? GeaendertVonId { get; set; }
    public Mitarbeiter? GeaendertVon { get; set; }

    public DateTime? FreigegebenAm { get; set; }
    public int? FreigegebenVonId { get; set; }
    public Mitarbeiter? FreigegebenVon { get; set; }

    public DateTime? ZurueckgezogenAm { get; set; }
    public int? ZurueckgezogenVonId { get; set; }
    public Mitarbeiter? ZurueckgezogenVon { get; set; }

    public byte[]? RowVersion { get; set; }
}

/// <summary>Werte stehen in der Datenbank — nie umnummerieren.</summary>
public enum SprachregelArt
{
    /// <summary>So sagen wir es.</summary>
    Verwenden = 0,
    /// <summary>Besser nicht — mit Alternative.</summary>
    Vermeiden = 1,
    /// <summary>Darf nie gesagt werden.</summary>
    Verboten = 2,
    /// <summary>Muss in bestimmten Situationen gesagt werden.</summary>
    Pflichthinweis = 3,
    /// <summary>Tonalität, Anrede, Stil.</summary>
    Grundsatz = 4,
}

[Flags]
public enum SprachregelZielgruppe
{
    Keine = 0,
    Vertrieb = 1,
    Support = 2,
    /// <summary>Claude (Vorgabe per Export).</summary>
    KI = 4,
}

[Flags]
public enum SprachregelKanal
{
    /// <summary>Keine Einschränkung — gilt in allen Kanälen.</summary>
    Keine = 0,
    EMail = 1,
    Telefon = 2,
    Angebot = 4,
    Website = 8,
    Chat = 16,
}

public enum SprachregelStatus
{
    /// <summary>Vorschlag — gilt noch nicht, für alle sichtbar und gekennzeichnet.</summary>
    Entwurf = 0,
    /// <summary>Von Compliance oder Admin freigegeben — der Wortlaut ändert sich nicht mehr.</summary>
    Freigegeben = 1,
    /// <summary>Gilt nicht mehr — bleibt zur Nachvollziehbarkeit erhalten.</summary>
    Zurueckgezogen = 2,
}

/// <summary>
/// Feste Auswahllisten der Leitlinien. Neue Werte am Ende ergänzen — die
/// Reihenfolge bestimmt die Sortierung im Export (und damit dessen Version),
/// die Schlüssel stehen in der Datenbank.
/// </summary>
public static class SprachregelKatalog
{
    public static readonly (string Schluessel, string Name)[] Themen =
    [
        ("produktleistung", "Produktleistung"),
        ("preise", "Preise und Rabatte"),
        ("recht", "Recht und Compliance"),
        ("wettbewerb", "Wettbewerb"),
        ("datenschutz", "Datenschutz und Sicherheit"),
        ("verfuegbarkeit", "Verfügbarkeit und Fristen"),
        ("ton", "Ton und Anrede"),
        ("sonstiges", "Sonstiges"),
    ];

    /// <summary>Reihenfolge der Bereiche auf der Seite und im Export.</summary>
    public static readonly (SprachregelArt Art, string Name)[] Arten =
    [
        (SprachregelArt.Grundsatz, "Grundsätze"),
        (SprachregelArt.Verwenden, "So sagen wir es"),
        (SprachregelArt.Vermeiden, "Besser vermeiden"),
        (SprachregelArt.Verboten, "Niemals"),
        (SprachregelArt.Pflichthinweis, "Pflichthinweise"),
    ];

    public static readonly (SprachregelZielgruppe Wert, string Schluessel, string Name)[] Zielgruppen =
    [
        (SprachregelZielgruppe.Vertrieb, "vertrieb", "Vertrieb"),
        (SprachregelZielgruppe.Support, "support", "Support"),
        (SprachregelZielgruppe.KI, "ki", "KI (Claude)"),
    ];

    public static readonly (SprachregelKanal Wert, string Schluessel, string Name)[] Kanaele =
    [
        (SprachregelKanal.EMail, "email", "E-Mail"),
        (SprachregelKanal.Telefon, "telefon", "Telefon"),
        (SprachregelKanal.Angebot, "angebot", "Angebot"),
        (SprachregelKanal.Website, "website", "Website"),
        (SprachregelKanal.Chat, "chat", "Chat"),
    ];

    public static bool IstThema(string? schluessel) => Themen.Any(t => t.Schluessel == schluessel);

    public static string ThemaName(string schluessel) =>
        Themen.FirstOrDefault(t => t.Schluessel == schluessel).Name ?? schluessel;

    /// <summary>Position des Themas im Katalog (unbekannte Schlüssel ans Ende).</summary>
    public static int ThemaRang(string schluessel)
    {
        var index = Array.FindIndex(Themen, t => t.Schluessel == schluessel);
        return index < 0 ? int.MaxValue : index;
    }

    /// <summary>Position der Art in der Bereichsreihenfolge.</summary>
    public static int ArtRang(SprachregelArt art)
    {
        var index = Array.FindIndex(Arten, a => a.Art == art);
        return index < 0 ? int.MaxValue : index;
    }

    public static SprachregelZielgruppe? Zielgruppe(string? schluessel)
    {
        var treffer = Zielgruppen.FirstOrDefault(z => string.Equals(z.Schluessel, schluessel, StringComparison.OrdinalIgnoreCase));
        return treffer.Schluessel is null ? null : treffer.Wert;
    }

    public static string ZielgruppeName(SprachregelZielgruppe wert) =>
        Zielgruppen.FirstOrDefault(z => z.Wert == wert).Name ?? wert.ToString();

    public static List<string> ZielgruppenSchluessel(SprachregelZielgruppe wert) =>
        Zielgruppen.Where(z => wert.HasFlag(z.Wert)).Select(z => z.Schluessel).ToList();

    public static List<string> KanalSchluessel(SprachregelKanal wert) =>
        Kanaele.Where(k => wert.HasFlag(k.Wert)).Select(k => k.Schluessel).ToList();

    public static List<string> KanalNamen(SprachregelKanal wert) =>
        Kanaele.Where(k => wert.HasFlag(k.Wert)).Select(k => k.Name).ToList();

    /// <summary>Schlüssel → Flags; null, wenn ein unbekannter Schlüssel dabei ist.</summary>
    public static SprachregelZielgruppe? ZielgruppenAus(IEnumerable<string>? schluessel)
    {
        var wert = SprachregelZielgruppe.Keine;
        foreach (var s in schluessel ?? [])
        {
            if (Zielgruppe(s) is not { } z)
                return null;
            wert |= z;
        }
        return wert;
    }

    /// <summary>Schlüssel → Flags; null, wenn ein unbekannter Schlüssel dabei ist.</summary>
    public static SprachregelKanal? KanaeleAus(IEnumerable<string>? schluessel)
    {
        var wert = SprachregelKanal.Keine;
        foreach (var s in schluessel ?? [])
        {
            var treffer = Kanaele.FirstOrDefault(k => string.Equals(k.Schluessel, s, StringComparison.OrdinalIgnoreCase));
            if (treffer.Schluessel is null)
                return null;
            wert |= treffer.Wert;
        }
        return wert;
    }
}

/// <summary>
/// Wer darf was bei den Kommunikationsleitlinien? Lesen alle aktiven
/// Mitarbeiter; vorschlagen (als Entwurf anlegen) jeder; eigene Entwürfe ändern
/// und löschen der Ersteller. Freigeben, zurückziehen, fremde Entwürfe ändern
/// und löschen: ausschließlich Admin oder Compliance — für alle Arten, die
/// Wiki-Redaktion zählt hier nicht (Entscheidung MP 10.11.2026).
/// Reine Logik ohne Datenbank — die API ruft sie mit dem Mitarbeiter-Kontext auf.
/// </summary>
public static class SprachregelRegeln
{
    /// <summary>Freigeben, zurückziehen, fremde Entwürfe ändern und löschen.</summary>
    public static bool DarfFreigeben(MitarbeiterRolle rollen) =>
        (rollen & (MitarbeiterRolle.Admin | MitarbeiterRolle.Compliance)) != 0;

    /// <summary>
    /// Inhalt ändern geht nur am Entwurf: der Ersteller oder wer freigeben darf.
    /// Freigegebene und zurückgezogene Regeln ändert niemand — sie werden neu gefasst.
    /// </summary>
    public static bool DarfBearbeiten(MitarbeiterRolle rollen, int eigeneId, int? erstelltVonId, SprachregelStatus status) =>
        status == SprachregelStatus.Entwurf && (erstelltVonId == eigeneId || DarfFreigeben(rollen));

    /// <summary>
    /// Löschen: Entwürfe der Ersteller oder wer freigeben darf; zurückgezogene
    /// Regeln nur wer freigeben darf. Eine geltende Regel löscht niemand — sie
    /// wird zurückgezogen, damit die Betroffenen davon erfahren.
    /// </summary>
    public static bool DarfLoeschen(MitarbeiterRolle rollen, int eigeneId, int? erstelltVonId, SprachregelStatus status) => status switch
    {
        SprachregelStatus.Entwurf => erstelltVonId == eigeneId || DarfFreigeben(rollen),
        SprachregelStatus.Zurueckgezogen => DarfFreigeben(rollen),
        _ => false,
    };

    /// <summary>Erlaubte Statuswechsel: Entwurf → Freigegeben → Zurückgezogen, nie zurück.</summary>
    public static bool IstErlaubterWechsel(SprachregelStatus von, SprachregelStatus nach) =>
        (von, nach) is (SprachregelStatus.Entwurf, SprachregelStatus.Freigegeben)
            or (SprachregelStatus.Freigegeben, SprachregelStatus.Zurueckgezogen);

    /// <summary>
    /// Export der Regeln einer Zielgruppe: Admin und Compliance immer; die Rolle
    /// Vertrieb bzw. Support zusätzlich für die eigene Zielgruppe. Der KI-Export
    /// bleibt Admin und Compliance vorbehalten.
    /// </summary>
    public static bool DarfExportieren(MitarbeiterRolle rollen, SprachregelZielgruppe zielgruppe) =>
        DarfFreigeben(rollen) || zielgruppe switch
        {
            SprachregelZielgruppe.Vertrieb => rollen.HasFlag(MitarbeiterRolle.Vertrieb),
            SprachregelZielgruppe.Support => rollen.HasFlag(MitarbeiterRolle.Support),
            _ => false,
        };

    /// <summary>
    /// Rollen, deren Mitarbeiter eine Regel betrifft: Vertrieb bei Zielgruppe
    /// Vertrieb, Support bei Zielgruppe Support. Die Zielgruppe KI betrifft
    /// keinen Mitarbeiter — Claude bekommt die Regel über den Export.
    /// </summary>
    public static MitarbeiterRolle BetroffeneRollen(SprachregelZielgruppe zielgruppen) =>
        (zielgruppen.HasFlag(SprachregelZielgruppe.Vertrieb) ? MitarbeiterRolle.Vertrieb : MitarbeiterRolle.Keine)
        | (zielgruppen.HasFlag(SprachregelZielgruppe.Support) ? MitarbeiterRolle.Support : MitarbeiterRolle.Keine);

    /// <summary>Lesebestätigung ist Pflicht für verbotene Aussagen und Pflichthinweise.</summary>
    public static bool IstBestaetigungspflichtig(SprachregelArt art) =>
        art is SprachregelArt.Verboten or SprachregelArt.Pflichthinweis;

    /// <summary>Welche Pflichtfelder fehlen? Liefert den Fehlertext oder null.</summary>
    public static string? Pruefen(SprachregelArt art, string? aussage, string? stattdessen, string? anlass,
        string? begruendung, SprachregelZielgruppe zielgruppen)
    {
        if (string.IsNullOrWhiteSpace(aussage))
            return "Bitte die Aussage angeben — den Begriff, die Formulierung oder den Grundsatz.";
        if (zielgruppen == SprachregelZielgruppe.Keine)
            return "Bitte mindestens eine Zielgruppe wählen (Vertrieb, Support oder KI).";
        if (art == SprachregelArt.Vermeiden && string.IsNullOrWhiteSpace(stattdessen))
            return "Bei „Besser vermeiden“ bitte angeben, was stattdessen gesagt wird.";
        if (art == SprachregelArt.Verboten && string.IsNullOrWhiteSpace(begruendung))
            return "Bei „Niemals“ bitte begründen, warum die Aussage nicht getroffen werden darf.";
        if (art == SprachregelArt.Pflichthinweis && string.IsNullOrWhiteSpace(anlass))
            return "Bei einem Pflichthinweis bitte den Anlass angeben — in welcher Situation er fällig ist.";
        return null;
    }
}

/// <summary>
/// Texte der Wiki-Neuerungen, die Freigabe und Rücknahme einer Regel
/// automatisch erzeugen. Reine Logik — Titel höchstens 200 Zeichen.
/// </summary>
public static class SprachregelNeuerung
{
    public static string Artbezeichnung(SprachregelArt art) => art switch
    {
        SprachregelArt.Verboten => "Niemals sagen",
        SprachregelArt.Pflichthinweis => "Pflichthinweis",
        SprachregelArt.Vermeiden => "Besser vermeiden",
        SprachregelArt.Verwenden => "So sagen wir es",
        _ => "Grundsatz",
    };

    public static string Titel(string vorsatz, string aussage)
    {
        var einzeilig = string.Join(' ', aussage.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var titel = $"{vorsatz}: {einzeilig}";
        return titel.Length <= 200 ? titel : titel[..199] + "…";
    }

    /// <summary>
    /// Neuerung zur Freigabe: Titel aus Art und Aussage; Text aus Wortlaut,
    /// Anlass, Alternative und Begründung (WikiFormat).
    /// </summary>
    public static (string Titel, string Text) Freigabe(Sprachregel r, bool ersetztVorgaenger)
    {
        var zeilen = new List<string>();
        if (ersetztVorgaenger)
            zeilen.Add("*Neufassung — ersetzt die bisherige Regel.*\n");
        zeilen.Add($"**{Artbezeichnung(r.Art)}:** {r.Aussage}");
        if (!string.IsNullOrWhiteSpace(r.Anlass))
            zeilen.Add($"\n**Anlass:** {r.Anlass}");
        if (!string.IsNullOrWhiteSpace(r.Stattdessen))
            zeilen.Add($"\n**Stattdessen:** {r.Stattdessen}");
        if (!string.IsNullOrWhiteSpace(r.Begruendung))
            zeilen.Add($"\n**Warum:**\n{r.Begruendung}");
        if (r.GueltigAb is { } ab)
            zeilen.Add($"\nGilt ab {ab:dd.MM.yyyy}.");
        var vorsatz = IstNurInfo(r, ersetztVorgaenger) ? $"Neu gefasst · {Artbezeichnung(r.Art)}" : Artbezeichnung(r.Art);
        return (Titel(vorsatz, r.Aussage), string.Join('\n', zeilen));
    }

    private static bool IstNurInfo(Sprachregel r, bool ersetztVorgaenger) =>
        ersetztVorgaenger && !SprachregelRegeln.IstBestaetigungspflichtig(r.Art);

    /// <summary>Neuerung zur Rücknahme — ohne Bestätigungspflicht.</summary>
    public static (string Titel, string Text) Ruecknahme(Sprachregel r) =>
        (Titel("Gilt nicht mehr", r.Aussage),
         $"Die Regel „{r.Aussage}“ ({Artbezeichnung(r.Art)}) wurde zurückgezogen und gilt nicht mehr.");
}
