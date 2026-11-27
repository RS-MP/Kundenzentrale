namespace Rausoft.Erp.Domain.Contracts;

/// <summary>
/// Liegt der unterschriebene Vertrag vor? (MP 27.11.2026: Ohne den unterschriebenen Vertrag über
/// die World-Check-Daten dürfen die Daten in der SaaS-Umgebung nicht freigeschaltet werden.)
///
/// „Liegt vor“ heißt beides zusammen, am selben Bestandteil: Die Unterschrift ist erfasst
/// (<see cref="Vertragsbestandteil.UnterzeichnetAm"/>) UND die unterschriebene Fassung ist als
/// Datei abgelegt. Die aus der Vorlage erzeugte Fassung zählt dafür nicht — sie ist der
/// unverschickte Entwurf. Ein Datum allein reicht also nicht, eine Datei allein auch nicht.
/// Reine Regel, ohne Datenbank prüfbar.
/// </summary>
public static class VertragsUnterschrift
{
    /// <summary>Bestandteile, die selbst kein Vertrag sind — an ihnen hängt keine Unterschrift des Vertrags.</summary>
    private static bool Vertragstext(BestandteilTyp typ) =>
        typ is not (BestandteilTyp.Bestellung or BestandteilTyp.Installationsprotokoll or BestandteilTyp.Kuendigung);

    private static bool DateiAbgelegt(Vertragsbestandteil b) => b.Dokumente.Any(d => d.VorlagenVersionId is null);

    /// <summary>Tag der Unterschrift, wenn die unterschriebene Fassung vorliegt — sonst nichts.</summary>
    public static DateOnly? LiegtVorSeit(Vertrag vertrag) =>
        vertrag.Bestandteile
            .Where(b => Vertragstext(b.Typ) && b.UnterzeichnetAm is not null && DateiAbgelegt(b))
            .Select(b => b.UnterzeichnetAm)
            .OrderBy(d => d)
            .FirstOrDefault();

    /// <summary>Was fehlt, in Worten für den Mitarbeiter; leer, wenn die unterschriebene Fassung vorliegt.</summary>
    public static string? WasFehlt(Vertrag vertrag)
    {
        if (LiegtVorSeit(vertrag) is not null)
            return null;
        var vertragsteile = vertrag.Bestandteile.Where(b => Vertragstext(b.Typ)).ToList();
        if (vertragsteile.Any(b => b.UnterzeichnetAm is not null))
            return "Die Unterschrift ist erfasst, aber die unterschriebene Fassung ist noch nicht abgelegt (Vertragsakte → am Bestandteil „+ Version hinzufügen“).";
        if (vertragsteile.Any(DateiAbgelegt))
            return "Eine Datei ist abgelegt, aber die Unterschrift ist nicht erfasst (Vertragsakte → Bestandteil bearbeiten → „Unterzeichnet am“).";
        return "Die unterschriebene Fassung liegt noch nicht vor: in der Vertragsakte am Bestandteil „Unterzeichnet am“ eintragen und die unterschriebene Datei als Version hinzufügen.";
    }
}
