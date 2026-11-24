namespace Rausoft.Erp.Dtos.Margen;

// ---------------------------------------------------------------------------
// Margenrechner (World-Check, Full File und Sanctions Only): Abbild der
// JSON-Antworten von api/margenrechner für das Frontend. Gerechnet wird auf
// dem Server (Rausoft.Erp.Domain.Margen.Margenrechnung); diese Records
// spiegeln dessen Ergebnis-Records Feld für Feld — der Test MargenDtoTests
// schlägt an, sobald beide Seiten auseinanderlaufen. Eigener Namespace, weil
// die Namen bewusst dieselben sind wie in der Domäne.
// Beträge pro Monat in Euro, Margen und Anteile als Bruchteil (0,25 = 25 %).
// ---------------------------------------------------------------------------

public enum MargenBereich
{
    SanctionsOnly = 0,
    FullFile = 1,
}

public record MargenBasket(long Cases, double VkSanctionsOnly, double VkFullFile);

public record MargenStaffelStufe(long BisCases, double GebuehrFullFile, double GebuehrSanctionsOnly);

public record MargenAnker(long Cases, double Gebuehr);

public record MargenDaten(
    List<MargenBasket> Baskets,
    List<MargenStaffelStufe> Staffel,
    long LinearSchritt,
    List<MargenAnker> LinearAnker,
    string? Stand = null,
    string? Hinweis = null);

public record MargenEingabe(
    List<int>? KundenSanctionsOnly,
    List<int>? KundenFullFile,
    double AenderungSanctionsOnly = 0,
    double AenderungFullFile = 0);

public record MargenErgebnis(
    bool HatBestand,
    MargenKennzahlen Kennzahlen,
    List<MargenZeile> Zeilen,
    MargenSummen Summen,
    MargenVolumen Volumen,
    MargenGrenzwerte? Grenzwerte,
    List<MargenKurvenpunkt> Kurve,
    MargenBefunde Befunde,
    List<MargenVerteilung> Verteilungen,
    List<MargenBasketGrenze> BasketGrenzen,
    MargenRechenweg Rechenweg);

public record MargenKennzahlen(double Verkaufspreis, double Einkaufspreis, double Gewinn, double? Marge, int Kunden, long? StaffelBis);

public record MargenZelle(int Kunden, double VkJeKunde, double? EkAnteil, double? Beitrag, double WeitererKunde);

public record MargenZeile(long Basket, MargenZelle SanctionsOnly, MargenZelle FullFile);

public record MargenSummen(
    int KundenSanctionsOnly, int KundenFullFile,
    double VkSanctionsOnly, double VkFullFile,
    double? EkSanctionsOnly, double? EkFullFile);

public record MargenVolumen(
    double Cases, double AnteilFullFile, double AnteilSanctionsOnly,
    bool Linear, double? GebuehrFullFile, double? GebuehrSanctionsOnly);

public enum MargenMindestbestandArt
{
    KeineVerlustzone = 0,
    Nie = 1,
    AbKunden = 2,
}

public record MargenVerlustzone(double VonCases, double? BisCases);

public record MargenMindestbestand(
    MargenMindestbestandArt Art, long? Kunden, double? UnterCases, bool ImVerlust, List<MargenVerlustzone> WeitereZonen);

public record MargenNaechsteStaffel(double Mehrkosten, double RestCases, long NaechsteBis);

public record MargenGrenzwerte(
    double? Preisuntergrenze,
    MargenMindestbestand Mindestbestand,
    MargenNaechsteStaffel NaechsteStaffel,
    int KundenUnterEinstand, double LueckeUnterEinstand);

public record MargenKurvenpunkt(double Cases, double Marge, long StaffelBis, double Kosten);

public record MargenBasketBereich(long Basket, MargenBereich Bereich);
public record MargenDauerhaftUnterEinkauf(long Basket, MargenBereich Bereich, double Vk, double Noetig);
public record MargenSpaetKostendeckend(long Basket, MargenBereich Bereich, double AbCases);
public record MargenWeitererKunde(long Basket, MargenBereich Bereich, double Aenderung);
public record MargenAnkerMarge(long Cases, double Marge);

