using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Api.Dienste;
using Rausoft.Erp.Domain.Customers;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Dtos;
using Rausoft.Erp.OdooImport;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Stufe 2 des Anfrage-Eingangs: eine gespeicherte Website-Anfrage auf Knopfdruck in Odoo
/// anlegen (Firma, Ansprechpartner, Verkaufschance, Aktivität). Erst die Vorschau (liest
/// Odoo, schreibt nichts), dann das Anlegen mit der Wahl des Mitarbeiters.
/// - Geschrieben wird ausschließlich über <see cref="OdooSchreibZugang.SchreibClientAsync"/>
///   an das eingestellte Ziel (Aus/Test/Produktiv).
/// - Die in Odoo vergebenen Nummern stehen je Datenbank in <see cref="AnfrageOdooAnlage"/>;
///   nur ein Anlegen in der Produktiv-Datenbank trägt die Nummer der Verkaufschance auch in
///   <see cref="Anfrage.OdooLeadId"/> ein (das liest der Lauf „anfragen-import“).
/// - Fehlertexte sind feste Kategorien; Odoos Antwort steht weder in der Oberfläche noch im Log.
/// </summary>
public partial class AnfragenController
{
    private static readonly TimeSpan AnmeldeWartezeit = TimeSpan.FromSeconds(20);
    private const int MaxStichwoerter = 20;

