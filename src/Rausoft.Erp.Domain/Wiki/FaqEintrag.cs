using Rausoft.Erp.Domain.Customers;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Domain.Wiki;

/// <summary>
/// Supportfall in der FAQ-Datenbank (Wiki-Thema „FAQ Support"): Symptom →
/// Ursache → Lösung, schnell erfasst und durchsuchbar. Ausgangsfall (29.10.2026):
/// Ein Kunde der ID.prove 3.0 SaaS bekam die 2FA nicht eingerichtet, weil in
/// seiner Authenticator-App bereits ein alter ID.prove-Eintrag existierte.
/// Die Einträge sind die künftige Wissensbasis des Ticketsystems (Phase 3):
/// stabile Id, feste Produkt-/Kategorielisten (<see cref="FaqKatalog"/>),
/// Schlagwörter und <see cref="TicketId"/> als Andockstelle.
/// </summary>
public class FaqEintrag
{
    public int Id { get; set; }

    public required string Titel { get; set; }

    /// <summary>Schlüssel aus <see cref="FaqKatalog.Produkte"/>.</summary>
    public required string Produkt { get; set; }

    /// <summary>Schlüssel aus <see cref="FaqKatalog.Kategorien"/>.</summary>
    public required string Kategorie { get; set; }

    /// <summary>Was der Kunde meldet (WikiFormat).</summary>
    public required string Symptom { get; set; }

    /// <summary>Warum es passiert (WikiFormat), optional.</summary>
    public string? Ursache { get; set; }

    /// <summary>Lösung bzw. Schritte (WikiFormat).</summary>
    public required string Loesung { get; set; }

    /// <summary>Kommagetrennt, bereinigt (siehe FaqSuche.Schlagwoerter in den DTOs).</summary>
    public string? Schlagwoerter { get; set; }

    public FaqStatus Status { get; set; } = FaqStatus.Entwurf;

    /// <summary>Kunde, bei dem der Fall zuerst auftrat (optional).</summary>
    public int? KundeId { get; set; }
    public Kunde? Kunde { get; set; }

    /// <summary>Wie oft der Fall vorkam — beginnt bei 1 (der erfasste Fall) und zählt mit „kam wieder vor".</summary>
    public int Vorkommen { get; set; } = 1;

    public DateTime? ZuletztVorgekommenAm { get; set; }

    /// <summary>
    /// Andockstelle für das spätere Ticketsystem (ANFORDERUNGEN.md Phase 3):
    /// Kennung des Tickets, aus dem der Fall entstand. Heute nie gesetzt.
    /// </summary>
    public string? TicketId { get; set; }

    public DateTime ErstelltAm { get; set; }
    public int? ErstelltVonId { get; set; }
    public Mitarbeiter? ErstelltVon { get; set; }

    public DateTime? GeaendertAm { get; set; }
    public int? GeaendertVonId { get; set; }
    public Mitarbeiter? GeaendertVon { get; set; }

    public byte[]? RowVersion { get; set; }
}

public enum FaqStatus
{
    /// <summary>Erfasst, noch nicht geprüft — für alle sichtbar, aber als Entwurf gekennzeichnet.</summary>
    Entwurf = 0,
    /// <summary>Von Support/Redaktion geprüft.</summary>
    Freigegeben = 1,
    /// <summary>Gilt nicht mehr (z. B. nach einem Update behoben) — bleibt zur Historie erhalten.</summary>
    Veraltet = 2,
}

/// <summary>
/// Feste Auswahllisten der FAQ (keine Freitexte, damit das Ticketsystem später
/// darauf filtern kann). Neue Werte hier ergänzen — bestehende Schlüssel nie
/// umbenennen, sie stehen in der Datenbank.
/// </summary>
public static class FaqKatalog
{
    public static readonly (string Schluessel, string Name)[] Produkte =
    [
        ("idprove-saas", "ID.prove 3.0 SaaS"),
        ("idprove-onprem", "ID.prove On-Prem"),
        ("portal", "Service-Portal"),
        ("allgemein", "Allgemein"),
    ];

    public static readonly (string Schluessel, string Name)[] Kategorien =
    [
        ("anmeldung-2fa", "Anmeldung / 2FA"),
        ("lizenz", "Lizenz"),
        ("installation", "Installation"),
        ("update", "Update"),
        ("daten", "Daten / Import"),
        ("pruefung", "Prüfung / Sanktionslisten"),
        ("schnittstelle", "Schnittstelle / API"),
        ("sonstiges", "Sonstiges"),
    ];

    public static bool IstProdukt(string? schluessel) => Produkte.Any(p => p.Schluessel == schluessel);
    public static bool IstKategorie(string? schluessel) => Kategorien.Any(k => k.Schluessel == schluessel);

    public static string ProduktName(string schluessel) =>
        Produkte.FirstOrDefault(p => p.Schluessel == schluessel).Name ?? schluessel;

    public static string KategorieName(string schluessel) =>
        Kategorien.FirstOrDefault(k => k.Schluessel == schluessel).Name ?? schluessel;
}

/// <summary>
/// Wer darf was in der FAQ? Lesen alle aktiven Mitarbeiter; anlegen jeder
/// (Erfassen soll schnell gehen); eigene Einträge bearbeiten der Ersteller;
/// freigeben, veraltet setzen, fremde Einträge ändern und löschen: Support,
/// Admin, Compliance und Wiki-Redaktion mit Freigabe für das Thema „faq".
/// Reine Logik ohne Datenbank — die API ruft sie mit dem Mitarbeiter-Kontext auf.
/// </summary>
public static class FaqRegeln
{
    /// <summary>Freigeben, veraltet setzen, löschen, fremde Einträge bearbeiten.</summary>
    public static bool DarfFreigeben(MitarbeiterRolle rollen, IEnumerable<string> freigegebeneWikiThemen)
    {
        if ((rollen & (MitarbeiterRolle.Admin | MitarbeiterRolle.Compliance | MitarbeiterRolle.Support)) != 0)
            return true;
        return rollen.HasFlag(MitarbeiterRolle.WikiRedaktion) && freigegebeneWikiThemen.Contains(WikiThema.Faq);
    }

    /// <summary>Inhalt eines Eintrags ändern: der Ersteller oder wer freigeben darf.</summary>
    public static bool DarfBearbeiten(MitarbeiterRolle rollen, IEnumerable<string> freigegebeneWikiThemen,
        int eigeneId, int? erstelltVonId) =>
        erstelltVonId == eigeneId || DarfFreigeben(rollen, freigegebeneWikiThemen);

    /// <summary>Löschen: wer freigeben darf — oder der Ersteller, solange der Eintrag Entwurf ist.</summary>
    public static bool DarfLoeschen(MitarbeiterRolle rollen, IEnumerable<string> freigegebeneWikiThemen,
        int eigeneId, int? erstelltVonId, FaqStatus status) =>
        DarfFreigeben(rollen, freigegebeneWikiThemen) || (erstelltVonId == eigeneId && status == FaqStatus.Entwurf);
}
