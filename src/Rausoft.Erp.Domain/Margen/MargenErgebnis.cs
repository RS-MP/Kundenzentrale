namespace Rausoft.Erp.Domain.Margen;

// ---------------------------------------------------------------------------
// Eingabe und Ergebnis des Margenrechners. Alle Beträge pro Monat in Euro;
// die Seite rechnet für „pro Jahr" selbst mal zwölf. Margen und Anteile als
// Bruchteil (0,25 = 25 %). Die Oberfläche bekommt diese Records als JSON —
// Rausoft.Erp.Dtos/MargenrechnerDtos.cs spiegelt sie (Test MargenDtoTests).
// ---------------------------------------------------------------------------

/// <summary>Kundenzahl je Basket (in der Reihenfolge der Baskets) und Preisänderung in Prozent je Bereich.</summary>
public record MargenEingabe(
    IReadOnlyList<int>? KundenSanctionsOnly,
    IReadOnlyList<int>? KundenFullFile,
    double AenderungSanctionsOnly = 0,
    double AenderungFullFile = 0);

public record MargenErgebnis(
    bool HatBestand,
    MargenKennzahlen Kennzahlen,
    List<MargenZeile> Zeilen,
    MargenSummen Summen,
    MargenVolumen Volumen,
    // null, solange kein Bestand eingetragen ist
    MargenGrenzwerte? Grenzwerte,
    // leer, solange kein Bestand eingetragen ist
    List<MargenKurvenpunkt> Kurve,
    MargenBefunde Befunde,
    List<MargenVerteilung> Verteilungen,
    List<MargenBasketGrenze> BasketGrenzen,
    MargenRechenweg Rechenweg);

public record MargenKennzahlen(double Verkaufspreis, double Einkaufspreis, double Gewinn, double? Marge, int Kunden, long? StaffelBis);

/// <summary>Eine Zelle der Eingabetabelle. EK-Anteil und Beitrag gibt es erst mit Bestand.</summary>
public record MargenZelle(int Kunden, double VkJeKunde, double? EkAnteil, double? Beitrag, double WeitererKunde);

public record MargenZeile(long Basket, MargenZelle SanctionsOnly, MargenZelle FullFile);

public record MargenSummen(
    int KundenSanctionsOnly, int KundenFullFile,
    double VkSanctionsOnly, double VkFullFile,
    double? EkSanctionsOnly, double? EkFullFile);

public record MargenVolumen(
    double Cases, double AnteilFullFile, double AnteilSanctionsOnly,
    // true = Stufe oberhalb der Tabelle (lineare Staffel)
    bool Linear, double? GebuehrFullFile, double? GebuehrSanctionsOnly);

public enum MargenMindestbestandArt
{
    KeineVerlustzone = 0,
    /// <summary>Mit dieser Mischung bei jedem Volumen im Verlust.</summary>
    Nie = 1,
    AbKunden = 2,
}

/// <summary>Verlustzone in Cases; <see cref="BisCases"/> null = dauerhaft.</summary>
public record MargenVerlustzone(double VonCases, double? BisCases);

public record MargenMindestbestand(
    MargenMindestbestandArt Art, long? Kunden, double? UnterCases, bool ImVerlust, List<MargenVerlustzone> WeitereZonen);

public record MargenNaechsteStaffel(double Mehrkosten, double RestCases, long NaechsteBis);

public record MargenGrenzwerte(
    // (Einkauf / Verkauf − 1): so weit können die VK-Preise sinken (negativ) bzw. müssten steigen (positiv)
    double? Preisuntergrenze,
    MargenMindestbestand Mindestbestand,
    MargenNaechsteStaffel NaechsteStaffel,
    int KundenUnterEinstand, double LueckeUnterEinstand);

/// <summary>Punkt der Margenkurve bei gleicher Mischung; Kosten = Einkaufspreis der Stufe für diese Mischung.</summary>
public record MargenKurvenpunkt(double Cases, double Marge, long StaffelBis, double Kosten);

public record MargenBasketBereich(long Basket, MargenBereich Bereich);
public record MargenDauerhaftUnterEinkauf(long Basket, MargenBereich Bereich, double Vk, double Noetig);
public record MargenSpaetKostendeckend(long Basket, MargenBereich Bereich, double AbCases);
public record MargenWeitererKunde(long Basket, MargenBereich Bereich, double Aenderung);
public record MargenAnkerMarge(long Cases, double Marge);

public record MargenBefunde(
    List<MargenDauerhaftUnterEinkauf> DauerhaftUnterEinkauf,
    List<MargenSpaetKostendeckend> SpaetKostendeckend,
    // Einkauf je Case und Monat oberhalb des letzten Ankers
    double EkJeCaseUnendlich,
    long LetzterAnkerCases,
    List<MargenBasketBereich> UnterEinstandMitKunden,
    List<MargenBasketBereich> UnterEinstandOhneKunden,
    // schlechtester zuerst
    List<MargenWeitererKunde> WeitererKundeSenktGewinn,
    // Marge bei gleicher Mischung an den Ankern zwischen erstem und letztem
    List<MargenAnkerMarge> MargeBeiAnkern,
    double? MargeUnendlich,
    long LinearAb,
    double MaxAnstiegJeStufe);

public record MargenVerteilung(
    string Name, string Beschreibung,
    double Cases, long StaffelBis, double Vk, double Ek, double Gewinn,
    List<int> KundenSanctionsOnly, List<int> KundenFullFile);

public record MargenSpanne(double Von, double Bis);

