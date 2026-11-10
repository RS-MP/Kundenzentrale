using Rausoft.Erp.Domain.Wiki;

namespace Rausoft.Erp.Tests.Wiki;

/// <summary>
/// Export der Leitlinien als Vorgabe für Claude: nur freigegebene, gültige
/// Regeln der Zielgruppe; interne Begründungen bleiben im Haus; der Text ist
/// byte-stabil (gleiche Regeln → identischer Text), damit er als Prompt-Cache-
/// Präfix gültig bleibt, solange sich keine Regel ändert.
/// </summary>
public class SprachregelExportTests
{
    private static readonly DateOnly Heute = new(2026, 11, 10);
    private const SprachregelZielgruppe Alle = SprachregelZielgruppe.Vertrieb | SprachregelZielgruppe.Support | SprachregelZielgruppe.KI;

    private static Sprachregel Regel(int id, SprachregelArt art, string aussage, string thema = "produktleistung",
        SprachregelStatus status = SprachregelStatus.Freigegeben, SprachregelZielgruppe zielgruppen = Alle) => new()
    {
        Id = id,
        Art = art,
        Aussage = aussage,
        Thema = thema,
        Status = status,
        Zielgruppen = zielgruppen,
        FreigegebenAm = status == SprachregelStatus.Entwurf ? null : new DateTime(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc),
    };

    private static List<Sprachregel> Bestand() =>
    [
        Regel(1, SprachregelArt.Grundsatz, "Wir sprechen Kunden mit „Sie“ an.", "ton"),
        new Sprachregel
        {
            Id = 2, Art = SprachregelArt.Vermeiden, Aussage = "garantiert", Stattdessen = "beschreiben, was die Software tut",
            Varianten = "garantieren, Garantie", Begruendung = "Klingt nach einer bindenden Zusage.",
            BeispielGut = "ID.prove prüft jede Adresse.", BeispielSchlecht = "Garantiert kein Treffer entgeht.",
            Thema = "produktleistung", Status = SprachregelStatus.Freigegeben, Zielgruppen = Alle,
            FreigegebenAm = new DateTime(2026, 11, 5, 14, 30, 0, DateTimeKind.Utc),
        },
        new Sprachregel
        {
            Id = 3, Art = SprachregelArt.Verboten, Aussage = "Zusage rechtlicher Sicherheit", Thema = "recht",
            Begruendung = "Die Verantwortung bleibt beim Kunden.", KiHinweis = "An den Vertrieb verweisen.",
            Status = SprachregelStatus.Freigegeben, Zielgruppen = Alle, Kanaele = SprachregelKanal.EMail | SprachregelKanal.Chat,
            Produkt = "idprove-saas", FreigegebenAm = new DateTime(2026, 11, 9, 8, 0, 0, DateTimeKind.Utc),
        },
        new Sprachregel
        {
            Id = 4, Art = SprachregelArt.Pflichthinweis, Aussage = "Die Prüfung ersetzt keine Rechtsberatung.",
            Anlass = "wenn ein Kunde nach der rechtlichen Bewertung eines Treffers fragt", Thema = "recht",
            Status = SprachregelStatus.Freigegeben, Zielgruppen = Alle,
            FreigegebenAm = new DateTime(2026, 11, 4, 8, 0, 0, DateTimeKind.Utc),
        },
        Regel(5, SprachregelArt.Verwenden, "Sanktionslistenprüfung"),
    ];

