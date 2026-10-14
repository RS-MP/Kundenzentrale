using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rausoft.Erp.Dtos;

// ---------------------------------------------------------------------------
// Preisliste (Wiki, Thema „Preisliste"): Abbild des Exports aus dem
// Preislisten-Projekt (Schema rausoft.preisliste/1, siehe
// ANLEITUNG-PREISLISTEN-EXPORT.md). Die Records werden direkt aus der JSON
// gelesen; PreislisteSichtbarkeit.Filtern entfernt serverseitig alles
// Interne, bevor es an Benutzer ohne Vertrieb/Admin/Compliance geht.
// ---------------------------------------------------------------------------

/// <summary>Preis als Zahl oder Text („auf Anfrage"); null = nicht verfügbar.</summary>
[JsonConverter(typeof(PreisWertConverter))]
public record PreisWert(decimal? Zahl, string? Text)
{
    public bool IstLeer => Zahl is null && Text is null;

    /// <summary>Anzeige: „49 €", „19,80 €", „auf Anfrage", „—".</summary>
    public string Anzeige(string einheit = "") =>
        Zahl is { } z ? (z == Math.Floor(z) ? $"{z:N0} €" : $"{z:N2} €") + einheit
        : Text ?? "—";
}

public class PreisWertConverter : JsonConverter<PreisWert>
{
    // Sonst übergeht System.Text.Json den Converter bei null und das Feld bleibt null statt „nicht verfügbar"
    public override bool HandleNull => true;

    public override PreisWert? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => new PreisWert(null, null),
            JsonTokenType.Number => new PreisWert(reader.GetDecimal(), null),
            JsonTokenType.String => new PreisWert(null, reader.GetString()),
            _ => throw new JsonException("Preis muss Zahl, Text oder null sein."),
        };

    public override void Write(Utf8JsonWriter writer, PreisWert? value, JsonSerializerOptions options)
    {
        if (value is null || value.IstLeer) writer.WriteNullValue();
        else if (value.Zahl is { } z) writer.WriteNumberValue(z);
        else if (value.Text is { } t) writer.WriteStringValue(t);
        else writer.WriteNullValue();
    }
}

public record PreislisteDaten(
    string Schema,
    string Produkt,
    int Version,
    string Stand,
    DateOnly? GueltigAb,
    string? Waehrung,
    string? Konditionen,
    PreislisteDokumente? Dokumente,
    List<PreislisteStufe> Stufen,
    List<PreislisteVolumenstufe> Volumenstufen,
    List<PreislisteText>? VolumenRegeln,
    List<PreislisteAddon> Addons,
    PreislistePakete? SanktionslistenPakete,
    PreislisteBenutzer? BenutzerStaffel,
    PreislisteWorldcheck? Worldcheck,
    PreislisteVolumenPool? VolumenPool,
    List<PreislisteBegriff>? Begriffe,
    List<PreislisteHinweis>? Vertriebshinweise,
    List<PreislisteSicherheit>? Sicherheit,
    List<PreislisteHinweis>? WarumIdprove,
    PreislisteText? Onprem,
    // Optional: Änderungen dieser Version in Worten des Preislisten-Projekts
    List<string>? Aenderungen = null);

public record PreislisteDokumente(string? Intern, string? Extern);

public record PreislisteStufe(
    string Schluessel,
    string Name,
    int? VolumenVon,
    int? VolumenBis,
    string? VolumenHinweis,
    PreisWert? SetupEinmalig,
    string? SetupHinweis,
    string? SetupHinweisIntern,
    int? InklusiveBenutzer,
    int? InklusiveMandanten,
    List<PreislisteMerkmal> Merkmale);

public record PreislisteMerkmal(string Text, bool Enthalten, string? Sichtbarkeit);

public record PreislisteVolumenstufe(long Stammdaten, string? Label, Dictionary<string, PreisWert> Preise);

public record PreislisteText(string Text, string? Sichtbarkeit = null);

