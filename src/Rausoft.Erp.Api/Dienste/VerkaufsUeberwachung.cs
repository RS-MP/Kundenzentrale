using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Domain.Vorgaenge;
using Rausoft.Erp.Dtos;
using Rausoft.Erp.OdooImport;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Offene Angebote und Aufträge aus Odoo (ANFORDERUNGEN.md, Entscheidung MP 26.11.2026):
/// 1. Übersicht — gesendete Angebote ohne Reaktion und bestätigte Aufträge, die niemand
///    gestartet hat: weder Vorgang hier noch Projekt in Odoo (und ohne Rechnung:
///    Abo-Verlängerungen stellt Odoo selbst in Rechnung). Aufträge mit laufendem Odoo-Projekt
///    stehen mit dessen Stand in einer eigenen Liste (<see cref="AuftragsProjekte"/>).
/// 2. Nachfass-Aufgaben — nach der eingestellten Frist beim Verkäufer des Angebots; erledigt
///    sich von selbst, sobald das Angebot in Odoo bestätigt oder storniert ist.
/// 3. Wochenübersicht per Mail.
/// Odoo wird nur gelesen. Stündlicher Takt (<see cref="VerkaufsHintergrund"/>) und Knopf.
/// </summary>
public class VerkaufsUeberwachung(
    ErpDbContext db, IServiceScopeFactory scopeFactory, IConfiguration konfiguration, MailDienst mail,
    ILogger<VerkaufsUeberwachung> log)
{
    private static readonly TimeSpan Wartezeit = TimeSpan.FromSeconds(25);

    // Takt und Knopf dürfen nicht gleichzeitig Aufgaben anlegen — sonst entstehen sie doppelt
    private static readonly SemaphoreSlim Sperre = new(1, 1);

    private static DateOnly Heute => DateOnly.FromDateTime(DateTime.Now);

    public record Einstellungen(
        int NachfassTage, int WiederholungTage, int RueckblickTage,
        string BerichtEmpfaenger, string BerichtTag, string ErlaubteDomaenen, string OdooUrl);

    public async Task<Einstellungen> EinstellungenAsync(CancellationToken ct)
    {
        var w = await db.Einstellungen.AsNoTracking()
            .Where(e => e.Schluessel.StartsWith("Verkauf.")
                        || e.Schluessel == EinstellungsSchluessel.ZertifikateEmpfaengerDomaenen
                        || e.Schluessel == EinstellungsSchluessel.MailAbsender
                        || e.Schluessel == EinstellungsSchluessel.OdooUrl)
            .ToDictionaryAsync(e => e.Schluessel, e => e.Wert, ct);
        return new Einstellungen(
            NachfassRegeln.Zahl(w.GetValueOrDefault(EinstellungsSchluessel.VerkaufNachfassTage), NachfassRegeln.StandardTage),
            NachfassRegeln.Zahl(w.GetValueOrDefault(EinstellungsSchluessel.VerkaufNachfassWiederholungTage), NachfassRegeln.StandardWiederholungTage),
            Math.Max(1, NachfassRegeln.Zahl(w.GetValueOrDefault(EinstellungsSchluessel.VerkaufRueckblickTage), NachfassRegeln.StandardRueckblickTage)),
            w.GetValueOrDefault(EinstellungsSchluessel.VerkaufBerichtEmpfaenger, ""),
            w.GetValueOrDefault(EinstellungsSchluessel.VerkaufBerichtTag, ""),
            BerichtsRegeln.DomaenenText(w.GetValueOrDefault(EinstellungsSchluessel.ZertifikateEmpfaengerDomaenen), w.GetValueOrDefault(EinstellungsSchluessel.MailAbsender)),
            w.GetValueOrDefault(EinstellungsSchluessel.OdooUrl, "").Trim().TrimEnd('/'));
    }

    // --- Übersicht -------------------------------------------------------------------

    public async Task<VerkaufUebersicht> UebersichtAsync(CancellationToken ct)
    {
        var e = await EinstellungenAsync(ct);
        var heute = Heute;
        var (odoo, verkaeufe, hinweis) = await LadenAsync(e, heute, ct);
        if (verkaeufe is null || odoo is null)
            return new VerkaufUebersicht([], [], [], [], e.NachfassTage, e.RueckblickTage, false, hinweis);

        var nummern = verkaeufe.Select(v => v.Nummer).ToList();
        var kunden = await KundenAsync(verkaeufe, ct);
        var mitVorgang = (await db.Vorgaenge.AsNoTracking()
                .Where(v => v.OdooAuftrag != null && nummern.Contains(v.OdooAuftrag))
                .Select(v => v.OdooAuftrag!).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var merker = (await db.VerkaufsMerker.AsNoTracking()
                .Where(m => nummern.Contains(m.Nummer))
                .Select(m => new
                {
                    m.Nummer, m.AusgeblendetAm, m.AusblendGrund, m.NachfassAnzahl,
                    AusgeblendetVon = m.AusgeblendetVon != null ? m.AusgeblendetVon.Anzeigename : null,
                    AufgabeStatus = m.NachfassAufgabe != null ? (AufgabenStatus?)m.NachfassAufgabe.Status : null,
                    AufgabeFaellig = m.NachfassAufgabe != null ? m.NachfassAufgabe.Faellig : null,
                    AufgabeBei = m.NachfassAufgabe == null ? null
                        : m.NachfassAufgabe.Zustaendig != null ? m.NachfassAufgabe.Zustaendig.Anzeigename : m.NachfassAufgabe.ZustaendigName,
                })
                .ToListAsync(ct))
            .ToDictionary(m => m.Nummer, StringComparer.OrdinalIgnoreCase);

        // Bestätigte Aufträge ohne Vorgang: Läuft dazu ein Projekt in Odoo? (Dort entsteht es,
        // sobald die Verkaufschance als gewonnen bestätigt wird.)
        var ohneVorgang = verkaeufe.Where(v => v.Bestaetigt && !mitVorgang.Contains(v.Nummer)).ToList();
        var (projekte, projektHinweis) = await ProjekteAsync(odoo, ohneVorgang, ct);

        VerkaufProjekt Projekt(OdooProjekt p) => new(
            p.Name, p.Bezug == ProjektBezug.Kunde, p.Phase, p.Status, p.Leitung, p.AngelegtAm,
            p.Aufgaben, p.AufgabenErledigt, p.LetzteBewegung, NachfassRegeln.TageOffen(p.LetzteBewegung, heute), p.Abgeschlossen,
            e.OdooUrl.Length > 0 ? $"{e.OdooUrl}/web#id={p.Id}&model=project.project&view_type=form" : null);

        VerkaufZeile Zeile(OdooVerkauf v)
        {
            var kunde = v.FirmaId is { } f ? kunden.GetValueOrDefault(f) : null;
            var m = merker.GetValueOrDefault(v.Nummer);
            var aufgabeOffen = m?.AufgabeStatus is { } s && s is not (AufgabenStatus.Erledigt or AufgabenStatus.Entfallen);
            return new VerkaufZeile(
                v.Nummer, v.Bestaetigt, kunde?.Id, kunde?.Firmenname ?? v.PartnerName ?? "(ohne Kunde)", kunde?.Kundennummer,
                v.Datum, NachfassRegeln.TageOffen(v.Datum, heute), v.GueltigBis,
                !v.Bestaetigt && v.GueltigBis is { } g && g < heute,
                v.Verkaeufer, KundenAuftraege.Inhalt(v.Produkte), v.Referenz, v.Abo,
                e.OdooUrl.Length > 0 ? $"{e.OdooUrl}/web#id={v.Id}&model=sale.order&view_type=form" : null,
                aufgabeOffen, aufgabeOffen ? m!.AufgabeFaellig : null, aufgabeOffen ? m!.AufgabeBei : null,
                m?.NachfassAnzahl ?? 0, m?.AusgeblendetAm, m?.AusgeblendetVon, m?.AusblendGrund,
                projekte.GetValueOrDefault(v.Id)?.Select(Projekt).ToList());
        }

        // Drei Fälle:
        // · Angebot gesendet, keine Reaktion
        // · Auftrag bestätigt, weder Rechnung noch Vorgang noch Odoo-Projekt → niemand hat angefangen
        // · Auftrag bestätigt, kein Vorgang, aber ein laufendes Odoo-Projekt → läuft in Odoo
        //   (auch mit Rechnung: die Rechnung sagt nichts darüber, ob das Projekt fertig ist).
        // Ein Auftrag, dessen Odoo-Projekte alle abgeschlossen sind, ist erledigt und erscheint nicht.
        bool Laeuft(OdooVerkauf v) => projekte.GetValueOrDefault(v.Id)?.Any(p => !p.Abgeschlossen) == true;
        var relevant = verkaeufe
            .Where(v => !v.Bestaetigt
                        || (!mitVorgang.Contains(v.Nummer) && (Laeuft(v) || (!v.HatRechnung && !projekte.ContainsKey(v.Id)))))
            .Select(Zeile)
            .OrderByDescending(z => z.Tage ?? 0).ThenBy(z => z.Nummer, StringComparer.Ordinal)
            .ToList();
        var sichtbar = relevant.Where(z => z.AusgeblendetAm is null).ToList();
        return new VerkaufUebersicht(
            sichtbar.Where(z => !z.Bestaetigt).ToList(),
            sichtbar.Where(z => z.Bestaetigt && z.Projekte is null).ToList(),
            // Was am längsten stillsteht, zuerst
            sichtbar.Where(z => z.Bestaetigt && z.Projekte is not null)
                .OrderByDescending(z => z.Projekte!.Where(p => !p.Abgeschlossen).Min(p => p.TageOhneBewegung ?? 0))
                .ThenBy(z => z.Nummer, StringComparer.Ordinal).ToList(),
            relevant.Where(z => z.AusgeblendetAm is not null).ToList(),
            e.NachfassTage, e.RueckblickTage, true,
            verkaeufe.Count >= OffeneVerkaeufe.MaxZeilen
                ? $"Odoo lieferte mehr als {OffeneVerkaeufe.MaxZeilen} Angebote und Aufträge — die Liste ist abgeschnitten. Bitte den Rückblick verkürzen."
                : null,
            projektHinweis);
    }

    public async Task AusblendenAsync(string nummer, string? grund, int? mitarbeiterId, CancellationToken ct)
    {
        var m = await MerkerAsync(nummer, ct);
        m.AusgeblendetAm = DateTime.UtcNow;
        m.AusgeblendetVonId = mitarbeiterId;
        m.AusblendGrund = string.IsNullOrWhiteSpace(grund) ? null : VorgangsHilfen.Kuerzen(grund.Trim(), 200);
        await db.SaveChangesAsync(ct);
    }

    public async Task EinblendenAsync(string nummer, CancellationToken ct)
    {
        var m = await MerkerAsync(nummer, ct);
        m.AusgeblendetAm = null;
        m.AusgeblendetVonId = null;
        m.AusblendGrund = null;
        await db.SaveChangesAsync(ct);
    }

    private async Task<VerkaufsMerker> MerkerAsync(string nummer, CancellationToken ct)
    {
        var m = await db.VerkaufsMerker.FirstOrDefaultAsync(x => x.Nummer == nummer, ct);
        if (m is null)
            db.VerkaufsMerker.Add(m = new VerkaufsMerker { Nummer = nummer });
        return m;
    }

    // --- Nachfass-Aufgaben -----------------------------------------------------------

    public async Task<VerkaufLaufErgebnis> NachfassenAsync(CancellationToken ct)
    {
        if (!await Sperre.WaitAsync(TimeSpan.Zero, ct))
            return new VerkaufLaufErgebnis(0, 0, "Die Prüfung läuft gerade bereits — bitte gleich noch einmal aktualisieren.");
        try
        {
            var e = await EinstellungenAsync(ct);
            var heute = Heute;
            var (_, verkaeufe, hinweis) = await LadenAsync(e, heute, ct);
            if (verkaeufe is null)
                return new VerkaufLaufErgebnis(0, 0, hinweis);

            var alle = verkaeufe.ToDictionary(v => v.Nummer, StringComparer.OrdinalIgnoreCase);
            var nummern = alle.Keys.ToList();
            var merker = await db.VerkaufsMerker.Include(m => m.NachfassAufgabe)
                .Where(m => nummern.Contains(m.Nummer)
                            || (m.NachfassAufgabe != null && m.NachfassAufgabe.Status != AufgabenStatus.Erledigt
                                && m.NachfassAufgabe.Status != AufgabenStatus.Entfallen))
                .ToListAsync(ct);

            // 1. Aufgaben schließen, deren Angebot in Odoo nicht mehr offen steht. Ist die Liste
            //    abgeschnitten, fehlt ein Angebot evtl. nur deshalb — dann nichts schließen.
            var geschlossen = 0;
            if (verkaeufe.Count < OffeneVerkaeufe.MaxZeilen)
                foreach (var m in merker.Where(m => m.NachfassAufgabe is { } a && Offen(a)))
                {
                    var stand = alle.GetValueOrDefault(m.Nummer);
                    if (stand is { Bestaetigt: false })
                        continue;
                    Schliessen(m.NachfassAufgabe!, stand is not null
                        ? $"Das Angebot {m.Nummer} ist in Odoo bestätigt."
                        : $"Das Angebot {m.Nummer} steht in Odoo nicht mehr als gesendet (storniert, zurückgesetzt oder älter als {e.RueckblickTage} Tage).");
                    geschlossen++;
                }

            // 2. Neue Aufgaben für gesendete Angebote, deren Frist erreicht ist
            var jeNummer = merker.ToDictionary(m => m.Nummer, StringComparer.OrdinalIgnoreCase);
            var kunden = await KundenAsync(verkaeufe, ct);
            var mitarbeiter = (await db.Mitarbeiter.AsNoTracking().Where(m => m.Aktiv).Select(m => new { m.Id, m.Email }).ToListAsync(ct))
                .GroupBy(m => m.Email, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
            var angelegt = 0;
            foreach (var v in verkaeufe.Where(v => !v.Bestaetigt))
            {
                var m = jeNummer.GetValueOrDefault(v.Nummer);
                if (m?.AusgeblendetAm is not null)
                    continue;
                var letzter = m?.NachfassAufgabe?.ErledigtAm is { } erledigt
                    ? DateOnly.FromDateTime(erledigt.ToLocalTime())
                    : m?.NachfassZuletztAm;
                if (!NachfassRegeln.AufgabeFaellig(v.Datum, heute, e.NachfassTage,
                        m?.NachfassAufgabe is { } a && Offen(a), letzter, e.WiederholungTage))
                    continue;

                var kunde = v.FirmaId is { } f ? kunden.GetValueOrDefault(f) : null;
                var strang = kunde is null ? null : await VorgangsHilfen.OdooStrangAsync(db, kunde.Id, v.Nummer, ct);
                int? zustaendig = v.VerkaeuferLogin is { } login && mitarbeiter.TryGetValue(login, out var id) ? id : null;
                var tage = NachfassRegeln.TageOffen(v.Datum, heute);
                var aufgabe = new Aufgabe
                {
                    KundeId = kunde?.Id,
                    Titel = VorgangsHilfen.Kuerzen(
                        kunde is null ? $"Angebot {v.Nummer} nachfassen ({v.PartnerName ?? "Kunde unbekannt"})" : $"Angebot {v.Nummer} nachfassen", 300),
                    Art = "Anruf",
                    Beschreibung =
                        $"Das Angebot {v.Nummer}{(v.Datum is { } d ? $" vom {d:dd.MM.yyyy}" : "")} ist seit {tage} Tagen ohne Rückmeldung" +
                        $"{(v.GueltigBis is { } g ? $" (gültig bis {g:dd.MM.yyyy})" : "")}." +
                        $"{(KundenAuftraege.Inhalt(v.Produkte) is { } inhalt ? $"\nInhalt: {inhalt}" : "")}" +
                        "\nBitte beim Kunden nachfragen und das Ergebnis in Odoo festhalten (bestätigen oder stornieren) — " +
                        "dann erledigt sich diese Aufgabe von selbst.",
                    Faellig = heute,
                    ZustaendigId = zustaendig,
                    ZustaendigName = zustaendig is null && v.Verkaeufer is not null ? VorgangsHilfen.Kuerzen(v.Verkaeufer, 200) : null,
                    Bezug = strang?.Name ?? $"Angebot {v.Nummer}",
                    StrangSchluessel = strang?.Schluessel,
                    Quelle = AktivitaetsQuelle.Kundenzentrale,
                    ErstelltAm = DateTime.UtcNow,
                };
                if (m is null)
                    db.VerkaufsMerker.Add(m = new VerkaufsMerker { Nummer = v.Nummer });
                m.NachfassAufgabe = aufgabe;
                m.NachfassZuletztAm = heute;
                m.NachfassAnzahl++;
                angelegt++;
            }

            if (angelegt + geschlossen > 0)
            {
                await db.SaveChangesAsync(ct);
                log.LogInformation("Offene Angebote: {Angelegt} Nachfass-Aufgabe(n) angelegt, {Geschlossen} automatisch erledigt",
                    angelegt, geschlossen);
            }
            return new VerkaufLaufErgebnis(angelegt, geschlossen, null);
        }
        finally
        {
            Sperre.Release();
        }
    }

    private static bool Offen(Aufgabe a) => a.Status is not (AufgabenStatus.Erledigt or AufgabenStatus.Entfallen);

    private void Schliessen(Aufgabe aufgabe, string grund)
    {
        aufgabe.Status = AufgabenStatus.Erledigt;
        aufgabe.ErledigtAm = DateTime.UtcNow;
        if (aufgabe.KundeId is { } kundeId)
            db.Aktivitaeten.Add(new Aktivitaet
            {
                KundeId = kundeId,
                Typ = AktivitaetsTyp.Aufgabe,
                Zeitpunkt = DateTime.UtcNow,
                Richtung = AktivitaetsRichtung.Intern,
                Betreff = VorgangsHilfen.Kuerzen($"Aufgabe automatisch erledigt: {aufgabe.Titel}", 300),
                Inhalt = grund,
                Bezug = aufgabe.Bezug,
                StrangSchluessel = aufgabe.StrangSchluessel,
                StrangName = aufgabe.Bezug,
            });
    }

    // --- Wochenübersicht ---------------------------------------------------------------

    /// <summary>Wochenübersicht am eingestellten Wochentag; <paramref name="erzwingen"/> sendet sofort (Knopf).</summary>
    /// <returns>Leer = gesendet; sonst der Grund, warum nichts gesendet wurde.</returns>
    public async Task<string?> WochenberichtAsync(bool erzwingen, CancellationToken ct)
    {
        var e = await EinstellungenAsync(ct);
        var heute = Heute;
        var letzter = await db.Einstellungen.FirstOrDefaultAsync(x => x.Schluessel == EinstellungsSchluessel.VerkaufLetzterBericht, ct);
        if (!erzwingen && !BerichtsRegeln.Faellig(e.BerichtTag, letzter?.Wert, heute))
            return "Heute nicht fällig.";
        var empfaenger = BerichtsRegeln.Empfaenger(e.BerichtEmpfaenger, e.ErlaubteDomaenen);
        if (empfaenger.Count == 0)
            return "Kein zulässiger Empfänger eingetragen (Administration → Offene Angebote und Aufträge).";

        var u = await UebersichtAsync(ct);
        if (!u.OdooErreichbar)
            return u.Hinweis ?? "Odoo ist nicht erreichbar.";

        static string Zeile(VerkaufZeile z, bool angebot) =>
            $"- {z.Nummer} · {z.Kunde} · {(angebot ? "offen" : "bestätigt")} seit {z.Tage ?? 0} Tagen" +
            $"{(z.Abgelaufen ? " · GÜLTIGKEIT ABGELAUFEN" : "")}" +
            $"{(z.Verkaeufer is not null ? $" · {z.Verkaeufer}" : "")}" +
            $"{(z.Inhalt is not null ? $" · {z.Inhalt}" : "")}" +
            $"{(angebot && z.AufgabeOffen ? $" · Nachfass-Aufgabe bei {z.AufgabeBei ?? "niemandem"}" : "")}";

        static string ProjektZeile(VerkaufZeile z) =>
            $"- {z.Nummer} · {z.Kunde} · " + string.Join("; ", (z.Projekte ?? []).Where(p => !p.Abgeschlossen).Select(p =>
                $"Projekt „{p.Name}“{(p.Vermutet ? " (vermutlich, über den Kunden zugeordnet)" : "")}" +
                $"{(p.Aufgaben > 0 && p.AufgabenErledigt is { } erledigt ? $" · {erledigt} von {p.Aufgaben} Aufgaben erledigt" : "")}" +
                $"{(p.TageOhneBewegung is { } tage ? $" · letzte Bewegung {(tage == 0 ? "heute" : $"vor {tage} Tagen")}" : "")}"));

        var text = $"""
            Offene Angebote und Aufträge ({heute:dd.MM.yyyy})

            Angebote ohne Rückmeldung des Kunden: {u.Angebote.Count}
            {(u.Angebote.Count == 0 ? "- keine" : string.Join("\n", u.Angebote.Select(z => Zeile(z, true))))}

            Bestätigte Aufträge, die niemand gestartet hat (weder Vorgang noch Projekt in Odoo): {u.Auftraege.Count}
            {(u.Auftraege.Count == 0 ? "- keine" : string.Join("\n", u.Auftraege.Select(z => Zeile(z, false))))}
            {(u.ProjektHinweis is not null ? $"Hinweis: {u.ProjektHinweis}\n" : "")}
            Aufträge, die als Projekt in Odoo laufen: {u.InOdoo.Count}
            {(u.InOdoo.Count == 0 ? "- keine" : string.Join("\n", u.InOdoo.Select(ProjektZeile)))}

            Von Hand ausgeblendet: {u.Ausgeblendet.Count}. Berücksichtigt sind die letzten {u.RueckblickTage} Tage.
            Einzelheiten in der Kundenzentrale unter „Angebote“.
            """;
        try
        {
            await mail.SendenAsync(empfaenger,
                $"Offene Angebote: {u.Angebote.Count} ohne Rückmeldung, {u.Auftraege.Count} Aufträge nicht gestartet", text, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Wochenübersicht offene Angebote konnte nicht gesendet werden");
            return "Versand fehlgeschlagen — Einzelheiten stehen im Server-Log.";
        }
        if (letzter is null)
            db.Einstellungen.Add(letzter = new Einstellung
            {
                Schluessel = EinstellungsSchluessel.VerkaufLetzterBericht,
                Beschreibung = "Datum der letzten Wochenübersicht offener Angebote (automatisch).",
            });
        letzter.Wert = heute.ToString("yyyy-MM-dd");
        await db.SaveChangesAsync(ct);
        return null;
    }

    // --- Odoo --------------------------------------------------------------------------

    private async Task<(IOdooAufruf? Odoo, List<OdooVerkauf>? Verkaeufe, string? Hinweis)> LadenAsync(Einstellungen e, DateOnly heute, CancellationToken ct)
    {
        try
        {
            var (odoo, _) = await OdooImportDienst.VerbindenAsync(scopeFactory, konfiguration);
            return (odoo, await OffeneVerkaeufe.LadenAsync(odoo, heute.AddDays(-e.RueckblickTage)).WaitAsync(Wartezeit, ct), null);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Odoo-Zugangsdaten unvollständig"))
        {
            return (null, null, "Odoo ist nicht eingerichtet (Administration → Odoo-Anbindung).");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TimeoutException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            // Details (Host, Antwort) nur ins Log — der Hinweis geht an jeden Mitarbeiter (SICHERHEIT.md N5)
            log.LogWarning(ex, "Offene Angebote: Odoo nicht erreichbar");
            return (null, null, ex is TimeoutException or TaskCanceledException
                ? "Odoo antwortet nicht (Zeitüberschreitung)."
                : "Odoo ist gerade nicht erreichbar. Einzelheiten stehen im Server-Log.");
        }
    }

    /// <summary>
    /// Odoo-Projekte zu den Aufträgen. Scheitert das Lesen (Projektmodul fehlt, der Odoo-Benutzer
    /// der Anbindung darf Projekte nicht lesen), bleibt die Übersicht nutzbar: Dann gilt jeder
    /// Auftrag ohne Vorgang als nicht gestartet, und ein Hinweis sagt, warum.
    /// </summary>
    private async Task<(Dictionary<int, List<OdooProjekt>> Projekte, string? Hinweis)> ProjekteAsync(
        IOdooAufruf odoo, List<OdooVerkauf> auftraege, CancellationToken ct)
    {
        try
        {
            return (await AuftragsProjekte.LadenAsync(odoo, auftraege).WaitAsync(Wartezeit, ct), null);
        }
        catch (OdooFehlerException ex)
        {
            // Nur die Art — Odoos Antworttext gehört weder ins Log noch in die Oberfläche
            log.LogWarning("Offene Aufträge: Odoo-Projekte nicht lesbar ({Art})", ex.Art);
            return ([], ex.Art?.EndsWith("AccessError", StringComparison.Ordinal) == true
                ? "Der Odoo-Benutzer der Anbindung darf Projekte nicht lesen — unten stehen deshalb alle Aufträge ohne Vorgang, auch solche mit Projekt in Odoo."
                : "Die Projekte in Odoo konnten nicht gelesen werden — unten stehen deshalb alle Aufträge ohne Vorgang, auch solche mit Projekt in Odoo.");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TimeoutException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "Offene Aufträge: Odoo-Projekte nicht lesbar");
            return ([], "Die Projekte in Odoo konnten gerade nicht gelesen werden — unten stehen deshalb alle Aufträge ohne Vorgang, auch solche mit Projekt in Odoo.");
        }
    }

    private sealed record KundeKurz(int Id, string Firmenname, string Kundennummer);

    private async Task<Dictionary<int, KundeKurz>> KundenAsync(List<OdooVerkauf> verkaeufe, CancellationToken ct)
    {
        var firmen = verkaeufe.Select(v => v.FirmaId).OfType<int>().Distinct().ToList();
        return (await db.Kunden.AsNoTracking()
                .Where(k => k.OdooId != null && firmen.Contains(k.OdooId.Value))
                .Select(k => new { OdooId = k.OdooId!.Value, k.Id, k.Firmenname, k.Kundennummer })
                .ToListAsync(ct))
            .GroupBy(k => k.OdooId)
            .ToDictionary(g => g.Key, g => new KundeKurz(g.First().Id, g.First().Firmenname, g.First().Kundennummer));
    }
}

/// <summary>Stündlicher Takt: Nachfass-Aufgaben prüfen, Wochenübersicht senden. Fehler brechen den Takt nie ab.</summary>
public class VerkaufsHintergrund(IServiceScopeFactory scopeFactory, ILogger<VerkaufsHintergrund> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dienst = scope.ServiceProvider.GetRequiredService<VerkaufsUeberwachung>();
                await dienst.NachfassenAsync(stoppingToken);
                if (await dienst.WochenberichtAsync(erzwingen: false, stoppingToken) is null)
                    log.LogInformation("Wochenübersicht offene Angebote gesendet");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Takt offene Angebote fehlgeschlagen");
            }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