    /// <summary>Was in Odoo angelegt oder wiederverwendet würde — nur lesen.</summary>
    [HttpPost("{id:int}/odoo/vorschau")]
    public async Task<ActionResult<AnfrageOdooVorschau>> OdooVorschau(int id, CancellationToken ct)
    {
        if (!await DarfAsync(ct))
            return Forbid();
        var anfrage = await db.Anfragen.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (anfrage is null)
            return NotFound();
        var anlagen = await db.AnfrageOdooAnlagen.AsNoTracking().Where(x => x.AnfrageId == id).ToListAsync(ct);
        var (verbindung, sperre) = await OdooVerbindungAsync(anfrage, anlagen, ct);
        if (verbindung is null)
            return Conflict(sperre);
        var hier = anlagen.FirstOrDefault(x => x.Datenbank == verbindung.Kennung);

        var anleger = new AnfrageOdooAnleger(verbindung.Odoo, verbindung.Url);
        OdooAnlageLage lage;
        try
        {
            await verbindung.Odoo.AnmeldenAsync().WaitAsync(AnmeldeWartezeit, ct);
            lage = await anleger.LageAsync(anfrage, hier);
        }
        catch (Exception ex) when (IstOdooFehler(ex))
        {
            return StatusCode(StatusCodes.Status502BadGateway, OdooFehlerMelden(id, ex));
        }

        var namen = await KampagnenNamenAsync(ct);
        var gemerkt = OdooVorbelegung.Lesen(await db.Einstellungen.AsNoTracking()
            .Where(e => e.Schluessel == EinstellungsSchluessel.AnfragenOdooVorbelegung)
            .Select(e => e.Wert).FirstOrDefaultAsync(ct));
        static IEnumerable<(int, string)> Paare(List<OdooEintrag> liste) => liste.Select(e => (e.Id, e.Name));
        static List<OdooAuswahl> Auswahl(List<OdooEintrag> liste) => liste.Select(e => new OdooAuswahl(e.Id, e.Name)).ToList();
        var stichwort = OdooLeadRegeln.FormularStichwort(anfrage.Formular);

        AnfrageOdooLead Lead(OdooLeadInfo l) => new(l.Id, l.Name, l.Phase, l.Zustaendig, anleger.LeadLink(l.Id));
        var person = lage.Person;
        var felder = OdooLeadRegeln.TextFelder(anfrage, person);
        if (lage.LeadFelder.TryGetValue(OdooLeadRegeln.ErsterBesuchFeld, out var typ) && OdooLeadRegeln.ErsterBesuch(anfrage, typ) is { } besuch)
            felder[OdooLeadRegeln.ErsterBesuchFeld] = besuch;
        var hinweise = new List<string>(lage.Hinweise);
        if (felder.Keys.Where(f => f != OdooLeadRegeln.ErsterBesuchFeld && !lage.LeadFelder.ContainsKey(f)).ToList() is { Count: > 0 } fehlend)
            hinweise.Add($"Diese Felder gibt es in der Odoo-Datenbank nicht, sie bleiben leer: {string.Join(", ", fehlend)}.");
        if (lage.Firmen.Count == 0 && string.IsNullOrWhiteSpace(anfrage.Firma))
            hinweise.Add("Die Anfrage nennt keine Firma — es wird keine Firma angelegt, die Verkaufschance hängt am Kontakt.");
        foreach (var andere in lage.Firmen.SelectMany(f => lage.LeadsJeFirma.GetValueOrDefault(f.Firma.Id) ?? [])
                     .Concat(lage.LeadsZumKontakt).Where(l => !l.Offen).DistinctBy(l => l.Id).Take(3))
            hinweise.Add($"Bestandskunde? Die Verkaufschance „{andere.Name}“ steht in Odoo auf „{andere.Phase}“.");

        return new AnfrageOdooVorschau(
            verbindung.Ziel.ToString(), verbindung.Kennung,
            hier is null ? [] : ErledigteSchritte(hier), hier?.LetzterFehler,
            lage.Firmen.Select(f => new AnfrageOdooFirma(
                f.Firma.Id, f.Firma.Name, f.Punkte, string.Join(", ", f.Gruende),
                f.Firma.HauptansprechpartnerName,
                f.Firma.HauptansprechpartnerId is { } ha && ha == lage.Kontakt?.Id,
                anleger.KontaktLink(f.Firma.Id),
                (lage.LeadsJeFirma.GetValueOrDefault(f.Firma.Id) ?? []).Where(l => l.Offen).Select(Lead).ToList())).ToList(),
            lage.FirmaVorschlag, hier?.FirmaId is not null,
            string.IsNullOrWhiteSpace(anfrage.Firma) ? null : anfrage.Firma.Trim(),
            new AnfrageOdooKontakt(lage.Kontakt?.Id, lage.Kontakt?.Name ?? person.NameFeld,
                lage.Kontakt is null ? person.Vorname : lage.Kontakt.Vorname ?? person.Vorname,
                lage.Email, anfrage.Telefon, lage.Kontakt?.FirmaName, lage.Kontakt?.HatAnrede ?? false,
                lage.Kontakt is { } k ? anleger.KontaktLink(k.Id) : null),
            person.AnredeVorschlag == Anrede.Keine ? null : person.AnredeVorschlag.ToString(),
            hier?.KontaktId is null && lage.Kontakt is not { HatAnrede: true } && lage.AnredeIds.Count > 0,
            lage.LeadsZumKontakt.Where(l => l.Offen).Select(Lead).ToList(),
            OdooLeadRegeln.Titel(anfrage, person), hier?.LeadId,
            Auswahl(lage.Phasen),
            OdooVorbelegung.Finden(gemerkt, OdooVorbelegung.Phase(anfrage.Formular), Paare(lage.Phasen))
                ?? (stichwort is null ? null : OdooLeadRegeln.Passend(Paare(lage.Phasen), stichwort)),
            Auswahl(lage.Stichwoerter),
            Auswahl(lage.Benutzer),
            OdooVorbelegung.Finden(gemerkt, OdooVorbelegung.Zuweisung, Paare(lage.Benutzer)),
            Auswahl(lage.Aktivitaetsarten),
            OdooVorbelegung.Finden(gemerkt, OdooVorbelegung.Aktivitaet(anfrage.Formular), Paare(lage.Aktivitaetsarten))
                ?? (stichwort is null ? null : OdooLeadRegeln.Passend(Paare(lage.Aktivitaetsarten), stichwort)),
            OdooLeadRegeln.AktivitaetsText(anfrage.Formular), OdooSchreibZugang.Heute(),
            Auswahl(lage.Quellen),
            OdooVorbelegung.Finden(gemerkt, OdooVorbelegung.Quelle(anfrage), Paare(lage.Quellen))
                ?? OdooLeadRegeln.Passend(Paare(lage.Quellen), OdooLeadRegeln.QuelleStichwoerter(anfrage)),
            Auswahl(lage.Medien),
            OdooVorbelegung.Finden(gemerkt, OdooVorbelegung.Medium(anfrage), Paare(lage.Medien))
                ?? OdooLeadRegeln.Passend(Paare(lage.Medien), OdooLeadRegeln.MediumStichwoerter(anfrage)),
            felder.Where(f => lage.LeadFelder.ContainsKey(f.Key)).Select(f => new AnfrageHerkunftsFeld(f.Key, f.Value)).ToList(),
            OdooLeadRegeln.Beschreibung(anfrage, namen),
            OdooLeadRegeln.NichtUebertragen.ToList(),
            hinweise,
            OdooFirmenRegeln.FirmenDomaene(lage.Email));
    }