public record PreislisteAddon(
    string Schluessel,
    string Name,
    string? NameIntern,
    string? Beschreibung,
    string? VerfuegbarAb,
    string? VerfuegbarkeitHinweis,
    PreisWert? PreisMonat,
    PreisWert? EinrichtungEinmalig,
    string? EinrichtungHinweis,
    string? Sichtbarkeit,
    string? HinweisIntern);

public record PreislistePakete(string? Hinweis, List<string> Standardlisten, List<PreislistePaket> Pakete);

public record PreislistePaket(string Schluessel, string Name, string? NameIntern, PreisWert? PreisMonat, List<string> Listen, string? Hinweis);

public record PreislisteBenutzer(
    string? Hinweis,
    List<PreislisteBenutzerStufe> Stufen,
    List<PreislistePauschale>? Pauschalen,
    List<PreislisteText>? Verkaufshilfen);

public record PreislisteBenutzerStufe(int Von, int? Bis, PreisWert? PreisProBenutzerMonat);

public record PreislistePauschale(string Name, string? NurStufe, PreisWert? PreisMonat);

public record PreislisteWorldcheck(
    string? Sichtbarkeit,
    string? Beschreibung,
    string? VerfuegbarAb,
    List<PreislisteWorldcheckPaket> Pakete,
    List<string>? Regeln);

public record PreislisteWorldcheckPaket(string Schluessel, string Name, string? Untertitel, List<PreislisteWorldcheckStufe> Staffel);

public record PreislisteWorldcheckStufe(long Stammdaten, PreisWert? PreisMonat, PreisWert? PreisJahr);

public record PreislisteVolumenPool(string? Sichtbarkeit, string? Beschreibung, List<PreislistePool> Pools, List<string>? Hinweise);

public record PreislistePool(string Name, string? Verwendung, string? Reset, string? Default);

public record PreislisteBegriff(string Intern, string Extern, string? Erlaeuterung);

public record PreislisteHinweis(string Titel, string Text);

public record PreislisteSicherheit(string Thema, string? TextExtern, string? TextIntern);

/// <summary>
/// Entfernt alles Interne aus einer Preisliste — für Benutzer ohne
/// Vertrieb/Admin/Compliance. Läuft serverseitig; die Oberfläche bekommt
/// interne Inhalte dann gar nicht erst.
/// </summary>
public static class PreislisteSichtbarkeit
{
    private static bool Intern(string? sichtbarkeit) =>
        string.Equals(sichtbarkeit, "intern", StringComparison.OrdinalIgnoreCase);

    public static PreislisteDaten Filtern(PreislisteDaten p, bool darfInternes)
    {
        if (darfInternes)
            return p;

        return p with
        {
            Dokumente = p.Dokumente is null ? null : p.Dokumente with { Intern = null },
            Stufen = p.Stufen.Select(s => s with
            {
                SetupHinweisIntern = null,
                Merkmale = s.Merkmale.Where(m => !Intern(m.Sichtbarkeit)).ToList(),
            }).ToList(),
            VolumenRegeln = p.VolumenRegeln?.Where(r => !Intern(r.Sichtbarkeit)).ToList(),
            Addons = p.Addons.Where(a => !Intern(a.Sichtbarkeit))
                .Select(a => a with { NameIntern = null, HinweisIntern = null }).ToList(),
            SanktionslistenPakete = p.SanktionslistenPakete is null ? null : p.SanktionslistenPakete with
            {
                Pakete = p.SanktionslistenPakete.Pakete.Select(x => x with { NameIntern = null }).ToList(),
            },
            BenutzerStaffel = p.BenutzerStaffel is null ? null : p.BenutzerStaffel with
            {
                Verkaufshilfen = p.BenutzerStaffel.Verkaufshilfen?.Where(v => !Intern(v.Sichtbarkeit)).ToList(),
            },
            // Abschnitte, die als Ganzes intern sind
            Worldcheck = p.Worldcheck is null || Intern(p.Worldcheck.Sichtbarkeit) ? null : p.Worldcheck,
            VolumenPool = p.VolumenPool is null || Intern(p.VolumenPool.Sichtbarkeit) ? null : p.VolumenPool,
            Vertriebshinweise = null,
            Sicherheit = p.Sicherheit?.Select(s => s with { TextIntern = null }).ToList(),
        };
    }
}

