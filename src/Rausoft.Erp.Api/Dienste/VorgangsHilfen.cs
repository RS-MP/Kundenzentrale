using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Domain.Vorgaenge;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>Gemeinsame Hilfen der Vorgangs-Controller: angemeldeter Mitarbeiter, Pflegerecht, Status als Text.</summary>
public static class VorgangsHilfen
{
    /// <summary>Vorgangstypen pflegen dürfen Admins und Bereichsleiter (Entscheidung 28.10.2026).</summary>
    public static async Task<bool> DarfTypenPflegenAsync(ErpDbContext db, int? ich, CancellationToken ct)
    {
        if (ich is null)
            return false;
        var rollen = await db.Mitarbeiter.Where(m => m.Id == ich && m.Aktiv).Select(m => m.Rollen).FirstOrDefaultAsync(ct);
        return rollen.HasFlag(MitarbeiterRolle.Admin) || await db.Bereiche.AnyAsync(b => b.LeiterId == ich && b.Aktiv, ct);
    }

    /// <summary>Verknüpft „wartet auf" zwischen den Schritten eines Vorgangs (auch bei Abfragen ohne Tracking).</summary>
    public static void Verknuepfen(List<Aufgabe> schritte)
    {
        var jeId = schritte.ToDictionary(s => s.Id);
        foreach (var s in schritte)
            s.WartetAuf = s.WartetAufId is { } w && jeId.TryGetValue(w, out var v) ? v : null;
    }

    public static string StatusText(VorgangsStatus status) => status switch
    {
        VorgangsStatus.Neu => "neu",
        VorgangsStatus.Laeuft => "laeuft",
        VorgangsStatus.WartetAufKunden => "wartet",
        VorgangsStatus.Haengt => "haengt",
        _ => "fertig",
    };

    /// <summary>Sortierung der Übersicht: hängende zuerst, dann neue, laufende, wartende, abgeschlossene.</summary>
    public static int Prioritaet(VorgangsStatus status) => status switch
    {
        VorgangsStatus.Haengt => 0,
        VorgangsStatus.Neu => 1,
        VorgangsStatus.Laeuft => 2,
        VorgangsStatus.WartetAufKunden => 3,
        _ => 4,
    };

    public static string SchrittStatus(Aufgabe s, DateOnly heute) => s.Status switch
    {
        AufgabenStatus.Erledigt => "erledigt",
        AufgabenStatus.Entfallen => "entfallen",
        _ when VorgangsLogik.Gesperrt(s) => "gesperrt",
        AufgabenStatus.WartetAufKunden => VorgangsLogik.WartetAufKunden(s, heute) ? "wartet" : "wiedervorlage",
        AufgabenStatus.InArbeit or AufgabenStatus.Rueckfrage => "inarbeit",
        _ => "offen",
    };

    /// <summary>
    /// Strang des importierten Odoo-Verlaufs zum Auftrag (z. B. „Verkauf S01194") —
    /// so liegen Mails, Odoo-Aktivitäten und die Schritte in der Akte in einem Strang.
    /// </summary>
    public static async Task<(string Schluessel, string Name)?> OdooStrangAsync(
        ErpDbContext db, int kundeId, string? auftrag, CancellationToken ct)
    {
        if (auftrag is null)
            return null;
        string[] bezuege = [$"Auftrag {auftrag}", $"Angebot {auftrag}"];
        var treffer = await db.Aktivitaeten.AsNoTracking()
            .Where(a => a.KundeId == kundeId && a.OdooModell == "sale.order" && a.StrangSchluessel != null
                        && a.Bezug != null && bezuege.Contains(a.Bezug))
            .OrderByDescending(a => a.Zeitpunkt)
            .Select(a => new { a.StrangSchluessel, a.StrangName })
            .FirstOrDefaultAsync(ct);
        return treffer is null ? null : (treffer.StrangSchluessel!, treffer.StrangName ?? $"Verkauf {auftrag}");
    }

    public static string Kuerzen(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