public record MargenBefunde(
    List<MargenDauerhaftUnterEinkauf> DauerhaftUnterEinkauf,
    List<MargenSpaetKostendeckend> SpaetKostendeckend,
    double EkJeCaseUnendlich,
    long LetzterAnkerCases,
    List<MargenBasketBereich> UnterEinstandMitKunden,
    List<MargenBasketBereich> UnterEinstandOhneKunden,
    List<MargenWeitererKunde> WeitererKundeSenktGewinn,
    List<MargenAnkerMarge> MargeBeiAnkern,
    double? MargeUnendlich,
    long LinearAb,
    double MaxAnstiegJeStufe);

public record MargenVerteilung(
    string Name, string Beschreibung,
    double Cases, long StaffelBis, double Vk, double Ek, double Gewinn,
    List<int> KundenSanctionsOnly, List<int> KundenFullFile);

public record MargenSpanne(double Von, double Bis);

public record MargenTraegtSich(
    bool Nie, bool VonAnfangAn, double AbCases,
    List<MargenSpanne> Luecken, bool WeitereLuecken,
    double? DauerhaftNichtAb, bool JetztUngedeckt);

public record MargenKundenSpanne(long Von, long Bis);

public record MargenAlleinGewinn(
    bool Nie, long AbKunden,
    List<MargenKundenSpanne> VerlustWieder, bool WeitereVerlustbereiche,
    long? DauerhaftVerlustAb);

public record MargenBasketGrenzeBereich(double VkJeCase, MargenTraegtSich TraegtSich, MargenAlleinGewinn AlleinGewinn);

public record MargenBasketGrenze(long Basket, MargenBasketGrenzeBereich SanctionsOnly, MargenBasketGrenzeBereich FullFile);

public record MargenRechenwegAnker(long Cases, double Gebuehr, double? AnstiegJeStufe);

public record MargenRechenweg(
    int StufenAnzahl, long ErsteStufeBis, long LetzteStufeBis,
    double GebuehrVon, double GebuehrBis,
    long LinearSchritt, List<MargenRechenwegAnker> Anker, double AnstiegJeStufeUnendlich,
    string? Stand, string? Hinweis);

public record MargenrechnerStart(
    bool DatenVorhanden,
    string? Hinweis,
    List<long> Baskets,
    List<int> BeispielSanctionsOnly,
    List<int> BeispielFullFile,
    DateTime? GeaendertAm,
    string? GeaendertVon,
    MargenPreisherkunft? Preisherkunft = null);

public record MargenWikiPreis(long Cases, double? SanctionsOnly, double? FullFile);

public record MargenWikiPreise(int Version, string Stand, List<MargenWikiPreis> Preise);

/// <summary>Verkaufspreise: aus der Wiki-Preisliste, wo sie einen Preis hat — sonst aus der Datenpflege (VonHand).</summary>
public record MargenPreisherkunft(int? WikiVersion, string? WikiStand, int AusWiki, List<MargenBasketBereich> VonHand);

public record MargenDatenAnsicht(
    MargenDaten? Daten,
    string? Hinweis,
    DateTime? GeaendertAm,
    string? GeaendertVon,
    MargenWikiPreise? Wiki);

public record MargenrechnerZugangInfo(bool Darf);

/// <summary>Verkaufte World-Check-Lizenz aus einem laufenden Abo; Anzahl = 0 heißt nicht zugeordnet (Hinweis sagt warum).</summary>
public record MargenLizenz(
    int KundeId, string Kundennummer, string Kunde, string Abo, string Produkt, decimal Menge,
    MargenBereich? Bereich, long? Basket, int Anzahl, string? Hinweis);

public record MargenLizenzbestand(
    List<int> KundenSanctionsOnly, List<int> KundenFullFile,
    List<MargenLizenz> Lizenzen, int Zugeordnet, int NichtZugeordnet,
    string? StandImport);