/// <summary>Antwort der Wiki-Seite: aufbereitete Preisliste + Metadaten der importierten Version.</summary>
public record PreislisteAnsicht(
    PreislisteDaten? Daten,
    PreislisteVersionEintrag? Version,
    bool DarfInternes,
    bool DarfBearbeiten,
    // UNC-Ordner mit den PDFs (Einstellung Preisliste.Verzeichnis), für „Ordner öffnen"
    string? Verzeichnis,
    List<PreislisteVersionEintrag> Historie,
    // Automatischer Vergleich mit der nächstälteren Version (null = keine Vorversion)
    PreislisteVergleichErgebnis? Vergleich);

/// <summary>Eine erkannte Änderung zwischen zwei Preislisten-Versionen. Art: neu · entfernt · geaendert.</summary>
public record PreislisteAenderung(string Abschnitt, string Art, string Text);

public record PreislisteVergleichErgebnis(int VergleichsVersion, List<PreislisteAenderung> Aenderungen);

/// <summary>
/// Vergleicht zwei Preislisten-Versionen und benennt, was neu, entfernt oder
/// geändert ist — je Abschnitt, in lesbaren Sätzen. Beide Seiten müssen vorher
/// mit <see cref="PreislisteSichtbarkeit.Filtern"/> auf den Leser zugeschnitten
/// sein, damit interne Änderungen nicht über den Vergleich durchsickern.
/// </summary>
public static class PreislisteVergleich
{
    public static List<PreislisteAenderung> Vergleichen(PreislisteDaten alt, PreislisteDaten neu)
    {
        var a = new List<PreislisteAenderung>();

        if (alt.GueltigAb != neu.GueltigAb)
            a.Add(new("Allgemein", "geaendert", $"Gültig ab: {Datum(alt.GueltigAb)} → {Datum(neu.GueltigAb)}"));
        if (alt.Konditionen != neu.Konditionen)
            a.Add(new("Allgemein", "geaendert", $"Konditionen: „{alt.Konditionen}“ → „{neu.Konditionen}“"));

        var stufenNamen = neu.Stufen.ToDictionary(s => s.Schluessel, s => s.Name);
        string Stufe(string? schluessel) => schluessel is not null && stufenNamen.TryGetValue(schluessel, out var n) ? n : schluessel ?? "—";

        Listen(a, "Stufen", alt.Stufen, neu.Stufen, s => s.Schluessel, s => s.Name, (x, y) =>
        {
            var t = new List<string>();
            if (x.Name != y.Name) t.Add($"Name {x.Name} → {y.Name}");
            if (x.VolumenVon != y.VolumenVon || x.VolumenBis != y.VolumenBis || x.VolumenHinweis != y.VolumenHinweis)
                t.Add($"Volumen {Volumen(x)} → {Volumen(y)}");
            if (x.SetupEinmalig != y.SetupEinmalig) t.Add($"Setup {Preis(x.SetupEinmalig)} → {Preis(y.SetupEinmalig)}");
            if (x.InklusiveBenutzer != y.InklusiveBenutzer) t.Add($"inklusive Benutzer {x.InklusiveBenutzer} → {y.InklusiveBenutzer}");
            if (x.InklusiveMandanten != y.InklusiveMandanten) t.Add($"inklusive Mandanten {x.InklusiveMandanten} → {y.InklusiveMandanten}");
            foreach (var m in y.Merkmale.Where(m => x.Merkmale.All(o => o.Text != m.Text)))
                t.Add($"neues Merkmal „{m.Text}“");
            foreach (var m in x.Merkmale.Where(m => y.Merkmale.All(o => o.Text != m.Text)))
                t.Add($"Merkmal entfernt „{m.Text}“");
            foreach (var m in y.Merkmale)
                if (x.Merkmale.FirstOrDefault(o => o.Text == m.Text) is { } o && o.Enthalten != m.Enthalten)
                    t.Add($"„{m.Text}“ {(m.Enthalten ? "jetzt enthalten" : "nicht mehr enthalten")}");
            return t;
        });

        Listen(a, "Volumenpreise", alt.Volumenstufen, neu.Volumenstufen,
            v => v.Stammdaten.ToString(), v => v.Label ?? v.Stammdaten.ToString("N0") + " Stammdaten", (x, y) =>
            {
                var t = new List<string>();
                foreach (var k in x.Preise.Keys.Union(y.Preise.Keys))
                {
                    x.Preise.TryGetValue(k, out var px);
                    y.Preise.TryGetValue(k, out var py);
                    if (px != py) t.Add($"{Stufe(k)} {Preis(px)} → {Preis(py)}");
                }
                return t;
            });
        Texte(a, "Volumenpreise", "Regel", alt.VolumenRegeln?.Select(r => r.Text), neu.VolumenRegeln?.Select(r => r.Text));

        Listen(a, "Add-ons", alt.Addons, neu.Addons, x => x.Schluessel, x => x.Name, (x, y) =>
        {
            var t = new List<string>();
            if (x.Name != y.Name) t.Add($"Name {x.Name} → {y.Name}");
            if (x.PreisMonat != y.PreisMonat) t.Add($"Preis/Monat {Preis(x.PreisMonat)} → {Preis(y.PreisMonat)}");
            if (x.EinrichtungEinmalig != y.EinrichtungEinmalig) t.Add($"Einrichtung {Preis(x.EinrichtungEinmalig)} → {Preis(y.EinrichtungEinmalig)}");
            if (x.VerfuegbarAb != y.VerfuegbarAb) t.Add($"verfügbar ab {Stufe(x.VerfuegbarAb)} → {Stufe(y.VerfuegbarAb)}");
            if (x.VerfuegbarkeitHinweis != y.VerfuegbarkeitHinweis) t.Add("Verfügbarkeitshinweis geändert");
            if (x.EinrichtungHinweis != y.EinrichtungHinweis) t.Add("Einrichtungshinweis geändert");
            if (x.Beschreibung != y.Beschreibung) t.Add("Beschreibung geändert");
            if (x.HinweisIntern != y.HinweisIntern) t.Add("interner Hinweis geändert");
            return t;
        });

        if (alt.SanktionslistenPakete is { } ap && neu.SanktionslistenPakete is { } np)
        {
            Texte(a, "Sanktionslisten-Pakete", "Standardliste", ap.Standardlisten, np.Standardlisten);
            Listen(a, "Sanktionslisten-Pakete", ap.Pakete, np.Pakete, p => p.Schluessel, p => p.Name, (x, y) =>
            {
                var t = new List<string>();
                if (x.Name != y.Name) t.Add($"Name {x.Name} → {y.Name}");
                if (x.PreisMonat != y.PreisMonat) t.Add($"Preis/Monat {Preis(x.PreisMonat)} → {Preis(y.PreisMonat)}");
                foreach (var l in y.Listen.Except(x.Listen)) t.Add($"Liste {l} hinzugekommen");
                foreach (var l in x.Listen.Except(y.Listen)) t.Add($"Liste {l} entfernt");
                if (x.Hinweis != y.Hinweis) t.Add("Hinweis geändert");
                return t;
            });
        }
        else if (alt.SanktionslistenPakete is null != neu.SanktionslistenPakete is null)
            a.Add(new("Sanktionslisten-Pakete", neu.SanktionslistenPakete is null ? "entfernt" : "neu", "Abschnitt Sanktionslisten-Pakete"));

        if (alt.BenutzerStaffel is { } ab && neu.BenutzerStaffel is { } nb)
        {
            if (ab.Hinweis != nb.Hinweis) a.Add(new("Benutzer-Pakete", "geaendert", "Hinweis geändert"));
            Listen(a, "Benutzer-Pakete", ab.Stufen, nb.Stufen, s => s.Von.ToString(),
                s => s.Bis is null ? $"ab {s.Von} Benutzer" : $"{s.Von} – {s.Bis} Benutzer", (x, y) =>
                {
                    var t = new List<string>();
                    if (x.Bis != y.Bis) t.Add($"bis {x.Bis?.ToString() ?? "offen"} → {y.Bis?.ToString() ?? "offen"}");
                    if (x.PreisProBenutzerMonat != y.PreisProBenutzerMonat) t.Add($"Preis {Preis(x.PreisProBenutzerMonat)} → {Preis(y.PreisProBenutzerMonat)}");
                    return t;
                });
            Listen(a, "Benutzer-Pakete", ab.Pauschalen, nb.Pauschalen, p => p.Name, p => $"Pauschale {p.Name}", (x, y) =>
            {
                var t = new List<string>();
                if (x.PreisMonat != y.PreisMonat) t.Add($"Preis {Preis(x.PreisMonat)} → {Preis(y.PreisMonat)}");
                if (x.NurStufe != y.NurStufe) t.Add($"nur {Stufe(x.NurStufe)} → {Stufe(y.NurStufe)}");
                return t;
            });
            Texte(a, "Benutzer-Pakete", "Verkaufshilfe", ab.Verkaufshilfen?.Select(v => v.Text), nb.Verkaufshilfen?.Select(v => v.Text));
        }

        if (alt.Worldcheck is { } aw && neu.Worldcheck is { } nw)
        {
            if (aw.VerfuegbarAb != nw.VerfuegbarAb) a.Add(new("WorldCheck", "geaendert", $"verfügbar ab {Stufe(aw.VerfuegbarAb)} → {Stufe(nw.VerfuegbarAb)}"));
            if (aw.Beschreibung != nw.Beschreibung) a.Add(new("WorldCheck", "geaendert", "Beschreibung geändert"));
            Listen(a, "WorldCheck", aw.Pakete, nw.Pakete, p => p.Schluessel, p => p.Name, (x, y) =>
            {
                var t = new List<string>();
                if (x.Name != y.Name) t.Add($"Name {x.Name} → {y.Name}");
                var xs = x.Staffel.ToDictionary(s => s.Stammdaten);
                var ys = y.Staffel.ToDictionary(s => s.Stammdaten);
                foreach (var k in xs.Keys.Union(ys.Keys).OrderBy(k => k))
                {
                    xs.TryGetValue(k, out var sx);
                    ys.TryGetValue(k, out var sy);
                    if (sx is null) t.Add($"neue Staffel {k:N0} Stammdaten");
                    else if (sy is null) t.Add($"Staffel {k:N0} Stammdaten entfernt");
                    else
                    {
                        if (sx.PreisMonat != sy.PreisMonat) t.Add($"{k:N0} Stammdaten /Monat {Preis(sx.PreisMonat)} → {Preis(sy.PreisMonat)}");
                        if (sx.PreisJahr != sy.PreisJahr) t.Add($"{k:N0} Stammdaten /Jahr {Preis(sx.PreisJahr)} → {Preis(sy.PreisJahr)}");
                    }
                }
                return t;
            });
            Texte(a, "WorldCheck", "Regel", aw.Regeln, nw.Regeln);
        }

        if (alt.VolumenPool is { } av && neu.VolumenPool is { } nv)
        {
            if (av.Beschreibung != nv.Beschreibung) a.Add(new("Volume-Pool", "geaendert", "Beschreibung geändert"));
            Listen(a, "Volume-Pool", av.Pools, nv.Pools, p => p.Name, p => p.Name, (x, y) =>
            {
                var t = new List<string>();
                if (x.Verwendung != y.Verwendung) t.Add("Verwendung geändert");
                if (x.Reset != y.Reset) t.Add($"Reset {x.Reset} → {y.Reset}");
                if (x.Default != y.Default) t.Add($"Default {x.Default} → {y.Default}");
                return t;
            });
            Texte(a, "Volume-Pool", "Hinweis", av.Hinweise, nv.Hinweise);
        }

        Listen(a, "Begriffe", alt.Begriffe, neu.Begriffe, b => b.Intern, b => $"{b.Intern} ↔ {b.Extern}", (x, y) =>
        {
            var t = new List<string>();
            if (x.Extern != y.Extern) t.Add($"beim Kunden {x.Extern} → {y.Extern}");
            if (x.Erlaeuterung != y.Erlaeuterung) t.Add("Erläuterung geändert");
            return t;
        });
        Listen(a, "Vertriebshinweise", alt.Vertriebshinweise, neu.Vertriebshinweise, h => h.Titel, h => h.Titel,
            (x, y) => x.Text != y.Text ? ["Text geändert"] : []);
        Listen(a, "Sicherheit und Hosting", alt.Sicherheit, neu.Sicherheit, s => s.Thema, s => s.Thema, (x, y) =>
        {
            var t = new List<string>();
            if (x.TextExtern != y.TextExtern) t.Add("Text geändert");
            if (x.TextIntern != y.TextIntern) t.Add("interner Text geändert");
            return t;
        });
        Listen(a, "Argumente", alt.WarumIdprove, neu.WarumIdprove, h => h.Titel, h => h.Titel,
            (x, y) => x.Text != y.Text ? ["Text geändert"] : []);
        if (alt.Onprem?.Text != neu.Onprem?.Text)
            a.Add(new("Argumente", "geaendert", "On-Premises-Text geändert"));

        return a;
    }

