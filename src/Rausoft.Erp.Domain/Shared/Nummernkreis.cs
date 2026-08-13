namespace Rausoft.Erp.Domain.Shared;

/// <summary>
/// Zentraler Nummernkreis (Leitplanke). Vergabe erfolgt transaktional mit
/// Concurrency-Check über <see cref="RowVersion"/> — keine Lücken, keine Dubletten.
/// </summary>
public class Nummernkreis
{
    public int Id { get; set; }

    /// <summary>Eindeutiger Schlüssel, z. B. "Kunde", "Vertrag", später "Rechnung".</summary>
    public required string Schluessel { get; set; }

    /// <summary>Präfix der erzeugten Nummern, z. B. "K" oder "V".</summary>
    public string Praefix { get; set; } = string.Empty;

    public int NaechsterWert { get; set; } = 1;

    /// <summary>Format des Zahlenteils, z. B. "D5" → K00042.</summary>
    public string Format { get; set; } = "D5";

    public byte[] RowVersion { get; set; } = [];

    public string Formatieren(int wert) => Praefix + wert.ToString(Format);
}
