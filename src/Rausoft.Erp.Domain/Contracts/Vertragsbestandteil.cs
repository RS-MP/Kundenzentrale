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
}
