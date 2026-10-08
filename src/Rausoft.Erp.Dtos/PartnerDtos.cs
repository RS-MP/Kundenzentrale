namespace Rausoft.Erp.Dtos;

/// <summary>Partner in der Übersicht /partner.</summary>
public record PartnerEintrag(
    int Id,
    string Name,
    string? Ort,
    string Land,
    // „Tipp-Provision" oder „Vertriebspartner"
    string Provisionsmodell,
    decimal? ProvisionProzent,
    bool Aktiv,
    int Kunden,
    int Vertraege,
    // Partner nutzt die Software auch selbst (verknüpfter Kunde)
    bool AuchKunde = false);

/// <summary>Partner-Akte: Stammdaten + zugeordnete Kunden (Verträge über api/vertraege?partnerId=).</summary>
public record PartnerDetail(
    int Id,
    string Name,
    string? Strasse,
    string? Plz,
    string? Ort,
    string Land,
    string? Email,
    string? Telefon,
    string Provisionsmodell,
    decimal? ProvisionProzent,
    bool Aktiv,
    List<PartnerKunde> Kunden,
    // Partner ist zugleich Kunde (eigene Nutzung) — Verweis in die Kundenakte
    PartnerKunde? AuchKunde,
    // Namensgleiche Kunden als Vorschlag für die Verknüpfung (nur solange keine besteht)
    List<PartnerKunde> KundeVorschlaege);

/// <summary>Verknüpfung „Partner ist auch Kunde" setzen (KundeId) oder lösen (null).</summary>
public record PartnerKundeVerknuepfen(int? KundeId);

/// <summary>Kunde des Partners — vermittelt und/oder Rechnung an den Partner.</summary>
public record PartnerKunde(
    int Id, string Kundennummer, string Firmenname, bool Vermittelt, bool RechnungAnPartner,
    // Laufendes Abo → Gruppe „Kunden"; sonst Gruppe „ohne aktives Abo" mit Kategorie
    bool AktivesAbo = false,
    // Kategorie wie in der Kundenliste: Kunde, Interessent, Ehemaliger Kunde, Zu klären, Einmalbestellung
    string? Kategorie = null);
