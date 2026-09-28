using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Domain.Contracts;

namespace Rausoft.Erp.Data;

/// <summary>
/// Dokumentdatum für Vertragsbestandteile ermitteln (FEEDBACK Nr. 18):
/// Datum im Dateinamen, sonst Datei-Änderungsdatum; dazu die Folgenummer
/// aus -1/-2-Suffixen. Genutzt beim Registrieren/Scannen und als
/// idempotente Nachrüstung für den Bestand (Scanner + Administration).
/// </summary>
public class DokumentdatumDienst(ErpDbContext db)
{
    public record Ergebnis(int Geprueft, int AusDateiname, int AusDateidatum, int OhneDatei);

    /// <summary>Setzt Datum/Quelle/Folge aus einer Datei — nur, wenn noch kein Datum gepflegt ist.</summary>
    public static void Uebernehmen(Vertragsbestandteil bestandteil, string dateipfad, DateTime? dateidatum = null)
    {
        if (bestandteil.Dokumentdatum is not null)
            return;
        bestandteil.Folge = DokumentdatumErkennung.Folge(dateipfad);
        if (DokumentdatumErkennung.AusDateiname(dateipfad) is { } ausName)
        {
            bestandteil.Dokumentdatum = ausName;
            bestandteil.DokumentdatumQuelle = DokumentdatumQuelle.Dateiname;
            return;
        }
        dateidatum ??= File.Exists(dateipfad) ? File.GetLastWriteTime(dateipfad) : null;
        if (dateidatum is { } geaendert)
        {
            bestandteil.Dokumentdatum = DateOnly.FromDateTime(geaendert);
            bestandteil.DokumentdatumQuelle = DokumentdatumQuelle.Dateidatum;
        }
    }

    /// <summary>Nachrüstung: alle Bestandteile mit Dokument, aber ohne Dokumentdatum.</summary>
    public async Task<Ergebnis> NachtragenAsync(CancellationToken ct = default)
    {
        var offen = await db.Vertragsbestandteile
            .Where(b => b.Dokumentdatum == null && b.Dokumente.Any())
            .Select(b => new
            {
                Bestandteil = b,
                // Erste Version = ursprünglicher Stand des Dokuments
                Pfad = b.Dokumente.OrderBy(d => d.Version).Select(d => d.Dateipfad).First(),
            })
            .ToListAsync(ct);

        int ausName = 0, ausDatei = 0, ohneDatei = 0;
        foreach (var eintrag in offen)
        {
            Uebernehmen(eintrag.Bestandteil, eintrag.Pfad);
            switch (eintrag.Bestandteil.DokumentdatumQuelle)
            {
                case DokumentdatumQuelle.Dateiname: ausName++; break;
                case DokumentdatumQuelle.Dateidatum: ausDatei++; break;
                default: ohneDatei++; break;
            }
        }
        await db.SaveChangesAsync(ct);
        return new Ergebnis(offen.Count, ausName, ausDatei, ohneDatei);
    }
}
