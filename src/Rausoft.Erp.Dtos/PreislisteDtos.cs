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
    PreislisteText? Onprem);

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
    List<PreislisteVersionEintrag> Historie);

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
    string? GeaendertVon);

public record WikiThemaInfoSpeichern(int? VerantwortlichId, int? VertreterId, string? Prozess);
