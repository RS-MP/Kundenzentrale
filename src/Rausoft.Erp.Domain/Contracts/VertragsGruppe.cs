namespace Rausoft.Erp.Domain.Contracts;

/// <summary>
/// Regeln für das Zusammenfassen von Verträgen (11/2026): Ein Vertrag kann
/// einem Hauptvertrag derselben Vertragspartei als Anhang zugeordnet werden.
/// Es gibt genau eine Ebene — ein Anhang hat keine eigenen Anhänge, ein
/// Hauptvertrag ist selbst kein Anhang. Damit sind Ketten und Kreise
/// ausgeschlossen. Unterschriebene Dokumente bleiben unberührt: Gruppiert
/// wird nur die Anzeige, verschmolzen wird nichts.
/// </summary>
public static class VertragsGruppe
{
    public const int BezeichnungMaxLaenge = 200;

    /// <summary>
    /// Prüft, ob <paramref name="anhang"/> dem Vertrag <paramref name="haupt"/>
    /// zugeordnet werden darf. Liefert den Ablehnungsgrund oder null.
    /// </summary>
    /// <param name="anhangHatAnhaenge">Der künftige Anhang ist selbst Hauptvertrag anderer Verträge.</param>
    public static string? ZuordnungsFehler(Vertrag anhang, Vertrag haupt, bool anhangHatAnhaenge)
    {
        if (anhang.Id == haupt.Id)
            return "Ein Vertrag kann nicht sein eigener Anhang sein.";
        if (!GleichePartei(anhang, haupt))
            return anhang.PartnerId is not null || haupt.PartnerId is not null
                ? "Anhang und Hauptvertrag müssen zum selben Partner bzw. Kunden gehören."
                : "Anhang und Hauptvertrag müssen zum selben Kunden gehören.";
        if (haupt.HauptvertragId is not null)
            return $"Vertrag {haupt.Vertragsnummer} ist selbst ein Anhang — bitte dessen Hauptvertrag wählen.";
        if (anhangHatAnhaenge)
            return $"Vertrag {anhang.Vertragsnummer} hat selbst Anhänge — diese zuerst lösen oder direkt dem neuen Hauptvertrag zuordnen.";
        return null;
    }

    /// <summary>Dieselbe Vertragspartei: derselbe Kunde oder derselbe Partner.</summary>
    public static bool GleichePartei(Vertrag a, Vertrag b) =>
        (a.KundeId is not null && a.KundeId == b.KundeId)
        || (a.PartnerId is not null && a.PartnerId == b.PartnerId);

    /// <summary>
    /// Bezeichnung aus der Eingabe: Leerraum und Zeilenumbrüche zusammenziehen,
    /// leer = keine Bezeichnung (null), Überlänge wird gekürzt.
    /// </summary>
    public static string? BezeichnungBereinigen(string? eingabe)
    {
        if (string.IsNullOrWhiteSpace(eingabe))
            return null;
        var text = string.Join(' ', eingabe.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length > BezeichnungMaxLaenge ? text[..BezeichnungMaxLaenge].TrimEnd() : text;
    }

    /// <summary>
    /// Ein Vertrag gilt als vertraulich, wenn er Bestandteile hat und ALLE
    /// vertraulich sind (typisch: Partnervertrag). Umbenennen und Zuordnen
    /// brauchen dann die Rolle „Vertrauliche Verträge“ — wer vom Inhalt nichts
    /// sehen darf, soll den Vertrag auch nicht umbenennen oder umhängen.
    /// </summary>
    public static bool IstVertraulich(IEnumerable<bool> bestandteileVertraulich)
    {
        var alle = bestandteileVertraulich.ToList();
        return alle.Count > 0 && alle.TrueForAll(v => v);
    }
}
