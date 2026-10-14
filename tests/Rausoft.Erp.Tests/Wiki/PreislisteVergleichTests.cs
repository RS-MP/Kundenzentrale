using System.Text.Json;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Tests.Wiki;

/// <summary>
/// Der automatische Vergleich zweier Preislisten-Versionen muss neue, entfernte
/// und geänderte Elemente benennen — und darf nach der Sichtbarkeitsfilterung
/// nichts Internes preisgeben. Platzhalterpreise, keine echten Werte.
/// </summary>
public class PreislisteVergleichTests
{
    private const string Alt = """
        {
          "schema": "rausoft.preisliste/1", "produkt": "Test", "version": 1, "stand": "2026-09",
          "gueltigAb": "2026-09-01", "konditionen": "monatlich",
          "stufen": [ { "schluessel": "a", "name": "A", "setupEinmalig": 10, "inklusiveBenutzer": 1,
                        "merkmale": [ { "text": "Basis", "enthalten": true }, { "text": "API", "enthalten": false } ] } ],
          "volumenstufen": [ { "stammdaten": 1000, "preise": { "a": 1 } }, { "stammdaten": 2000, "preise": { "a": 2 } } ],
          "addons": [ { "schluessel": "x", "name": "X", "preisMonat": 5 }, { "schluessel": "alt", "name": "Altes Add-on", "preisMonat": 1 } ],
          "sanktionslistenPakete": { "standardlisten": ["CFSP"], "pakete": [ { "schluessel": "p", "name": "P", "preisMonat": 3, "listen": ["SECO"] } ] },
          "benutzerStaffel": { "stufen": [ { "von": 1, "bis": 4, "preisProBenutzerMonat": 1 } ] },
          "worldcheck": { "sichtbarkeit": "intern", "pakete": [ { "schluessel": "w", "name": "W", "staffel": [ { "stammdaten": 1000, "preisMonat": 9 } ] } ] },
          "begriffe": [ { "intern": "Tier", "extern": "Stufe" } ],
          "sicherheit": [ { "thema": "Cloud", "textExtern": "alt", "textIntern": "GEHEIM alt" } ]
        }
        """;

    private const string Neu = """
        {
          "schema": "rausoft.preisliste/1", "produkt": "Test", "version": 2, "stand": "2026-10",
          "gueltigAb": "2026-10-01", "konditionen": "monatlich",
          "stufen": [ { "schluessel": "a", "name": "A", "setupEinmalig": 20, "inklusiveBenutzer": 1,
                        "merkmale": [ { "text": "Basis", "enthalten": true }, { "text": "API", "enthalten": true }, { "text": "SSO", "enthalten": true } ] } ],
          "volumenstufen": [ { "stammdaten": 1000, "preise": { "a": 1 } }, { "stammdaten": 2000, "preise": { "a": 3 } }, { "stammdaten": 5000, "preise": { "a": 4 } } ],
          "addons": [ { "schluessel": "x", "name": "X", "preisMonat": 5 }, { "schluessel": "neu", "name": "Neues Add-on", "preisMonat": 7 },
                      { "schluessel": "intern", "name": "GEHEIM", "preisMonat": 8, "sichtbarkeit": "intern" } ],
          "sanktionslistenPakete": { "standardlisten": ["CFSP", "SDN"], "pakete": [ { "schluessel": "p", "name": "P", "preisMonat": 3, "listen": ["SECO", "OFSI"] } ] },
          "benutzerStaffel": { "stufen": [ { "von": 1, "bis": 4, "preisProBenutzerMonat": 2 } ] },
          "worldcheck": { "sichtbarkeit": "intern", "pakete": [ { "schluessel": "w", "name": "W", "staffel": [ { "stammdaten": 1000, "preisMonat": 11 } ] } ] },
          "begriffe": [ { "intern": "Tier", "extern": "Stufe" } ],
          "sicherheit": [ { "thema": "Cloud", "textExtern": "alt", "textIntern": "GEHEIM neu" } ]
        }
        """;

    private static readonly JsonSerializerOptions Optionen = new() { PropertyNameCaseInsensitive = true };

    private static PreislisteDaten Lesen(string json) => JsonSerializer.Deserialize<PreislisteDaten>(json, Optionen)!;

    [Fact]
    public void ErkenntNeuEntferntUndGeaendert()
    {
        var a = PreislisteVergleich.Vergleichen(Lesen(Alt), Lesen(Neu));
        var texte = a.Select(x => $"{x.Abschnitt}|{x.Art}|{x.Text}").ToList();

        Assert.Contains("Allgemein|geaendert|Gültig ab: 01.09.2026 → 01.10.2026", texte);
        Assert.Contains("Stufen|geaendert|A: Setup 10 € → 20 €", texte);
        Assert.Contains("Stufen|geaendert|A: neues Merkmal „SSO“", texte);
        Assert.Contains("Stufen|geaendert|A: „API“ jetzt enthalten", texte);
        Assert.Contains("Volumenpreise|neu|5.000 Stammdaten", texte);
        Assert.Contains("Volumenpreise|geaendert|2.000 Stammdaten: A 2 € → 3 €", texte);
        Assert.Contains("Add-ons|neu|Neues Add-on", texte);
        Assert.Contains("Add-ons|entfernt|Altes Add-on", texte);
        Assert.Contains("Sanktionslisten-Pakete|neu|Standardliste: SDN", texte);
        Assert.Contains("Sanktionslisten-Pakete|geaendert|P: Liste OFSI hinzugekommen", texte);
        Assert.Contains("Benutzer-Pakete|geaendert|1 – 4 Benutzer: Preis 1 € → 2 €", texte);
        Assert.Contains("WorldCheck|geaendert|W: 1.000 Stammdaten /Monat 9 € → 11 €", texte);
        Assert.Contains("Sicherheit und Hosting|geaendert|Cloud: interner Text geändert", texte);
        // Unverändertes taucht nicht auf
        Assert.DoesNotContain(texte, t => t.Contains("Begriffe"));
        Assert.DoesNotContain(texte, t => t.StartsWith("Add-ons|geaendert|X"));
    }

    [Fact]
    public void GleicheVersionenErgebenKeineAenderungen()
    {
        Assert.Empty(PreislisteVergleich.Vergleichen(Lesen(Alt), Lesen(Alt)));
    }

    [Fact]
    public void NachSichtbarkeitsfilterKeineInternenAenderungen()
    {
        var alt = PreislisteSichtbarkeit.Filtern(Lesen(Alt), darfInternes: false);
        var neu = PreislisteSichtbarkeit.Filtern(Lesen(Neu), darfInternes: false);
        var a = PreislisteVergleich.Vergleichen(alt, neu);

        Assert.DoesNotContain(a, x => x.Text.Contains("GEHEIM"));
        Assert.DoesNotContain(a, x => x.Abschnitt == "WorldCheck");
        Assert.DoesNotContain(a, x => x.Text.Contains("interner Text"));
        // Externes bleibt sichtbar
        Assert.Contains(a, x => x.Abschnitt == "Add-ons" && x.Art == "neu" && x.Text == "Neues Add-on");
    }
}
