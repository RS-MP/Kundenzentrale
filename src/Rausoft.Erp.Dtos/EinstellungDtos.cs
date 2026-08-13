namespace Rausoft.Erp.Dtos;

public record EinstellungEintrag(string Schluessel, string Wert, string? Beschreibung);

public record EinstellungAenderung(string Wert);
