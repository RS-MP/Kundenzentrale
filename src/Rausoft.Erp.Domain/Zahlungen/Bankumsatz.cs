using Rausoft.Erp.Domain.Customers;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Domain.Zahlungen;

/// <summary>
/// Zahlungseingang aus einem Kontoauszug (ANFORDERUNGEN.md, „Kontoauszüge", Entscheidung MP
/// 26.11.2026). Gespeichert werden nur gebuchte Eingänge — Abgänge (z. B. Gehälter) und
/// vorgemerkte Umsätze werden beim Einlesen gezählt und verworfen. Die Kontoverbindung des
/// Zahlers wird nicht gespeichert; für die Zuordnung reichen Name, Betrag und Verwendungszweck.
/// </summary>
public class Bankumsatz
{
    public int Id { get; set; }

    /// <summary>SHA-256 über die Buchung — dieselbe Buchung aus einer zweiten Datei entsteht nicht doppelt.</summary>
    public required string Fingerabdruck { get; set; }

    /// <summary>Eigenes Konto, auf dem der Betrag einging.</summary>
    public string? Auftragskonto { get; set; }

    public DateOnly Buchungstag { get; set; }
    public DateOnly? Valuta { get; set; }
    public string? Buchungstext { get; set; }
    public string Verwendungszweck { get; set; } = "";
    public string? Zahler { get; set; }
    public decimal Betrag { get; set; }
    public string Waehrung { get; set; } = "EUR";

    /// <summary>Dateiname des Kontoauszugs (ohne Pfad).</summary>
    public required string Datei { get; set; }
    public DateTime ImportiertAm { get; set; }

    public UmsatzStatus Status { get; set; }

    /// <summary>Warum der Eingang zur Klärung liegt bzw. was beim Buchen passiert ist.</summary>
    public string? Hinweis { get; set; }

    /// <summary>Zugeordnete Rechnung (Nummer aus Odoo); bei der Klärung der Vorschlag.</summary>
    public string? Rechnung { get; set; }
    public string? RechnungKunde { get; set; }
    public int? KundeId { get; set; }
    public Kunde? Kunde { get; set; }

    /// <summary>Vom System eindeutig zugeordnet (Rechnungsnummer im Verwendungszweck, Betrag exakt).</summary>
    public bool Automatisch { get; set; }
    public DateTime? ZugeordnetAm { get; set; }
    public int? ZugeordnetVonId { get; set; }
    public Mitarbeiter? ZugeordnetVon { get; set; }

    public DateTime? GebuchtAm { get; set; }
    /// <summary>In welche Odoo-Datenbank gebucht wurde — Test und Produktiv nie verwechseln.</summary>
    public OdooSchreibZiel? GebuchtZiel { get; set; }
    public string? GebuchtIn { get; set; }
    public int? OdooZahlungId { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

public enum UmsatzStatus
{
    /// <summary>Noch nicht (eindeutig) zugeordnet — ein Mensch entscheidet.</summary>
    Klaerung = 0,
    /// <summary>Rechnung steht fest, in Odoo aber noch nicht gebucht (Schreiben aus, Odoo nicht erreichbar).</summary>
    Zugeordnet = 1,
    /// <summary>Zahlung in Odoo registriert.</summary>
    Gebucht = 2,
    /// <summary>Gehört zu keiner Rechnung (z. B. Erstattung, Umbuchung).</summary>
    Ignoriert = 3,
    /// <summary>Das Buchen wurde begonnen, das Ergebnis ist unbekannt — in Odoo nachsehen, nie automatisch wiederholen.</summary>
    BuchungUnklar = 4,
}