    /// <summary>Listen mit Schlüssel: neu / entfernt / je Element die Unterschiede.</summary>
    private static void Listen<T>(List<PreislisteAenderung> a, string abschnitt, IEnumerable<T>? alt, IEnumerable<T>? neu,
        Func<T, string> schluessel, Func<T, string> name, Func<T, T, IEnumerable<string>> unterschiede)
    {
        var altD = (alt ?? []).GroupBy(schluessel).ToDictionary(g => g.Key, g => g.First());
        var neuD = (neu ?? []).GroupBy(schluessel).ToDictionary(g => g.Key, g => g.First());
        foreach (var (k, n) in neuD)
            if (!altD.ContainsKey(k))
                a.Add(new(abschnitt, "neu", name(n)));
        foreach (var (k, o) in altD)
            if (!neuD.ContainsKey(k))
                a.Add(new(abschnitt, "entfernt", name(o)));
        foreach (var (k, n) in neuD)
            if (altD.TryGetValue(k, out var o))
                foreach (var u in unterschiede(o, n))
                    a.Add(new(abschnitt, "geaendert", $"{name(n)}: {u}"));
    }

    /// <summary>Reine Textlisten: Text neu / Text entfernt.</summary>
    private static void Texte(List<PreislisteAenderung> a, string abschnitt, string art, IEnumerable<string>? alt, IEnumerable<string>? neu)
    {
        var altL = (alt ?? []).ToList();
        var neuL = (neu ?? []).ToList();
        foreach (var t in neuL.Except(altL)) a.Add(new(abschnitt, "neu", $"{art}: {t}"));
        foreach (var t in altL.Except(neuL)) a.Add(new(abschnitt, "entfernt", $"{art}: {t}"));
    }

