namespace Rausoft.Erp.Domain.Contracts;

/// <summary>
/// Typisierter Vertragsbestandteil (explizite Anforderung: auswertbar,
/// wer z. B. eine Sondervereinbarung oder ein NDA hat).
/// </summary>
public class Vertragsbestandteil
{
    public int Id { get; set; }

    public int VertragId { get; set; }
    public Vertrag? Vertrag { get; set; }

    public BestandteilTyp Typ { get; set; }

    public required string Titel { get; set; }

    public string? Beschreibung { get; set; }

    public DateOnly? UnterzeichnetAm { get; set; }

    /// <summary>
    /// Stand des Dokuments (FEEDBACK Nr. 18) — ergibt die zeitliche Reihenfolge,
    /// z. B. bei mehreren Bestellungen. Abgeleitet aus Dateiname bzw.
    /// Dateidatum, bei Bedarf manuell korrigierbar. Für die Sortierung gilt
    /// <see cref="UnterzeichnetAm"/> vor diesem Wert (<see cref="Stand"/>).
    /// </summary>
    public DateOnly? Dokumentdatum { get; set; }

    public DokumentdatumQuelle? DokumentdatumQuelle { get; set; }

    /// <summary>Folgenummer aus dem Dateinamen (4228-2.pdf → 2) — Reihenfolge bei gleichem Datum.</summary>
    public int Folge { get; set; }

    /// <summary>
    /// Vertraulich (FEEDBACK Nr. 20): Titel, Beschreibung und Dokumente sehen nur
    /// Mitarbeiter mit der Rolle „Vertrauliche Verträge"; die Dokumente liegen in
    /// der separaten vertraulichen Ablage. Alle anderen sehen nur Dokumentart,
    /// Datum und die <see cref="Kurzinfo"/>.
    /// </summary>
    public bool Vertraulich { get; set; }

    /// <summary>Für alle sichtbare Kurzbeschreibung, z. B. „Sonderpreis 10 % auf Wartung bis 2027".</summary>
    public string? Kurzinfo { get; set; }

    /// <summary>Maßgebliches Datum für Reihenfolge und Anzeige.</summary>
    public DateOnly? Stand => UnterzeichnetAm ?? Dokumentdatum;

    public List<Vertragsdokument> Dokumente { get; set; } = [];
}

public enum BestandteilTyp
{
    Lizenzvertrag = 0,
    Avv = 1,
    Nda = 2,
    Sondervereinbarung = 3,
    Nachtrag = 4,
    Sonstiges = 5,
    Wartungsvertrag = 6,
    Bestellung = 7,
    Installationsprotokoll = 8,
    Kuendigung = 9,
}

/// <summary>Woher das <see cref="Vertragsbestandteil.Dokumentdatum"/> stammt.</summary>
public enum DokumentdatumQuelle
{
    Manuell = 0,
    Erzeugt = 1,
    Dateiname = 2,
    Dateidatum = 3,
}