/// <summary>Ab welchem Gesamtvolumen deckt der VK den EK-Anteil des Baskets?</summary>
public record MargenTraegtSich(
    bool Nie, bool VonAnfangAn, double AbCases,
    List<MargenSpanne> Luecken, bool WeitereLuecken,
    double? DauerhaftNichtAb, bool JetztUngedeckt);

public record MargenKundenSpanne(long Von, long Bis);

/// <summary>Bestand nur aus diesem Basket: ab wie vielen Kunden Gewinn?</summary>
public record MargenAlleinGewinn(
    bool Nie, long AbKunden,
    List<MargenKundenSpanne> VerlustWieder, bool WeitereVerlustbereiche,
    long? DauerhaftVerlustAb);

public record MargenBasketGrenzeBereich(double VkJeCase, MargenTraegtSich TraegtSich, MargenAlleinGewinn AlleinGewinn);

public record MargenBasketGrenze(long Basket, MargenBasketGrenzeBereich SanctionsOnly, MargenBasketGrenzeBereich FullFile);

/// <summary>Anker der linearen Staffel; <see cref="AnstiegJeStufe"/> = Anstieg je Stufe vom vorigen Anker bis hierher.</summary>
public record MargenRechenwegAnker(long Cases, double Gebuehr, double? AnstiegJeStufe);

/// <summary>Eckdaten der Einkaufsstaffel für den Abschnitt „So rechnet die Seite".</summary>
public record MargenRechenweg(
    int StufenAnzahl, long ErsteStufeBis, long LetzteStufeBis,
    double GebuehrVon, double GebuehrBis,
    long LinearSchritt, List<MargenRechenwegAnker> Anker, double AnstiegJeStufeUnendlich,
    string? Stand, string? Hinweis);

// --- Antworten der API rund um den Rechner (ebenfalls in den Dtos gespiegelt) ---

/// <summary>Was die Seite beim Öffnen braucht: Baskets für die Eingabetabelle und der Beispielbestand. Keine Preise.</summary>
public record MargenrechnerStart(
    bool DatenVorhanden,
    // Warum nicht gerechnet werden kann (noch nichts gepflegt / Stand nicht lesbar)
    string? Hinweis,
    List<long> Baskets,
    List<int> BeispielSanctionsOnly,
    List<int> BeispielFullFile,
    DateTime? GeaendertAm,
    string? GeaendertVon,
    // null, solange keine Daten gepflegt sind
    MargenPreisherkunft? Preisherkunft = null);

/// <summary>World-Check-Verkaufspreis je Monat aus der Wiki-Preisliste für einen Basket.</summary>
public record MargenWikiPreis(long Cases, double? SanctionsOnly, double? FullFile);

public record MargenWikiPreise(int Version, string Stand, List<MargenWikiPreis> Preise);

/// <summary>
/// Woher die Verkaufspreise der Rechnung stammen: aus der Wiki-Preisliste
/// (<see cref="AusWiki"/> Preise) oder — wo das Wiki für Basket und Bereich
/// keinen Preis hat — aus der Datenpflege des Rechners (<see cref="VonHand"/>).
/// </summary>
public record MargenPreisherkunft(int? WikiVersion, string? WikiStand, int AusWiki, List<MargenBasketBereich> VonHand);

/// <summary>
/// Die gültigen Verkaufspreise stehen in der Wiki-Preisliste (Entscheidung MP
/// 24.11.2026): Hat sie für einen Basket und Bereich einen Preis, gilt dieser.
/// Der in der Datenpflege eingetragene Verkaufspreis gilt nur dort, wo das Wiki
/// keinen hat. Welche Baskets es gibt, bestimmt allein die Datenpflege;
/// die Einkaufsstaffel bleibt unberührt.
/// </summary>
public static class MargenVerkaufspreise
{
    public static (MargenDaten Daten, MargenPreisherkunft Herkunft) Anwenden(MargenDaten gepflegt, MargenWikiPreise? wiki)
    {
        var jeBasket = new Dictionary<long, MargenWikiPreis>();
        foreach (var preis in wiki?.Preise ?? [])
            jeBasket.TryAdd(preis.Cases, preis);

        var ausWiki = 0;
        var vonHand = new List<MargenBasketBereich>();
        double Preis(long basket, MargenBereich bereich, double? imWiki, double eingetragen)
        {
            if (imWiki is { } p && double.IsFinite(p) && p >= 0 && p <= MargenDaten.MaxBetrag)
            {
                ausWiki++;
                return p;
            }
            vonHand.Add(new MargenBasketBereich(basket, bereich));
            return eingetragen;
        }

        var baskets = gepflegt.Baskets.Select(b =>
        {
            jeBasket.TryGetValue(b.Cases, out var w);
            return b with
            {
                VkSanctionsOnly = Preis(b.Cases, MargenBereich.SanctionsOnly, w?.SanctionsOnly, b.VkSanctionsOnly),
                VkFullFile = Preis(b.Cases, MargenBereich.FullFile, w?.FullFile, b.VkFullFile),
            };
        }).ToList();

        return (gepflegt with { Baskets = baskets }, new MargenPreisherkunft(wiki?.Version, wiki?.Stand, ausWiki, vonHand));
    }
}

/// <summary>Datenpflege: der gespeicherte Stand und die Preise der aktuellen Wiki-Preisliste zum Vergleich.</summary>
public record MargenDatenAnsicht(
    MargenDaten? Daten,
    string? Hinweis,
    DateTime? GeaendertAm,
    string? GeaendertVon,
    MargenWikiPreise? Wiki);

/// <summary>Für die Navigation: darf der angemeldete Benutzer den Margenrechner sehen?</summary>
public record MargenrechnerZugangInfo(bool Darf);