    /// <summary>
    /// Legt die Anfrage in Odoo an bzw. setzt einen abgebrochenen Lauf fort. Läuft bewusst
    /// ohne Abbruch-Token: Schließt der Mitarbeiter das Fenster, werden die Schritte trotzdem
    /// zu Ende geführt und gespeichert.
    /// </summary>
    [HttpPost("{id:int}/odoo/anlegen")]
    public async Task<ActionResult<AnfrageOdooErgebnis>> OdooAnlegen(int id, AnfrageOdooAnlegen daten)
    {
        if (!await DarfAsync(CancellationToken.None))
            return Forbid();
        Anrede? anrede;
        switch (daten.Anrede?.Trim().ToLowerInvariant())
        {
            case null or "": anrede = null; break;
            case "herr": anrede = Anrede.Herr; break;
            case "frau": anrede = Anrede.Frau; break;
            case "keine": anrede = Anrede.Keine; break;
            default: return BadRequest("Die Anrede muss „Herr“, „Frau“ oder „Keine“ sein.");
        }
        if (daten.StichwortIds?.Count > MaxStichwoerter)
            return BadRequest($"Höchstens {MaxStichwoerter} Stichwörter.");
        var wahl = new OdooAnlageWahl(daten.FirmaId, anrede, daten.HauptansprechpartnerUmstellen, daten.LeadId,
            daten.PhaseId, daten.StichwortIds?.Distinct().ToList() ?? [], daten.BenutzerId, daten.AktivitaetsartId,
            daten.QuelleId, daten.MediumId, daten.FirmaAnreichern);

        var anfrage = await db.Anfragen.FirstOrDefaultAsync(a => a.Id == id);
        if (anfrage is null)
            return NotFound();
        var anlagen = await db.AnfrageOdooAnlagen.Where(x => x.AnfrageId == id).ToListAsync();
        var (verbindung, sperre) = await OdooVerbindungAsync(anfrage, anlagen, CancellationToken.None);
        if (verbindung is null)
            return Conflict(sperre);

        // Lauf anmelden: Der eindeutige Index (Anfrage, Datenbank) und die Zeilenversion lassen
        // von zwei gleichzeitigen Klicks nur einen durch.
        var jetzt = DateTime.UtcNow;
        var stand = anlagen.FirstOrDefault(x => x.Datenbank == verbindung.Kennung);
        if (stand is null)
        {
            stand = new AnfrageOdooAnlage
            {
                AnfrageId = id, Datenbank = verbindung.Kennung, BegonnenAm = jetzt,
                BegonnenVonId = await angemeldet.IdAsync(),
            };
            db.AnfrageOdooAnlagen.Add(stand);
        }
        stand.Ziel = verbindung.Ziel;
        stand.LaeuftSeit = jetzt;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict("Diese Anfrage wird gerade in Odoo angelegt — bitte einen Moment warten.");
        }

        string? fehler = null;
        OdooAnlageLage? lage = null;
        try
        {
            await verbindung.Odoo.AnmeldenAsync().WaitAsync(AnmeldeWartezeit);
            var anleger = new AnfrageOdooAnleger(verbindung.Odoo, verbindung.Url);
            lage = await anleger.LageAsync(anfrage, stand);
            if (AnfrageOdooAnleger.WahlFehler(anfrage, lage, wahl, stand) is { } wahlFehler)
            {
                stand.LaeuftSeit = null;
                await db.SaveChangesAsync();
                return BadRequest(wahlFehler);
            }
            await anleger.AusfuehrenAsync(anfrage, lage, wahl, stand, await KampagnenNamenAsync(CancellationToken.None),
                OdooSchreibZugang.Heute(), DateTime.UtcNow, () => db.SaveChangesAsync());
        }
        catch (Exception ex) when (IstOdooFehler(ex))
        {
            fehler = OdooFehlerMelden(id, ex);
            stand.LetzterFehler = fehler.Length <= 300 ? fehler : fehler[..300];
        }
        stand.LaeuftSeit = null;

