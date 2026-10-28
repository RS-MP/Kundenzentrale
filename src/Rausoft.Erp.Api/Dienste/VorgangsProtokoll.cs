using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Domain.Vorgaenge;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Verlaufseinträge der Vorgänge — beim Kunden, im Strang des Vorgangs und mit Verweis
/// auf ihn. Gemeinsam genutzt vom Controller und vom Odoo-Abgleich.
/// </summary>
public static class VorgangsProtokoll
{
    private static DateOnly Heute => DateOnly.FromDateTime(DateTime.Now);

    public static void Verlauf(ErpDbContext db, Vorgang v, int? ich, string betreff, string? inhalt = null,
        AktivitaetsStufe stufe = AktivitaetsStufe.Systemmeldung, AktivitaetsTyp typ = AktivitaetsTyp.Aufgabe)
    {
        var eintrag = new Aktivitaet
        {
            KundeId = v.KundeId,
            Typ = typ,
            Zeitpunkt = DateTime.UtcNow,
            MitarbeiterId = ich,
            AutorName = ich is null ? "Kundenzentrale (automatisch)" : null,
            Richtung = AktivitaetsRichtung.Intern,
            Betreff = VorgangsHilfen.Kuerzen(betreff, 300),
            Inhalt = inhalt,
            VerweisTyp = AktivitaetsVerweisTyp.Vorgang,
            VerweisId = v.Id,
            StrangSchluessel = v.StrangSchluessel,
            StrangName = v.StrangName,
            Bezug = v.StrangName,
            Stufe = stufe,
        };
        if (ich is not null)
            eintrag.Teilnehmer.Add(new AktivitaetTeilnehmer { Seite = TeilnehmerSeite.Rausoft, MitarbeiterId = ich });
        db.Aktivitaeten.Add(eintrag);
    }

    /// <summary>Weitergabe: Schritte, die jetzt in den Pool ihres Bereichs kommen.</summary>
    public static void Freigabe(ErpDbContext db, Vorgang v, int? ich, List<Aufgabe> freigegeben)
    {
        foreach (var f in freigegeben)
            Verlauf(db, v, ich, $"Jetzt dran ({f.ZustaendigBereich?.Name ?? "Pool"}): {f.Titel}");
    }

    /// <summary>
    /// Vorgang in einen anderen Verlaufsstrang hängen: Schritte und alle bisherigen
    /// Einträge des Vorgangs ziehen mit (Odoo-Verlauf des Auftrags oder eigener Strang).
    /// </summary>
    public static async Task StrangSetzenAsync(ErpDbContext db, Vorgang v, string schluessel, string name, CancellationToken ct)
    {
        v.StrangSchluessel = schluessel;
        v.StrangName = VorgangsHilfen.Kuerzen(name, 300);
        foreach (var s in v.Schritte)
        {
            s.StrangSchluessel = v.StrangSchluessel;
            s.Bezug = v.StrangName;
        }
        var eintraege = await db.Aktivitaeten
            .Where(a => a.VerweisTyp == AktivitaetsVerweisTyp.Vorgang && a.VerweisId == v.Id)
            .ToListAsync(ct);
        foreach (var a in eintraege)
        {
            a.StrangSchluessel = v.StrangSchluessel;
            a.StrangName = v.StrangName;
            a.Bezug = v.StrangName;
        }
    }

    public static void Abschluss(ErpDbContext db, Vorgang v, int? ich)
    {
        if (VorgangsLogik.AbschlussAktualisieren(v, Heute, DateTime.UtcNow))
            Verlauf(db, v, ich, $"Vorgang abgeschlossen: {v.Titel}", null, AktivitaetsStufe.Meilenstein);
    }
}
