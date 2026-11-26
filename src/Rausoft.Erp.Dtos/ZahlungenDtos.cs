namespace Rausoft.Erp.Dtos;

// ---------------------------------------------------------------------------
// Zahlungseingänge aus Kontoauszügen (ANFORDERUNGEN.md, „Kontoauszüge").
// Status als Text: "klaerung" · "zugeordnet" · "gebucht" · "ignoriert" · "unklar"
// ---------------------------------------------------------------------------

public record ZahlungenUebersicht(
    List<UmsatzEintrag> Klaerung,
    List<UmsatzEintrag> Zugeordnet,
    List<UmsatzEintrag> Gebucht,
    List<UmsatzEintrag> Ignoriert,
    ZahlungenStand Stand);

/// <param name="SchreibZiel">"Aus", "Test" oder "Produktiv" — wohin Zahlungen in Odoo gebucht werden.</param>
/// <param name="SchreibSperre">Warum gerade nicht gebucht wird; leer = es wird gebucht.</param>
public record ZahlungenStand(
    bool OrdnerEingerichtet, int WartendeDateien, string? LetzterLauf, string SchreibZiel, string? SchreibSperre,
    bool AutomatischBuchen);

/// <param name="Rechnung">Zugeordnete Rechnung; in der Klärung der Vorschlag.</param>
/// <param name="GebuchtZiel">"Test" oder "Produktiv", wenn gebucht.</param>
public record UmsatzEintrag(
    int Id, DateOnly Buchungstag, string? Zahler, string Verwendungszweck, decimal Betrag, string Waehrung,
    string Status, string? Hinweis, string? Rechnung, string? RechnungKunde, int? KundeId, bool Automatisch,
    DateTime? GebuchtAm, string? GebuchtZiel, string Datei);

public record OffenePostenListe(List<OffenerPosten> Posten, decimal SummeOffen, decimal SummeUeberfaellig, string? Hinweis);

/// <param name="TageUeberfaellig">Tage seit der Fälligkeit; leer = noch nicht fällig.</param>
public record OffenerPosten(
    string Nummer, string? Kunde, int? KundeId, DateOnly? Datum, DateOnly? Faellig, int? TageUeberfaellig,
    decimal Betrag, decimal Offen, string Waehrung);

public record UmsatzZuordnen(string Rechnung);

public record UmsatzIgnorieren(string? Grund);

public record ZahlungsLauf(
    int Dateien, int NeueEingaenge, int Doppelt, int Abgaenge, int Vorgemerkt, int Gebucht, int Klaerung, List<string> Meldungen);

public record ZahlungenZugang(bool Darf);
