using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Domain.Wiki;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>Aktiver Mitarbeiter als möglicher Leser einer Neuerung.</summary>
public sealed record WikiLeser(int Id, string Anzeigename, MitarbeiterRolle Rollen);

/// <summary>
/// Auswertung der Lesungen einer Wiki-Neuerung: wer gehört zum Kreis
/// (<see cref="WikiNeuerung.Zielrollen"/>), wer hat gesehen, wer bestätigt.
/// Eine Stelle für die Themenseiten (Neuerungen) und die Kommunikationsleitlinien.
/// </summary>
public static class WikiLesestand
{
    public static Task<List<WikiLeser>> LeserLadenAsync(ErpDbContext db, CancellationToken ct) =>
        db.Mitarbeiter.AsNoTracking()
            .Where(m => m.Aktiv).OrderBy(m => m.Anzeigename)
            .Select(m => new WikiLeser(m.Id, m.Anzeigename, m.Rollen))
            .ToListAsync(ct);

    /// <summary>Lesestand der Neuerung über alle Betroffenen; die Neuerung muss mit Lesungen geladen sein.</summary>
    public static (int Gesamt, int Gesehen, int Bestaetigt, List<WikiLesung> Lesungen) Auswerten(
        WikiNeuerung n, IReadOnlyList<WikiLeser> leser)
    {
        var zeilen = leser
            .Where(m => WikiNeuerung.Betrifft(n.Zielrollen, m.Rollen))
            .Select(m =>
            {
                var l = n.Lesungen.FirstOrDefault(x => x.MitarbeiterId == m.Id);
                return new WikiLesung(m.Anzeigename, l?.GesehenAm, l?.BestaetigtAm);
            })
            .ToList();
        return (zeilen.Count, zeilen.Count(z => z.GesehenAm is not null), zeilen.Count(z => z.BestaetigtAm is not null), zeilen);
    }

    /// <summary>Kreis der Neuerung als Text (z. B. „Vertrieb, Support") — null = alle Mitarbeiter.</summary>
    public static string? Zielkreis(MitarbeiterRolle zielrollen) =>
        zielrollen == MitarbeiterRolle.Keine
            ? null
            : string.Join(", ", Enum.GetValues<MitarbeiterRolle>()
                .Where(r => r != MitarbeiterRolle.Keine && zielrollen.HasFlag(r))
                .Select(r => r.ToString()));
}
