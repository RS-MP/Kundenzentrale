using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Domain.Wiki;

/// <summary>
/// Neuerung zu einem Wiki-Thema — „was hat sich geändert" in eigenen Worten
/// (z. B. je importierter Preislisten-Version). Steht oben auf der Themenseite.
/// Je Mitarbeiter wird protokolliert, wer sie gesehen und — wenn gefordert —
/// bestätigt hat (<see cref="WikiNeuerungLesung"/>).
/// </summary>
public class WikiNeuerung
{
    public int Id { get; set; }

    /// <summary>Schlüssel aus <see cref="WikiThema"/>.</summary>
    public required string Thema { get; set; }

    public required string Titel { get; set; }

    /// <summary>Beschreibung der Änderungen (Fließtext, Zeilenumbrüche erlaubt).</summary>
    public required string Text { get; set; }

    /// <summary>Verknüpfte Preislisten-Version, wenn die Neuerung aus einem Import stammt.</summary>
    public int? PreislistenVersionId { get; set; }
    public PreislistenVersion? PreislistenVersion { get; set; }

    /// <summary>
    /// true: jeder Mitarbeiter muss das Lesen ausdrücklich bestätigen (Hinweis
    /// bleibt stehen, bis bestätigt). false: es wird nur festgehalten, wer die
    /// Neuerung gesehen hat.
    /// </summary>
    public bool BestaetigungErforderlich { get; set; }

    public DateTime ErstelltAm { get; set; }
    public int? ErstelltVonId { get; set; }
    public Mitarbeiter? ErstelltVon { get; set; }

    public DateTime? GeaendertAm { get; set; }

    public List<WikiNeuerungLesung> Lesungen { get; set; } = [];
}

/// <summary>Wer hat eine Neuerung wann gesehen bzw. bestätigt — eine Zeile je Mitarbeiter.</summary>
public class WikiNeuerungLesung
{
    public int Id { get; set; }

    public int NeuerungId { get; set; }
    public WikiNeuerung? Neuerung { get; set; }

    public int MitarbeiterId { get; set; }
    public Mitarbeiter? Mitarbeiter { get; set; }

    /// <summary>Erstes Anzeigen der Neuerung auf der Themenseite.</summary>
    public DateTime GesehenAm { get; set; }

    /// <summary>Ausdrückliche Bestätigung „gelesen" (nur bei Bestätigungspflicht relevant).</summary>
    public DateTime? BestaetigtAm { get; set; }
}

/// <summary>
/// Meldung eines Mitarbeiters zu einem Wiki-Thema — etwas fehlt oder ist falsch.
/// Verantwortlicher und Vertretung des Themas (und die Redaktion) sehen offene
/// Meldungen in der Navigation und auf der Themenseite und erledigen sie mit
/// einer Antwort.
/// </summary>
public class WikiMeldung
{
    public int Id { get; set; }

    /// <summary>Schlüssel aus <see cref="WikiThema"/>.</summary>
    public required string Thema { get; set; }

    /// <summary>Worauf sich die Meldung bezieht (Abschnitt, Liste …), frei gewählt.</summary>
    public string? Bezug { get; set; }

    public required string Text { get; set; }

    public DateTime ErstelltAm { get; set; }
    public int? ErstelltVonId { get; set; }
    public Mitarbeiter? ErstelltVon { get; set; }

    public DateTime? ErledigtAm { get; set; }
    public int? ErledigtVonId { get; set; }
    public Mitarbeiter? ErledigtVon { get; set; }

    /// <summary>Rückmeldung an den Melder (was wurde geändert / warum nicht).</summary>
    public string? Antwort { get; set; }
}
