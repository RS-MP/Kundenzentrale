using Rausoft.Erp.Domain.Customers;

namespace Rausoft.Erp.Domain.Contracts;

/// <summary>
/// Vertrag eines Kunden. Kündigungstermine werden aus Beginn/Laufzeit/Frist
/// berechnet, nicht gespeichert. Die Kette Angebot → Vertrag → Abo → Rechnung
/// ist mitgedacht: AngebotId (FK) folgt in Phase 3, Abos docken in Phase 4 an.
/// </summary>
public class Vertrag
{
    public int Id { get; set; }

    /// <summary>Aus dem zentralen Nummernkreis vergeben.</summary>
    public required string Vertragsnummer { get; set; }

    /// <summary>
    /// Frei vergebene Bezeichnung („Wartungsvertrag ID.prove“) — steht in allen
    /// Listen neben der Nummer. Reine Handpflege: Bestandsscan und Odoo-Import
    /// schreiben dieses Feld nie, ein erneuter Lauf überschreibt also nichts.
    /// Für alle sichtbar (wie die Kurzinfo), auch bei vertraulichen Verträgen.
    /// </summary>
    public string? Bezeichnung { get; set; }

    /// <summary>
    /// Anhang eines anderen Vertrags derselben Vertragspartei (Nachtrag, AVV,
    /// Zusatzvereinbarung …). Der Datensatz bleibt mit eigener Nummer und eigenen
    /// Dokumenten bestehen; Status, Laufzeit und Kündigung gelten vom
    /// Hauptvertrag — die eigenen Werte bleiben gespeichert und gelten wieder,
    /// sobald die Zuordnung gelöst wird. Genau eine Ebene, Regeln in
    /// <see cref="VertragsGruppe"/>. Ebenfalls reine Handpflege.
    /// </summary>
    public int? HauptvertragId { get; set; }
    public Vertrag? Hauptvertrag { get; set; }

    public List<Vertrag> Anhaenge { get; set; } = [];

    /// <summary>
    /// Vertragspartei: genau eines von Kunde oder Partner (FEEDBACK Nr. 20 Stufe 2,
    /// Partnerverträge — z. B. Vertriebs-/Provisionsvereinbarungen). Per
    /// Check-Constraint in der DB erzwungen.
    /// </summary>
    public int? KundeId { get; set; }
    public Kunde? Kunde { get; set; }

    public int? PartnerId { get; set; }
    public Partner? Partner { get; set; }

    public VertragsStatus Status { get; set; } = VertragsStatus.Entwurf;

    public DateOnly Beginn { get; set; }

    public int? MindestlaufzeitMonate { get; set; }

    /// <summary>Automatische Verlängerung um n Monate, wenn nicht gekündigt.</summary>
    public int? VerlaengerungMonate { get; set; }

    public int? KuendigungsfristMonate { get; set; }

    public DateOnly? GekuendigtZum { get; set; }

    public string? Notiz { get; set; }

    public int? OdooId { get; set; }

    public List<Vertragsbestandteil> Bestandteile { get; set; } = [];

    public byte[] RowVersion { get; set; } = [];

    /// <summary>
    /// Nächstmögliches Vertragsende bei Kündigung am Stichtag: Ende der
    /// Mindestlaufzeit bzw. der jeweils aktuellen Verlängerungsperiode.
    /// Null, wenn keine Laufzeitangaben gepflegt sind.
    /// </summary>
    public DateOnly? NaechstesVertragsende(DateOnly stichtag)
    {
        if (GekuendigtZum is not null)
            return GekuendigtZum;
        if (MindestlaufzeitMonate is null)
            return null;

        var ende = Beginn.AddMonths(MindestlaufzeitMonate.Value);
        var frist = KuendigungsfristMonate ?? 0;

        // Solange die Kündigungsfrist zum Stichtag nicht mehr einzuhalten ist,
        // verschiebt sich das Ende um die Verlängerungsperiode.
        while (stichtag.AddMonths(frist) > ende)
        {
            if (VerlaengerungMonate is null or <= 0)
                return ende; // keine automatische Verlängerung vereinbart
            ende = ende.AddMonths(VerlaengerungMonate.Value);
        }

        return ende;
    }
}

public enum VertragsStatus
{
    Entwurf = 0,
    Aktiv = 1,
    Gekuendigt = 2,
    Beendet = 3,
}