        if (stand.Stand == OdooAnlageStand.Fertig && lage is not null)
        {
            // Nur eine Nummer aus der Produktiv-Datenbank darf an die Anfrage — und nur, wenn die
            // Verkaufschance nicht schon zu einer anderen (aus Odoo übernommenen) Anfrage gehört.
            if (verbindung.Ziel == OdooSchreibZiel.Produktiv && anfrage.OdooLeadId is null && stand.LeadId is { } leadId
                && !await db.Anfragen.AnyAsync(a => a.OdooLeadId == leadId))
            {
                anfrage.OdooLeadId = leadId;
                anfrage.Ergebnis = AnfrageErgebnis.Offen;
            }
            await VorbelegungMerkenAsync(anfrage, lage, wahl, stand.LeadNeu);
        }
        await db.SaveChangesAsync();

        var eintrag = (await EintraegeAsync(db.Anfragen.Where(a => a.Id == id),
            await KampagnenNamenAsync(CancellationToken.None), CancellationToken.None)).Single();
        return new AnfrageOdooErgebnis(stand.Stand == OdooAnlageStand.Fertig, ErledigteSchritte(stand), fehler, eintrag);
    }

    // ---- Hilfen ----

    /// <summary>
    /// Prüft, ob diese Anfrage jetzt geschrieben werden darf, und liefert die Verbindung zum
    /// Schreibziel — oder den Grund, warum nicht.
    /// </summary>
    private async Task<(OdooSchreibZugang.SchreibVerbindung? Verbindung, string? Sperre)> OdooVerbindungAsync(
        Anfrage anfrage, IReadOnlyCollection<AnfrageOdooAnlage> anlagen, CancellationToken ct)
    {
        var odooStand = await zugang.StandAsync(ct);
        if (zugang.SchreibSperre(odooStand) is { } aus)
            return (null, aus);
        if (OdooAnlageRegeln.Sperre(anfrage, anlagen, OdooSchreibZugang.ZielKennung(odooStand)!, DateTime.UtcNow) is { } sperre)
            return (null, sperre);
        try
        {
            return (await zugang.SchreibClientAsync(ct), null);
        }
        catch (InvalidOperationException ex)
        {
            return (null, ex.Message); // eigene Klartext-Meldung (Zugangsdaten unvollständig, abgelaufen …)
        }
    }

    private static bool IstOdooFehler(Exception ex) =>
        ex is OdooSchrittFehler or OdooFehlerException or HttpRequestException or TimeoutException
            or TaskCanceledException or System.Text.Json.JsonException
        || (ex is InvalidOperationException && ex.Message.StartsWith("Odoo-Anmeldung fehlgeschlagen"));

    /// <summary>Fester Text für die Oberfläche; ins Log gehen nur Schritt und Art des Fehlers, nie Odoos Antwort.</summary>
    private string OdooFehlerMelden(int anfrageId, Exception ex)
    {
        var schritt = (ex as OdooSchrittFehler)?.Schritt;
        log.LogWarning("Anfrage {AnfrageId} in Odoo anlegen: {Schritt} fehlgeschlagen ({Art})",
            anfrageId, schritt ?? "Anmeldung", OdooFehlerText.Art(ex));
        return schritt is null ? OdooFehlerText.Fuer(ex) : $"{schritt}: {OdooFehlerText.Fuer(ex)}";
    }

    /// <summary>Was in dieser Odoo-Datenbank schon steht — für Vorschau (Fortsetzung) und Ergebnis.</summary>
    private static List<string> ErledigteSchritte(AnfrageOdooAnlage s)
    {
        var schritte = new List<string>();
        if (s.FirmaId is { } firma)
            schritte.Add($"Firma {(s.FirmaNeu ? "angelegt" : "wiederverwendet")} (Nr. {firma})");
        if (s.KontaktId is { } kontakt)
            schritte.Add($"Ansprechpartner {(s.KontaktNeu ? "angelegt" : "wiederverwendet")} (Nr. {kontakt})");
        if (s.HauptansprechpartnerErledigt && s.FirmaId is not null)
            schritte.Add("Hauptansprechpartner geprüft");
        if (s.LeadId is { } lead)
            schritte.Add(s.LeadNeu ? $"Verkaufschance angelegt (Nr. {lead})" : $"Vorhandene Verkaufschance ergänzt (Nr. {lead})");
        if (s.AktivitaetErledigt)
            schritte.Add(s.AktivitaetId is { } aktivitaet
                ? $"Aktivität angelegt (Nr. {aktivitaet})"
                : "Aktivität: eine gleiche offene bestand bereits");
        return schritte;
    }

    private AnfrageOdooAnlageInfo? OdooAnlageInfo(
        IReadOnlyCollection<AnfrageOdooAnlage> anlagen, OdooSchreibZugang.Stand stand, string? kennung)
    {
        // Maßgeblich: was in der Produktiv-Datenbank fertig ist, sonst der Stand in der aktuellen Datenbank, sonst der jüngste
        var anlage = anlagen.FirstOrDefault(x => x is { Ziel: OdooSchreibZiel.Produktiv, Stand: OdooAnlageStand.Fertig })
                     ?? anlagen.FirstOrDefault(x => x.Datenbank == kennung)
                     ?? anlagen.OrderByDescending(x => x.BegonnenAm).FirstOrDefault();
        if (anlage is null)
            return null;
        var url = anlage.Datenbank == OdooAnlageRegeln.Kennung(stand.TestUrl, stand.TestDatenbank) ? stand.TestUrl
            : anlage.Datenbank == OdooAnlageRegeln.Kennung(stand.ProdUrl, stand.ProdDatenbank) ? stand.ProdUrl
            : null;
        return new AnfrageOdooAnlageInfo(
            anlage.Ziel.ToString(), anlage.Stand == OdooAnlageStand.Fertig,
            Ortszeit.VonUtc(anlage.AbgeschlossenAm ?? anlage.BegonnenAm), anlage.LeadId,
            anlage.LeadId is { } lead && !string.IsNullOrEmpty(url) ? AnfrageOdooAnleger.LeadLink(url, lead) : null,
            anlage.Stand == OdooAnlageStand.Fertig ? null : anlage.LetzterFehler,
            anlage.Datenbank == kennung);
    }

    /// <summary>Merkt sich die Wahl als Namen — beim nächsten Mal ist sie vorbelegt.</summary>
    private async Task VorbelegungMerkenAsync(Anfrage anfrage, OdooAnlageLage lage, OdooAnlageWahl wahl, bool leadNeu)
    {
        static string? Name(List<OdooEintrag> liste, int? id) => liste.FirstOrDefault(e => e.Id == id)?.Name;
        var merken = new List<KeyValuePair<string, string?>>
        {
            new(OdooVorbelegung.Zuweisung, Name(lage.Benutzer, wahl.BenutzerId)),
            new(OdooVorbelegung.Aktivitaet(anfrage.Formular), Name(lage.Aktivitaetsarten, wahl.AktivitaetsartId)),
        };
        if (leadNeu)
        {
            merken.Add(new(OdooVorbelegung.Phase(anfrage.Formular), Name(lage.Phasen, wahl.PhaseId)));
            merken.Add(new(OdooVorbelegung.Quelle(anfrage), Name(lage.Quellen, wahl.QuelleId)));
            merken.Add(new(OdooVorbelegung.Medium(anfrage), Name(lage.Medien, wahl.MediumId)));
        }
        var einstellung = await db.Einstellungen.FirstOrDefaultAsync(e => e.Schluessel == EinstellungsSchluessel.AnfragenOdooVorbelegung);
        if (einstellung is null)
            db.Einstellungen.Add(einstellung = new Einstellung { Schluessel = EinstellungsSchluessel.AnfragenOdooVorbelegung });
        einstellung.Wert = OdooVorbelegung.Merken(einstellung.Wert, merken, MaxAusschlussLaenge);
    }
}
