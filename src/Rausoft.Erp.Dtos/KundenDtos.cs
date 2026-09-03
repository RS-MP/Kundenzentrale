namespace Rausoft.Erp.Dtos;

/// <summary>Seitenweise Ergebnisliste für Tabellen mit Paging.</summary>
public record SeitenErgebnis<T>(List<T> Eintraege, int Gesamt, int Seite, int ProSeite)
{
    public int SeitenAnzahl => Math.Max(1, (int)Math.Ceiling(Gesamt / (double)ProSeite));
}

public record KundeListenEintrag(
    int Id,
    string Kundennummer,
    string Firmenname,
    string Status,
    string? Betriebsart,
    string? Ort,
    string? PartnerHinweis,
    string Kategorie);

public record KundeDetail(
    int Id,
    string Kundennummer,
    string Firmenname,
    string Status,
    string? Betriebsart,
    string? Kundengruppe,
    string? UStIdNr,
    int? OdooId,
    string? Notiz,
    string? VermitteltDurchPartner,
    string? RechnungsempfaengerPartner,
    List<AnsprechpartnerEintrag> Ansprechpartner,
    List<AdresseEintrag> Adressen,
    string Kategorie);

public record AnsprechpartnerEintrag(
    int Id,
    string Anrede,
    string? Titel,
    string? Vorname,
    string Nachname,
    string? Position,
    string? Email,
    string? TelefonFestnetz,
    string? TelefonMobil,
    List<string> Rollen,
    bool Aktiv);

public record AdresseEintrag(
    int Id,
    string Typ,
    string Strasse,
    string? Zusatz,
    string Plz,
    string Ort,
    string Land);
