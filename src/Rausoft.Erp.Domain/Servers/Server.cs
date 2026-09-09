namespace Rausoft.Erp.Domain.Servers;

/// <summary>
/// ID.prove-Server (Shared/Private Cloud). Der Alias ist der neutrale
/// Anzeigename und das Austauschformat mit dem Service Portal — er muss dort
/// exakt gleich angelegt sein. URL und Prüf-Mandant bleiben intern und
/// verlassen die Kundenzentrale nie.
/// Die Statusfelder halten das Ergebnis des letzten Healthchecks
/// (bewusst ohne Audit — der Takt würde das Log fluten).
/// </summary>
public class Server
{
    public int Id { get; set; }

    /// <summary>Neutraler Name, exakt wie im Service Portal (Groß-/Kleinschreibung zählt).</summary>
    public required string Alias { get; set; }

    /// <summary>Interne Basis-URL, z. B. https://shared01.idprove.intern — nur intern!</summary>
    public required string Url { get; set; }

    /// <summary>Mandanten-Kennung für den Healthcheck (?customer=…). Administrierbar.</summary>
    public string PruefMandant { get; set; } = string.Empty;

    public bool Aktiv { get; set; } = true;

    // --- Ergebnis des letzten Healthchecks -----------------------------------
    public bool? IstErreichbar { get; set; }
    public bool? ConnectorAktiv { get; set; }
    public string? SanktionslistenStand { get; set; }
    /// <summary>Generischer Fehlertext — keine internen URLs/Kundennamen (geht ans Portal).</summary>
    public string? Fehlertext { get; set; }
    public DateTime? GeprueftAm { get; set; }

    /// <summary>
    /// Release-Stand laut Service Portal (dort vom Techniker gepflegt) —
    /// kommt als Antwort der Status-Meldung zurück.
    /// </summary>
    public string? ReleaseStand { get; set; }
}
