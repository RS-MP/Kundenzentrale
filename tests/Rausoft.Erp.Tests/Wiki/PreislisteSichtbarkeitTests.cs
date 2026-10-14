using System.Text.Json;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Tests.Wiki;

/// <summary>
/// Die interne Preisliste darf Benutzer ohne Vertrieb/Admin/Compliance nie
/// erreichen — der Filter läuft serverseitig und muss alle internen Stellen
/// treffen. Platzhalterpreise, keine echten Werte.
/// </summary>
public class PreislisteSichtbarkeitTests
{
    private const string Beispiel = """
        {
          "schema": "rausoft.preisliste/1", "produkt": "Test", "version": 1, "stand": "2026-10",
          "gueltigAb": "2026-10-01",
          "dokumente": { "intern": "intern.pdf", "extern": "extern.pdf" },
          "stufen": [ { "schluessel": "a", "name": "A", "setupEinmalig": "auf Anfrage", "setupHinweisIntern": "GEHEIM",
                        "merkmale": [ { "text": "öffentlich", "enthalten": true },
                                      { "text": "GEHEIM", "enthalten": true, "sichtbarkeit": "intern" } ] } ],
          "volumenstufen": [ { "stammdaten": 1000, "preise": { "a": 1 } },
                             { "stammdaten": 2000, "preise": { "a": "auf Anfrage" } } ],
          "volumenRegeln": [ { "text": "öffentlich" }, { "text": "GEHEIM", "sichtbarkeit": "intern" } ],
          "addons": [ { "schluessel": "x", "name": "X", "preisMonat": 2.5, "nameIntern": "GEHEIM", "hinweisIntern": "GEHEIM" },
                      { "schluessel": "y", "name": "GEHEIM", "sichtbarkeit": "intern" } ],
          "sanktionslistenPakete": { "standardlisten": ["CFSP"], "pakete": [ { "schluessel": "p", "name": "P", "nameIntern": "GEHEIM", "listen": ["SECO"] } ] },
          "benutzerStaffel": { "stufen": [ { "von": 1, "bis": 4, "preisProBenutzerMonat": 1 } ],
                               "verkaufshilfen": [ { "text": "GEHEIM", "sichtbarkeit": "intern" } ] },
          "worldcheck": { "sichtbarkeit": "intern", "pakete": [], "regeln": ["GEHEIM"] },
          "volumenPool": { "sichtbarkeit": "intern", "pools": [] },
          "begriffe": [ { "intern": "Tier", "extern": "Stufe" } ],
          "vertriebshinweise": [ { "titel": "GEHEIM", "text": "GEHEIM" } ],
          "sicherheit": [ { "thema": "Cloud", "textExtern": "öffentlich", "textIntern": "GEHEIM" } ]
        }
        """;

    private static readonly JsonSerializerOptions Optionen = new() { PropertyNameCaseInsensitive = true };

    private static PreislisteDaten Lesen() => JsonSerializer.Deserialize<PreislisteDaten>(Beispiel, Optionen)!;

    [Fact]
    public void Preise_WerdenAlsZahlOderTextGelesen()
    {
        var p = Lesen();
        Assert.Equal(1m, p.Volumenstufen[0].Preise["a"].Zahl);
        Assert.Equal("auf Anfrage", p.Volumenstufen[1].Preise["a"].Text);
        Assert.Equal("2,50 €", p.Addons[0].PreisMonat!.Anzeige());
        Assert.Equal("1 €", p.Volumenstufen[0].Preise["a"].Anzeige());
        Assert.Equal("auf Anfrage", p.Stufen[0].SetupEinmalig!.Anzeige());
    }

    [Fact]
    public void NullPreis_WirdZuNichtVerfuegbar_StattNullReferenz()
    {
        // „enterprise": null in den Volumenstufen — der Converter muss auch null übernehmen,
        // sonst bleibt das Feld null und die Anzeige stürzt ab (NullReferenceException im Browser)
        const string json = """{ "stammdaten": 1000, "preise": { "a": null } }""";
        var v = JsonSerializer.Deserialize<PreislisteVolumenstufe>(json, Optionen)!;
        Assert.NotNull(v.Preise["a"]);
        Assert.True(v.Preise["a"].IstLeer);
        Assert.Equal("—", v.Preise["a"].Anzeige());
    }

    [Fact]
    public void MitBerechtigung_BleibtAllesErhalten()
    {
        var p = PreislisteSichtbarkeit.Filtern(Lesen(), darfInternes: true);
        Assert.Contains("GEHEIM", JsonSerializer.Serialize(p));
    }

    [Fact]
    public void OhneBerechtigung_EnthaeltNichtsInternes()
    {
        var p = PreislisteSichtbarkeit.Filtern(Lesen(), darfInternes: false);
        var json = JsonSerializer.Serialize(p);
        Assert.DoesNotContain("GEHEIM", json);
        Assert.DoesNotContain("intern.pdf", json);
        // Öffentliches bleibt
        Assert.Equal("extern.pdf", p.Dokumente!.Extern);
        Assert.Single(p.Stufen[0].Merkmale);
        Assert.Single(p.VolumenRegeln!);
        Assert.Single(p.Addons);
        Assert.Null(p.Worldcheck);
        Assert.Null(p.VolumenPool);
        Assert.Null(p.Vertriebshinweise);
        Assert.Equal("öffentlich", p.Sicherheit![0].TextExtern);
        Assert.Single(p.Begriffe!); // Begriffe intern↔extern sind für alle sichtbar
    }
}