    private static string Preis(PreisWert? p) => p is null || p.IstLeer ? "—" : p.Anzeige();
    private static string Datum(DateOnly? d) => d?.ToString("dd.MM.yyyy") ?? "—";
    private static string Volumen(PreislisteStufe s) =>
        s.VolumenHinweis ?? (s.VolumenBis is null ? $"ab {s.VolumenVon:N0}" : $"{s.VolumenVon:N0} – {s.VolumenBis:N0}");
}

public record PreislisteVersionEintrag(
    int Id,
    int Version,
    string Stand,
    DateOnly? GueltigAb,
    string? DateiIntern,
    string? DateiExtern,
    DateTime ImportiertAm,
    string? ImportiertVon,
    string? Notiz);

/// <summary>Zuständigkeit und Prozess je Wiki-Thema (z. B. Preisliste).</summary>
public record WikiThemaInfoEintrag(
    string Thema,
    int? VerantwortlichId,
    string? Verantwortlich,
    int? VertreterId,
    string? Vertreter,
    string? Prozess,
    DateTime? GeaendertAm,
    string? GeaendertVon,
    // Von der Redaktion angeordnete Abschnitts-Reihenfolge; leer = Standard der Seite
    List<string> Abschnitte,
    // Abschnitte, die beim Öffnen ausgeklappt sind; leer = alle eingeklappt
    List<string> AbschnitteOffen);