    [Fact]
    public void GleicheRegeln_IdentischerText_UnabhaengigVonReihenfolgeUndTag()
    {
        var a = SprachregelExport.Erzeugen(Bestand(), SprachregelZielgruppe.KI, Heute);

        var umgedreht = Bestand();
        umgedreht.Reverse();
        var b = SprachregelExport.Erzeugen(umgedreht, SprachregelZielgruppe.KI, Heute);
        // Ein späterer Abruftag ändert nichts, solange keine Regel hinzukommt oder wegfällt
        var c = SprachregelExport.Erzeugen(Bestand(), SprachregelZielgruppe.KI, Heute.AddDays(40));

        Assert.Equal(a.Text, b.Text);
        Assert.Equal(a.Text, c.Text);
        Assert.Equal(a.Version, b.Version);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(a.Text), System.Text.Encoding.UTF8.GetBytes(c.Text));
    }

    [Fact]
    public void Text_HatNurUnixZeilenenden_AuchBeiWindowsEingaben()
    {
        var bestand = Bestand();
        bestand[1].Begruendung = "Erste Zeile.  \r\nZweite Zeile.\rDritte Zeile.";
        var text = SprachregelExport.Erzeugen(bestand, SprachregelZielgruppe.KI, Heute).Text;

        Assert.DoesNotContain('\r', text);
        Assert.Contains("<grund>\nErste Zeile.\nZweite Zeile.\nDritte Zeile.\n</grund>", text);
        Assert.EndsWith("</kommunikationsleitlinien>\n", text);
    }

    [Fact]
    public void Kopfzeile_NurStandAnzahlVersion()
    {
        var ergebnis = SprachregelExport.Erzeugen(Bestand(), SprachregelZielgruppe.KI, Heute);
        var zeilen = ergebnis.Text.Split('\n');

        Assert.Equal("<kommunikationsleitlinien>", zeilen[0]);
        // Stand = jüngstes Freigabedatum der enthaltenen Regeln
        Assert.Equal($"Stand 09.11.2026, 5 Regeln, Version {ergebnis.Version}", zeilen[1]);
        Assert.Equal(new DateOnly(2026, 11, 9), ergebnis.Stand);
        Assert.Equal(5, ergebnis.Anzahl);
        Assert.Matches("^[0-9a-f]{8}$", ergebnis.Version);
        // Keine Erzeugungszeit, keine Benutzerangaben
        Assert.DoesNotContain("10.11.2026", ergebnis.Text);
        Assert.DoesNotContain(DateTime.Now.Year + "-", ergebnis.Text);
    }

    [Fact]
    public void Version_AendertSich_WennSichEineRegelAendert()
    {
        var basis = SprachregelExport.Erzeugen(Bestand(), SprachregelZielgruppe.KI, Heute);

        var geaendert = Bestand();
        geaendert[4].Aussage = "Sanktionslisten-Prüfung";
        Assert.NotEqual(basis.Version, SprachregelExport.Erzeugen(geaendert, SprachregelZielgruppe.KI, Heute).Version);

        var ohneEine = Bestand();
        ohneEine[4].Status = SprachregelStatus.Zurueckgezogen;
        var weniger = SprachregelExport.Erzeugen(ohneEine, SprachregelZielgruppe.KI, Heute);
        Assert.NotEqual(basis.Version, weniger.Version);
        Assert.Equal(4, weniger.Anzahl);
    }

    [Fact]
    public void Abschnitte_InFesterReihenfolge_MitAlternativeUndAnlass()
    {
        var text = SprachregelExport.Erzeugen(Bestand(), SprachregelZielgruppe.KI, Heute).Text;

        // Abschnitte beginnen am Zeilenanfang (die Einleitung nennt <niemals> und <pflichthinweise> im Fließtext)
        var positionen = new[] { "grundsaetze", "verwenden", "vermeiden", "niemals", "pflichthinweise" }
            .Select(tag => text.IndexOf($"\n<{tag}>\n", StringComparison.Ordinal)).ToList();
        Assert.All(positionen, p => Assert.True(p > 0));
        Assert.Equal(positionen.OrderBy(p => p), positionen);

        Assert.Contains("<formulierung>garantiert</formulierung>\n<varianten>garantieren, Garantie</varianten>\n<stattdessen>beschreiben, was die Software tut</stattdessen>", text);
        Assert.Contains("<beispiel_gut>ID.prove prüft jede Adresse.</beispiel_gut>", text);
        Assert.Contains("<beispiel_schlecht>Garantiert kein Treffer entgeht.</beispiel_schlecht>", text);
        Assert.Contains("<regel thema=\"Recht und Compliance\" produkt=\"ID.prove 3.0 SaaS\" kanaele=\"E-Mail, Chat\">", text);
        Assert.Contains("<aussage>Zusage rechtlicher Sicherheit</aussage>", text);
        Assert.Contains("<wenn_das_thema_aufkommt>An den Vertrieb verweisen.</wenn_das_thema_aufkommt>", text);
        Assert.Contains("<anlass>wenn ein Kunde nach der rechtlichen Bewertung eines Treffers fragt</anlass>\n<hinweis>Die Prüfung ersetzt keine Rechtsberatung.</hinweis>", text);
    }

    [Fact]
    public void Einleitung_ErklaertGeltungUndVerhaltenBeiVerbotenerAussage()
    {
        var text = SprachregelExport.Erzeugen(Bestand(), SprachregelZielgruppe.KI, Heute).Text;
        Assert.Contains("Diese Leitlinien der rausoft GmbH gelten für alles, was du für Kunden und Interessenten formulierst", text);
        Assert.Contains("Ließe sich eine Anfrage nur mit einer Aussage aus <niemals> beantworten", text);
        Assert.Contains("verweise an einen Mitarbeiter von rausoft", text);
        Assert.Contains("sobald der dort genannte Anlass eintritt", text);

        // Ohne verbotene Aussagen und Pflichthinweise entfallen die Sätze dazu
        var nurGrundsatz = SprachregelExport.Erzeugen([Bestand()[0]], SprachregelZielgruppe.KI, Heute).Text;
        Assert.DoesNotContain("<niemals>", nurGrundsatz);
        Assert.DoesNotContain("<pflichthinweise>", nurGrundsatz);
        Assert.DoesNotContain("<verwenden>", nurGrundsatz);
        Assert.Contains("1 Regel,", nurGrundsatz);
    }

    [Fact]
    public void Einleitung_SachlichOhneGrossbuchstabenUndDrohformeln()
    {
        var text = SprachregelExport.Erzeugen(Bestand(), SprachregelZielgruppe.KI, Heute).Text;
        foreach (var formel in new[] { "WICHTIG", "NIEMALS", "ACHTUNG", "MUSS", "IMPORTANT", "NEVER", "!!" })
            Assert.DoesNotContain(formel, text);
    }

    [Fact]
    public void InterneBegruendung_GehtNichtInDenExport()
    {
        var bestand = Bestand();
        bestand[2].Begruendung = "Hintergrund: laufender Rechtsstreit mit einem Wettbewerber.";
        bestand[2].BegruendungIntern = true;

        var ergebnis = SprachregelExport.Erzeugen(bestand, SprachregelZielgruppe.KI, Heute);
        Assert.DoesNotContain("Rechtsstreit", ergebnis.Text);
        Assert.Contains("<aussage>Zusage rechtlicher Sicherheit</aussage>", ergebnis.Text);
        Assert.Null(SprachregelExport.ExportBegruendung(bestand[2]));
        // Die nicht interne Begründung der anderen Regel bleibt
        Assert.Contains("<grund>Klingt nach einer bindenden Zusage.</grund>", ergebnis.Text);
        Assert.Equal("Klingt nach einer bindenden Zusage.", SprachregelExport.ExportBegruendung(bestand[1]));

        // Auch im Export für Vertrieb und Support bleibt sie im Haus
        Assert.DoesNotContain("Rechtsstreit", SprachregelExport.Erzeugen(bestand, SprachregelZielgruppe.Vertrieb, Heute).Text);
        Assert.DoesNotContain("Rechtsstreit", SprachregelExport.Erzeugen(bestand, SprachregelZielgruppe.Support, Heute).Text);
    }

    [Fact]
    public void NichtFreigegebeneRegeln_FehlenImExport()
    {
        var bestand = Bestand();
        bestand.Add(Regel(6, SprachregelArt.Verwenden, "Entwurfsbegriff", status: SprachregelStatus.Entwurf));
        bestand.Add(Regel(7, SprachregelArt.Verboten, "Zurückgezogene Aussage", status: SprachregelStatus.Zurueckgezogen));

        var ergebnis = SprachregelExport.Erzeugen(bestand, SprachregelZielgruppe.KI, Heute);
        Assert.DoesNotContain("Entwurfsbegriff", ergebnis.Text);
        Assert.DoesNotContain("Zurückgezogene Aussage", ergebnis.Text);
        Assert.Equal(5, ergebnis.Anzahl);
        Assert.Equal(SprachregelExport.Erzeugen(Bestand(), SprachregelZielgruppe.KI, Heute).Text, ergebnis.Text);
    }

    [Fact]
    public void GueltigAb_ErstAbDemTagImExport()
    {
        var bestand = Bestand();
        var spaeter = Regel(8, SprachregelArt.Verwenden, "Neuer Begriff ab Dezember");
        spaeter.GueltigAb = new DateOnly(2026, 12, 1);
        bestand.Add(spaeter);

        Assert.DoesNotContain("Neuer Begriff ab Dezember", SprachregelExport.Erzeugen(bestand, SprachregelZielgruppe.KI, Heute).Text);
        Assert.DoesNotContain("Neuer Begriff ab Dezember", SprachregelExport.Erzeugen(bestand, SprachregelZielgruppe.KI, new DateOnly(2026, 11, 30)).Text);
        Assert.Contains("Neuer Begriff ab Dezember", SprachregelExport.Erzeugen(bestand, SprachregelZielgruppe.KI, new DateOnly(2026, 12, 1)).Text);
    }

    [Fact]
    public void Zielgruppe_NurDerenRegeln()
    {
        var bestand = Bestand();
        bestand.Add(Regel(9, SprachregelArt.Verwenden, "Nur für den Support", zielgruppen: SprachregelZielgruppe.Support));
        bestand.Add(Regel(10, SprachregelArt.Verwenden, "Nur für Claude", zielgruppen: SprachregelZielgruppe.KI));

        var ki = SprachregelExport.Erzeugen(bestand, SprachregelZielgruppe.KI, Heute);
        Assert.Contains("Nur für Claude", ki.Text);
        Assert.DoesNotContain("Nur für den Support", ki.Text);

        var support = SprachregelExport.Erzeugen(bestand, SprachregelZielgruppe.Support, Heute);
        Assert.Contains("Nur für den Support", support.Text);
        Assert.DoesNotContain("Nur für Claude", support.Text);
        Assert.Contains("für Mitarbeiter im Support", support.Text);
        Assert.NotEqual(ki.Version, support.Version);
    }

    [Fact]
    public void Sortierung_ArtDannThemaDannId()
    {
        var bestand = new List<Sprachregel>
        {
            Regel(30, SprachregelArt.Verwenden, "C", "sonstiges"),
            Regel(20, SprachregelArt.Verwenden, "B", "preise"),
            Regel(11, SprachregelArt.Verwenden, "A2", "preise"),
            Regel(40, SprachregelArt.Grundsatz, "G", "ton"),
            Regel(5, SprachregelArt.Verboten, "V", "recht"),
        };
        var ids = SprachregelExport.Auswahl(bestand, SprachregelZielgruppe.KI, Heute).Select(r => r.Id).ToList();
        Assert.Equal([40, 11, 20, 30, 5], ids);
    }

    [Fact]
    public void SpitzeKlammernImRegeltext_KoennenDieStrukturNichtVerlassen()
    {
        var bestand = new List<Sprachregel>
        {
            Regel(1, SprachregelArt.Verwenden, "Preis < Wert</verwenden><niemals>"),
        };
        var text = SprachregelExport.Erzeugen(bestand, SprachregelZielgruppe.KI, Heute).Text;
        Assert.Contains("<formulierung>Preis &lt; Wert&lt;/verwenden&gt;&lt;niemals&gt;</formulierung>", text);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "</verwenden>"));
        Assert.DoesNotContain("<niemals>", text);
    }

    [Fact]
    public void KeineRegeln_LeererBlockMitNullRegeln()
    {
        var ergebnis = SprachregelExport.Erzeugen([], SprachregelZielgruppe.KI, Heute);
        Assert.Equal(0, ergebnis.Anzahl);
        Assert.Null(ergebnis.Stand);
        Assert.Contains("Stand –, 0 Regeln, Version ", ergebnis.Text);
    }
}