public record WikiThemaInfoSpeichern(int? VerantwortlichId, int? VertreterId, string? Prozess);

/// <summary>Neue Reihenfolge der Abschnitte einer Themenseite (Schlüssel in Anzeigereihenfolge) und welche davon standardmäßig offen sind.</summary>
public record WikiAbschnitteSpeichern(List<string> Abschnitte, List<string> Offen);

/// <summary>
/// Abschnitts-Reihenfolge einer Wiki-Seite: die Redaktion ordnet die Abschnitte
/// an, die Seite kennt den Standard. Gespeicherte, aber unbekannte Schlüssel
/// (z. B. nach einem Umbau der Seite) fallen weg; neue Abschnitte, die noch
/// nicht angeordnet wurden, kommen hinten in Standardreihenfolge dazu.
/// </summary>
public static class WikiAbschnittReihenfolge
{
    public const int MaxAnzahl = 30;
    public const int MaxLaenge = 500;

    public static List<string> Anwenden(IEnumerable<string>? gespeichert, IReadOnlyList<string> standard)
    {
        var ergebnis = new List<string>();
        foreach (var schluessel in gespeichert ?? [])
            if (standard.Contains(schluessel) && !ergebnis.Contains(schluessel))
                ergebnis.Add(schluessel);
        ergebnis.AddRange(standard.Where(s => !ergebnis.Contains(s)));
        return ergebnis;
    }

    public static List<string> Lesen(string? gespeichert) =>
        string.IsNullOrWhiteSpace(gespeichert)
            ? []
            : gespeichert.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>Prüft die Schlüssel (nur a-z, 0-9, Bindestrich; eindeutig) und liefert den Speicherwert — null bei leerer oder ungültiger Eingabe.</summary>
    public static string? Schreiben(IEnumerable<string>? abschnitte, out string? fehler)
    {
        fehler = null;
        var liste = (abschnitte ?? []).Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
        if (liste.Count == 0)
            return null;
        if (liste.Count > MaxAnzahl)
        {
            fehler = $"Höchstens {MaxAnzahl} Abschnitte.";
            return null;
        }
        if (liste.Any(a => !a.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')))
        {
            fehler = "Abschnitts-Schlüssel dürfen nur Kleinbuchstaben, Ziffern und Bindestriche enthalten.";
            return null;
        }
        if (liste.Distinct().Count() != liste.Count)
        {
            fehler = "Ein Abschnitt kommt mehrfach vor.";
            return null;
        }
        var wert = string.Join(',', liste);
        if (wert.Length > MaxLaenge)
        {
            fehler = "Die Reihenfolge ist zu lang.";
            return null;
        }
        return wert;
    }
}
