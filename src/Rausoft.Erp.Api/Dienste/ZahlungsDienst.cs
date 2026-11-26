using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Domain.Zahlungen;
using Rausoft.Erp.Dtos;
using Rausoft.Erp.OdooImport;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>Autorisierung „Buchhaltung": Zahlungseingänge und offene Posten — Rolle Buchhaltung oder Admin.</summary>
public class BuchhaltungAnforderung : IAuthorizationRequirement
{
    public const string Policy = "Buchhaltung";
}

public class BuchhaltungAnforderungHandler(AngemeldeterMitarbeiter angemeldet) : AuthorizationHandler<BuchhaltungAnforderung>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, BuchhaltungAnforderung anforderung)
    {
        if (await angemeldet.HatRolleAsync(MitarbeiterRolle.Buchhaltung | MitarbeiterRolle.Admin))
            context.Succeed(anforderung);
    }
}

/// <summary>
/// Kontoauszüge (ANFORDERUNGEN.md, Entscheidung MP 26.11.2026): Die Datei wird in den
/// Eingangsordner kopiert; die Kundenzentrale liest die Zahlungseingänge, ordnet sie den
/// offenen Odoo-Rechnungen zu und registriert eindeutige Fälle als Zahlung in Odoo. Alles
/// andere liegt in der Klärungsliste. Regeln:
/// - Gespeichert werden nur gebuchte Eingänge; Abgänge werden gezählt und verworfen.
/// - Dieselbe Buchung entsteht nie doppelt (Fingerabdruck), auch wenn Dateien sich überlappen.
/// - Geschrieben wird ausschließlich über <see cref="OdooSchreibZugang"/> (Ziel Aus/Test/Produktiv).
/// - Vor dem Aufruf in Odoo wird der Eingang als „Buchung unklar" gespeichert: Bricht der
///   Aufruf ab, wird nie von selbst ein zweites Mal gebucht.
/// </summary>
public class ZahlungsDienst(
    ErpDbContext db, IServiceScopeFactory scopeFactory, IConfiguration konfiguration, OdooSchreibZugang schreibZugang,
    MailDienst mail, ILogger<ZahlungsDienst> log)
{
    private const int MaxDateienJeLauf = 50;
    private static readonly TimeSpan LeseWartezeit = TimeSpan.FromSeconds(30);
    private static readonly CultureInfo Deutsch = CultureInfo.GetCultureInfo("de-DE");

    // Takt und Knopf dürfen nicht gleichzeitig einlesen oder buchen
    private static readonly SemaphoreSlim Sperre = new(1, 1);

    private static DateOnly Heute => DateOnly.FromDateTime(DateTime.Now);

    public record Einstellungen(
        string Verzeichnis, bool AutomatischBuchen, string Journal, string Empfaenger, string BerichtTag,
        string ErlaubteDomaenen, string? LetzterLauf);

    public async Task<Einstellungen> EinstellungenAsync(CancellationToken ct)
    {
        var w = await db.Einstellungen.AsNoTracking()
            .Where(e => e.Schluessel.StartsWith("Zahlungen.") || e.Schluessel == EinstellungsSchluessel.ZertifikateEmpfaengerDomaenen
                        || e.Schluessel == EinstellungsSchluessel.MailAbsender)
            .ToDictionaryAsync(e => e.Schluessel, e => e.Wert, ct);
        return new Einstellungen(
            w.GetValueOrDefault(EinstellungsSchluessel.ZahlungenEingangVerzeichnis, "").Trim(),
            !(w.GetValueOrDefault(EinstellungsSchluessel.ZahlungenAutomatischBuchen, "Ja").Trim().ToLowerInvariant() is "nein" or "false" or "0" or "aus"),
            w.GetValueOrDefault(EinstellungsSchluessel.ZahlungenOdooJournal, "").Trim(),
            w.GetValueOrDefault(EinstellungsSchluessel.ZahlungenEmpfaenger, ""),
            w.GetValueOrDefault(EinstellungsSchluessel.ZahlungenBerichtTag, ""),
            BerichtsRegeln.DomaenenText(w.GetValueOrDefault(EinstellungsSchluessel.ZertifikateEmpfaengerDomaenen), w.GetValueOrDefault(EinstellungsSchluessel.MailAbsender)),
            w.GetValueOrDefault(EinstellungsSchluessel.ZahlungenLetzterLauf) is { Length: > 0 } lauf ? lauf : null);
    }

    /// <summary>Kontoauszüge, die im Eingangsordner auf das Einlesen warten.</summary>
    public static List<FileInfo> Wartend(string verzeichnis)
    {
        try
        {
            return verzeichnis.Length == 0 || !Directory.Exists(verzeichnis)
                ? []
                : new DirectoryInfo(verzeichnis).EnumerateFiles("*.csv", SearchOption.TopDirectoryOnly)
                    .OrderBy(f => f.LastWriteTimeUtc).Take(MaxDateienJeLauf).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // --- Übersicht -------------------------------------------------------------------

    public async Task<ZahlungenUebersicht> UebersichtAsync(CancellationToken ct)
    {
        var e = await EinstellungenAsync(ct);
        var stand = await schreibZugang.StandAsync(ct);
        var grenze = Heute.AddDays(-60);
        var alle = await db.Bankumsaetze.AsNoTracking()
            .Where(u => u.Status == UmsatzStatus.Klaerung || u.Status == UmsatzStatus.Zugeordnet
                        || u.Status == UmsatzStatus.BuchungUnklar || u.Buchungstag >= grenze)
            .OrderByDescending(u => u.Buchungstag).ThenByDescending(u => u.Id)
            .Take(1000)
            .ToListAsync(ct);
        List<UmsatzEintrag> Liste(params UmsatzStatus[] status) => alle.Where(u => status.Contains(u.Status)).Select(Eintrag).ToList();
        return new ZahlungenUebersicht(
            Liste(UmsatzStatus.Klaerung, UmsatzStatus.BuchungUnklar), Liste(UmsatzStatus.Zugeordnet),
            Liste(UmsatzStatus.Gebucht), Liste(UmsatzStatus.Ignoriert),
            new ZahlungenStand(e.Verzeichnis.Length > 0, Wartend(e.Verzeichnis).Count, e.LetzterLauf,
                stand.Ziel.ToString(), schreibZugang.SchreibSperre(stand), e.AutomatischBuchen));
    }

    private static UmsatzEintrag Eintrag(Bankumsatz u) => new(
        u.Id, u.Buchungstag, u.Zahler, u.Verwendungszweck, u.Betrag, u.Waehrung,
        u.Status switch
        {
            UmsatzStatus.Zugeordnet => "zugeordnet", UmsatzStatus.Gebucht => "gebucht", UmsatzStatus.Ignoriert => "ignoriert",
            UmsatzStatus.BuchungUnklar => "unklar", _ => "klaerung",
        },
        u.Hinweis, u.Rechnung, u.RechnungKunde, u.KundeId, u.Automatisch, u.GebuchtAm, u.GebuchtZiel?.ToString(), u.Datei);

    public async Task<OffenePostenListe> OffenePostenAsync(CancellationToken ct)
    {
        var (rechnungen, hinweis) = await RechnungenAsync(ct);
        if (rechnungen is null)
            return new OffenePostenListe([], 0, 0, hinweis);
        var kunden = await KundenAsync(rechnungen, ct);
        var heute = Heute;
        var posten = rechnungen
            .Select(r => new OffenerPosten(r.Nummer, r.Kunde, r.FirmaId is { } f && kunden.TryGetValue(f, out var k) ? k : null,
                r.Datum, r.Faellig, r.Faellig is { } d && d < heute ? heute.DayNumber - d.DayNumber : null,
                r.Betrag, r.Offen, r.Waehrung))
            .OrderByDescending(p => p.TageUeberfaellig ?? -1).ThenBy(p => p.Faellig ?? DateOnly.MaxValue)
            .ToList();
        return new OffenePostenListe(posten, posten.Sum(p => p.Offen),
            posten.Where(p => p.TageUeberfaellig is not null).Sum(p => p.Offen),
            rechnungen.Count >= OffenePosten.MaxRechnungen ? "Die Liste ist abgeschnitten — Odoo hat mehr offene Rechnungen." : null);
    }

    // --- Einlesen ----------------------------------------------------------------------

    /// <summary>Liest alle wartenden Dateien, ordnet zu und bucht. Auch ohne neue Datei: holt liegengebliebene Buchungen nach.</summary>
    public async Task<ZahlungsLauf> EinlesenAsync(CancellationToken ct)
    {
        if (!await Sperre.WaitAsync(TimeSpan.Zero, ct))
            return new ZahlungsLauf(0, 0, 0, 0, 0, 0, 0, ["Ein Lauf ist gerade aktiv — bitte gleich noch einmal aktualisieren."]);
        try
        {
            var e = await EinstellungenAsync(ct);
            var meldungen = new List<string>();
            int dateien = 0, neu = 0, doppelt = 0, abgaenge = 0, vorgemerkt = 0;

            if (e.Verzeichnis.Length == 0)
                meldungen.Add("Der Eingangsordner ist nicht eingerichtet (Administration → Kontoauszüge).");
            else if (!Directory.Exists(e.Verzeichnis))
                meldungen.Add("Der Eingangsordner ist nicht erreichbar — Pfad und Rechte des Dienstkontos prüfen.");
            else
                foreach (var datei in Wartend(e.Verzeichnis))
                {
                    ct.ThrowIfCancellationRequested();
                    // Wird die Datei gerade noch kopiert, kommt sie beim nächsten Lauf dran
                    if (datei.LastWriteTimeUtc > DateTime.UtcNow.AddSeconds(-10))
                        continue;
                    var ergebnis = await DateiEinlesenAsync(datei, ct);
                    if (ergebnis is null)
                        continue;
                    dateien++;
                    if (ergebnis.Value.Inhalt.Fehler is { } fehler)
                    {
                        meldungen.Add($"{datei.Name}: {fehler} Die Datei liegt jetzt im Unterordner „fehler“.");
                        Verschieben(datei, "fehler", meldungen);
                        continue;
                    }
                    neu += ergebnis.Value.Neu;
                    doppelt += ergebnis.Value.Inhalt.Eingaenge.Count - ergebnis.Value.Neu;
                    abgaenge += ergebnis.Value.Inhalt.Abgaenge;
                    vorgemerkt += ergebnis.Value.Inhalt.Vorgemerkt;
                    meldungen.AddRange(ergebnis.Value.Inhalt.Warnungen.Take(5).Select(w => $"{datei.Name}: {w}"));
                    Verschieben(datei, "verarbeitet", meldungen);
                }

            var (gebucht, klaerung) = await ZuordnenUndBuchenAsync(e, meldungen, ct);
            var lauf = new ZahlungsLauf(dateien, neu, doppelt, abgaenge, vorgemerkt, gebucht, klaerung, meldungen);
            if (dateien > 0 || gebucht > 0)
            {
                await LetztenLaufSetzenAsync(
                    $"{DateTime.Now:dd.MM.yyyy HH:mm} — {dateien} Datei(en), {neu} neue Eingänge, {gebucht} in Odoo gebucht, {klaerung} zur Klärung", ct);
                log.LogInformation("Kontoauszüge: {Dateien} Datei(en), {Neu} neue Eingänge, {Gebucht} gebucht, {Klaerung} zur Klärung",
                    dateien, neu, gebucht, klaerung);
            }
            if (neu > 0)
                await LaufMeldenAsync(e, lauf, ct);
            return lauf;
        }
        finally
        {
            Sperre.Release();
        }
    }

    private async Task<(KontoauszugInhalt Inhalt, int Neu)?> DateiEinlesenAsync(FileInfo datei, CancellationToken ct)
    {
        byte[] bytes;
        try
        {
            if (datei.Length > KontoauszugLeser.MaxBytes)
                return (new KontoauszugInhalt([], 0, 0, [], $"Die Datei ist größer als {KontoauszugLeser.MaxBytes / 1024 / 1024} MB."), 0);
            // Exklusiv öffnen: Schreibt noch jemand, scheitert das — dann beim nächsten Lauf
            await using var strom = new FileStream(datei.FullName, FileMode.Open, FileAccess.Read, FileShare.None);
            bytes = new byte[strom.Length];
            await strom.ReadExactlyAsync(bytes, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning("Kontoauszug {Datei} ist noch nicht lesbar ({Art}) — nächster Lauf", datei.Name, ex.GetType().Name);
            return null;
        }

        var inhalt = KontoauszugLeser.Lesen(KontoauszugLeser.Text(bytes));
        if (inhalt.Fehler is not null || inhalt.Eingaenge.Count == 0)
            return (inhalt, 0);

        var abdruecke = inhalt.Eingaenge.Select(z => z.Fingerabdruck).ToList();
        var bekannt = (await db.Bankumsaetze.AsNoTracking().Where(u => abdruecke.Contains(u.Fingerabdruck))
            .Select(u => u.Fingerabdruck).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var neue = inhalt.Eingaenge.Where(z => bekannt.Add(z.Fingerabdruck)).ToList();
        foreach (var z in neue)
            db.Bankumsaetze.Add(new Bankumsatz
            {
                Fingerabdruck = z.Fingerabdruck, Auftragskonto = z.Auftragskonto, Buchungstag = z.Buchungstag, Valuta = z.Valuta,
                Buchungstext = z.Buchungstext, Verwendungszweck = z.Verwendungszweck, Zahler = z.Zahler, Betrag = z.Betrag,
                Waehrung = z.Waehrung, Datei = VorgangsHilfen.Kuerzen(datei.Name, 260), ImportiertAm = DateTime.UtcNow,
                Status = UmsatzStatus.Klaerung,
            });
        await db.SaveChangesAsync(ct);
        return (inhalt, neue.Count);
    }

    private void Verschieben(FileInfo datei, string unterordner, List<string> meldungen)
    {
        try
        {
            var ziel = Directory.CreateDirectory(Path.Combine(datei.DirectoryName!, unterordner)).FullName;
            var name = $"{DateTime.Now:yyyyMMdd-HHmmss}_{datei.Name}";
            for (var i = 2; File.Exists(Path.Combine(ziel, name)); i++)
                name = $"{DateTime.Now:yyyyMMdd-HHmmss}-{i}_{datei.Name}";
            datei.MoveTo(Path.Combine(ziel, name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Die Eingänge sind gespeichert; bleibt die Datei liegen, erkennt der nächste Lauf sie als doppelt
            log.LogWarning(ex, "Kontoauszug {Datei} konnte nicht nach {Ordner} verschoben werden", datei.Name, unterordner);
            meldungen.Add($"{datei.Name}: eingelesen, ließ sich aber nicht in den Unterordner „{unterordner}“ verschieben (Rechte des Dienstkontos prüfen).");
        }
    }

    // --- Zuordnen und Buchen -------------------------------------------------------------

    private async Task<(int Gebucht, int Klaerung)> ZuordnenUndBuchenAsync(Einstellungen e, List<string> meldungen, CancellationToken ct)
    {
        var offen = await db.Bankumsaetze
            .Where(u => u.Status == UmsatzStatus.Klaerung || u.Status == UmsatzStatus.Zugeordnet || u.Status == UmsatzStatus.BuchungUnklar)
            .OrderBy(u => u.Buchungstag).ThenBy(u => u.Id)
            .ToListAsync(ct);
        var klaerung = offen.Where(u => u.Status == UmsatzStatus.Klaerung).ToList();
        if (klaerung.Count > 0)
        {
            var (rechnungen, hinweis) = await RechnungenAsync(ct);
            if (rechnungen is null)
                meldungen.Add($"{hinweis} Die Eingänge bleiben in der Klärung und werden beim nächsten Lauf zugeordnet.");
            else
            {
                var kunden = await KundenAsync(rechnungen, ct);
                // Eine Rechnung wird in einem Lauf nur einmal vergeben — zahlt der Kunde doppelt, geht der zweite Eingang in die Klärung
                var vergeben = offen.Where(u => u.Status != UmsatzStatus.Klaerung && u.Rechnung is not null)
                    .Select(u => u.Rechnung!).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var u in klaerung)
                {
                    var frei = rechnungen.Where(r => !vergeben.Contains(r.Nummer)).ToList();
                    var z = ZahlungsZuordnung.Pruefen(u.Verwendungszweck, u.Zahler, u.Betrag, u.Waehrung, frei);
                    var vorschlag = z.Rechnungen.Count == 1 ? z.Rechnungen[0] : null;
                    u.Hinweis = VorgangsHilfen.Kuerzen(z.Grund, 500);
                    u.Rechnung = vorschlag?.Nummer;
                    u.RechnungKunde = vorschlag?.Kunde is { } name ? VorgangsHilfen.Kuerzen(name, 200) : null;
                    u.KundeId = vorschlag?.FirmaId is { } f && kunden.TryGetValue(f, out var k) ? k : null;
                    if (z.Art != ZuordnungsArt.Eindeutig)
                        continue;
                    u.Status = UmsatzStatus.Zugeordnet;
                    u.Automatisch = true;
                    u.ZugeordnetAm = DateTime.UtcNow;
                    vergeben.Add(vorschlag!.Nummer);
                }
                await db.SaveChangesAsync(ct);
            }
        }

        // Buchen: von Hand Zugeordnetes immer, automatisch Zugeordnetes nur, wenn eingeschaltet
        var zuBuchen = offen.Where(u => u.Status == UmsatzStatus.Zugeordnet && (!u.Automatisch || e.AutomatischBuchen)).ToList();
        var gebucht = 0;
        if (zuBuchen.Count > 0)
        {
            var verbindung = await SchreibVerbindungAsync(meldungen, zuBuchen.Count, ct);
            if (verbindung is not null)
                foreach (var u in zuBuchen)
                    if (await BuchenAsync(u, verbindung, e.Journal, ct))
                        gebucht++;
        }
        return (gebucht, offen.Count(u => u.Status is UmsatzStatus.Klaerung or UmsatzStatus.BuchungUnklar));
    }

    private async Task<OdooSchreibZugang.SchreibVerbindung?> SchreibVerbindungAsync(List<string> meldungen, int anzahl, CancellationToken ct)
    {
        try
        {
            return await schreibZugang.SchreibClientAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            // Klartext aus OdooSchreibRegeln (ausgeschaltet, Testdatenbank abgelaufen …)
            meldungen.Add($"{anzahl} zugeordnete(r) Eingang/Eingänge noch nicht in Odoo gebucht: {ex.Message}");
            return null;
        }
    }

    /// <returns>true, wenn die Zahlung in Odoo steht.</returns>
    private async Task<bool> BuchenAsync(Bankumsatz u, OdooSchreibZugang.SchreibVerbindung verbindung, string journal, CancellationToken ct)
    {
        // Erst festhalten, dann schreiben: Stürzt der Prozess mitten im Aufruf ab, bucht kein späterer Lauf noch einmal
        u.Status = UmsatzStatus.BuchungUnklar;
        u.Hinweis = $"Buchung an Rechnung {u.Rechnung} in Odoo begonnen — Ergebnis steht noch aus.";
        await db.SaveChangesAsync(ct);

        BuchungsErgebnis ergebnis;
        try
        {
            ergebnis = await ZahlungsBucher.BuchenAsync(verbindung.Odoo, u.Rechnung!, u.Betrag, u.Valuta ?? u.Buchungstag, journal,
                $"{u.Zahler}: {u.Verwendungszweck}".Trim(' ', ':'));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or InvalidOperationException)
        {
            // Fehler vor dem eigentlichen Buchen (Anmeldung, Suchen der Rechnung): In Odoo ist nichts entstanden
            log.LogWarning("Zahlung zu Rechnung {Rechnung}: Odoo nicht erreichbar ({Art})", u.Rechnung,
                ex is OdooFehlerException o ? o.Art : ex.GetType().Name);
            u.Status = UmsatzStatus.Zugeordnet;
            u.Hinweis = "Odoo war nicht erreichbar oder hat die Anfrage abgelehnt — es wurde nichts gebucht, der nächste Lauf versucht es erneut.";
            await db.SaveChangesAsync(CancellationToken.None);
            return false;
        }

        u.Hinweis = VorgangsHilfen.Kuerzen(ergebnis.Meldung, 500);
        if (ergebnis.Gebucht)
        {
            u.Status = UmsatzStatus.Gebucht;
            u.GebuchtAm = DateTime.UtcNow;
            u.GebuchtZiel = verbindung.Ziel;
            u.GebuchtIn = VorgangsHilfen.Kuerzen(verbindung.Kennung, 300);
            u.OdooZahlungId = ergebnis.ZahlungId;
        }
        else if (!ergebnis.Unklar)
        {
            // Odoo hat vor dem Schreiben abgelehnt (schon bezahlt, anderer Betrag …) — ein Mensch entscheidet
            u.Status = UmsatzStatus.Klaerung;
            u.Automatisch = false;
        }
        // Das Ergebnis muss gespeichert werden, auch wenn der Aufrufer inzwischen abgebrochen hat
        await db.SaveChangesAsync(CancellationToken.None);
        if (ergebnis.Gebucht)
            log.LogInformation("Zahlung zu Rechnung {Rechnung} in Odoo registriert (Ziel {Ziel})", u.Rechnung, verbindung.Ziel);
        return ergebnis.Gebucht;
    }

    // --- Handgriffe aus der Klärungsliste --------------------------------------------------

    /// <summary>Eingang von Hand einer offenen Rechnung zuordnen und, wenn das Schreiben eingeschaltet ist, sofort buchen.</summary>
    /// <returns>Fehlertext oder null; <paramref name="meldung"/> beschreibt das Ergebnis.</returns>
    public async Task<(bool Ok, string Meldung)> ZuordnenAsync(int id, string rechnung, int? mitarbeiterId, CancellationToken ct)
    {
        if (!await Sperre.WaitAsync(TimeSpan.FromSeconds(5), ct))
            return (false, "Ein Lauf ist gerade aktiv — bitte gleich noch einmal versuchen.");
        try
        {
            var u = await db.Bankumsaetze.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (u is null)
                return (false, "Den Eingang gibt es nicht mehr.");
            if (u.Status is not (UmsatzStatus.Klaerung or UmsatzStatus.Zugeordnet))
                return (false, "Der Eingang ist schon gebucht, ignoriert oder in einer unklaren Buchung — bitte zuerst zurücksetzen.");
            var (rechnungen, hinweis) = await RechnungenAsync(ct);
            if (rechnungen is null)
                return (false, hinweis ?? "Odoo ist nicht erreichbar.");
            var r = rechnungen.FirstOrDefault(x => x.Nummer.Equals(rechnung.Trim(), StringComparison.OrdinalIgnoreCase));
            if (r is null)
                return (false, $"Die Rechnung {rechnung.Trim()} ist in Odoo nicht (mehr) offen.");
            if (r.Offen < u.Betrag)
                return (false, $"An der Rechnung {r.Nummer} sind nur {r.Offen.ToString("N2", Deutsch)} € offen — der Eingang ist höher. Bitte in Odoo von Hand buchen und den Eingang hier ignorieren.");

            var kunden = await KundenAsync([r], ct);
            u.Status = UmsatzStatus.Zugeordnet;
            u.Automatisch = false;
            u.Rechnung = r.Nummer;
            u.RechnungKunde = r.Kunde is { } name ? VorgangsHilfen.Kuerzen(name, 200) : null;
            u.KundeId = r.FirmaId is { } f && kunden.TryGetValue(f, out var k) ? k : null;
            u.ZugeordnetAm = DateTime.UtcNow;
            u.ZugeordnetVonId = mitarbeiterId;
            u.Hinweis = r.Offen == u.Betrag ? "Von Hand zugeordnet." : $"Von Hand zugeordnet — Teilzahlung, an der Rechnung bleiben {(r.Offen - u.Betrag).ToString("N2", Deutsch)} € offen.";
            await db.SaveChangesAsync(ct);

            var meldungen = new List<string>();
            var verbindung = await SchreibVerbindungAsync(meldungen, 1, ct);
            if (verbindung is null)
                return (true, $"Zugeordnet. {meldungen[0]}");
            var e = await EinstellungenAsync(ct);
            await BuchenAsync(u, verbindung, e.Journal, ct);
            return (u.Status == UmsatzStatus.Gebucht, u.Hinweis ?? "");
        }
        finally
        {
            Sperre.Release();
        }
    }

    public async Task<string?> IgnorierenAsync(int id, string? grund, int? mitarbeiterId, CancellationToken ct)
    {
        var u = await db.Bankumsaetze.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null)
            return "Den Eingang gibt es nicht mehr.";
        if (u.Status is not (UmsatzStatus.Klaerung or UmsatzStatus.Zugeordnet))
            return "Nur Eingänge in der Klärung oder noch nicht gebuchte Zuordnungen lassen sich ignorieren.";
        u.Status = UmsatzStatus.Ignoriert;
        u.Automatisch = false;
        u.ZugeordnetVonId = mitarbeiterId;
        u.ZugeordnetAm = DateTime.UtcNow;
        u.Hinweis = VorgangsHilfen.Kuerzen(string.IsNullOrWhiteSpace(grund) ? "Von Hand ignoriert." : $"Ignoriert: {grund.Trim()}", 500);
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>
    /// Zurück in die Klärung: ignorierte Eingänge, unklare Buchungen (nach dem Blick in Odoo) und
    /// Buchungen, die nur in der Odoo-Testdatenbank stehen. Produktiv Gebuchtes bleibt gebucht.
    /// </summary>
    public async Task<string?> ZuruecksetzenAsync(int id, CancellationToken ct)
    {
        var u = await db.Bankumsaetze.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null)
            return "Den Eingang gibt es nicht mehr.";
        var erlaubt = u.Status is UmsatzStatus.Ignoriert or UmsatzStatus.BuchungUnklar or UmsatzStatus.Zugeordnet
                      || (u.Status == UmsatzStatus.Gebucht && u.GebuchtZiel == OdooSchreibZiel.Test);
        if (!erlaubt)
            return "In der Produktiv-Datenbank gebuchte Zahlungen lassen sich hier nicht zurücksetzen — bitte in Odoo stornieren.";
        u.Status = UmsatzStatus.Klaerung;
        u.Automatisch = false;
        u.GebuchtAm = null;
        u.GebuchtZiel = null;
        u.GebuchtIn = null;
        u.OdooZahlungId = null;
        u.Hinweis = "Von Hand zurück in die Klärung gelegt.";
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Unklare Buchung nach dem Blick in Odoo: Die Zahlung steht dort — als gebucht festhalten.</summary>
    public async Task<string?> AlsGebuchtAsync(int id, int? mitarbeiterId, CancellationToken ct)
    {
        var u = await db.Bankumsaetze.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null)
            return "Den Eingang gibt es nicht mehr.";
        if (u.Status is not (UmsatzStatus.BuchungUnklar or UmsatzStatus.Klaerung or UmsatzStatus.Zugeordnet))
            return "Der Eingang ist schon gebucht oder ignoriert.";
        var stand = await schreibZugang.StandAsync(ct);
        u.Status = UmsatzStatus.Gebucht;
        u.GebuchtAm = DateTime.UtcNow;
        u.GebuchtZiel = stand.Ziel == OdooSchreibZiel.Test ? OdooSchreibZiel.Test : OdooSchreibZiel.Produktiv;
        u.ZugeordnetVonId = mitarbeiterId;
        u.Hinweis = "Von Hand als in Odoo gebucht festgehalten.";
        await db.SaveChangesAsync(ct);
        return null;
    }

    // --- Meldungen -----------------------------------------------------------------------

    private async Task LaufMeldenAsync(Einstellungen e, ZahlungsLauf lauf, CancellationToken ct)
    {
        var empfaenger = BerichtsRegeln.Empfaenger(e.Empfaenger, e.ErlaubteDomaenen);
        if (empfaenger.Count == 0)
            return;
        var klaerung = await db.Bankumsaetze.AsNoTracking()
            .Where(u => u.Status == UmsatzStatus.Klaerung || u.Status == UmsatzStatus.BuchungUnklar)
            .OrderBy(u => u.Buchungstag).Take(50).ToListAsync(ct);
        var text = $"""
            Kontoauszug eingelesen ({DateTime.Now:dd.MM.yyyy HH:mm})

            {lauf.NeueEingaenge} neue Zahlungseingänge aus {lauf.Dateien} Datei(en), {lauf.Gebucht} in Odoo gebucht.
            {lauf.Doppelt} schon bekannte Buchungen übersprungen, {lauf.Abgaenge} Abgänge nicht übernommen.

            Zur Klärung: {klaerung.Count}
            {(klaerung.Count == 0 ? "- nichts" : string.Join("\n", klaerung.Select(u =>
                $"- {u.Buchungstag:dd.MM.yyyy} · {u.Betrag.ToString("N2", Deutsch)} {u.Waehrung} · {u.Zahler ?? "(ohne Namen)"} · {u.Hinweis}")))}
            {(lauf.Meldungen.Count == 0 ? "" : "\nHinweise:\n" + string.Join("\n", lauf.Meldungen.Select(m => $"- {m}")))}

            Einzelheiten in der Kundenzentrale unter „Zahlungen“.
            """;
        try
        {
            await mail.SendenAsync(empfaenger,
                $"Kontoauszug: {lauf.NeueEingaenge} Eingänge, {lauf.Gebucht} gebucht, {klaerung.Count} zur Klärung", text, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Meldung zum Kontoauszug konnte nicht gesendet werden");
        }
    }

    /// <summary>Wochenübersicht der ausstehenden Zahlungen; <paramref name="erzwingen"/> sendet sofort.</summary>
    /// <returns>Leer = gesendet; sonst der Grund, warum nichts gesendet wurde.</returns>
    public async Task<string?> WochenberichtAsync(bool erzwingen, CancellationToken ct)
    {
        var e = await EinstellungenAsync(ct);
        var heute = Heute;
        var letzter = await db.Einstellungen.FirstOrDefaultAsync(x => x.Schluessel == EinstellungsSchluessel.ZahlungenLetzterBericht, ct);
        if (!erzwingen && !BerichtsRegeln.Faellig(e.BerichtTag, letzter?.Wert, heute))
            return "Heute nicht fällig.";
        var empfaenger = BerichtsRegeln.Empfaenger(e.Empfaenger, e.ErlaubteDomaenen);
        if (empfaenger.Count == 0)
            return "Kein zulässiger Empfänger eingetragen (Administration → Kontoauszüge).";
        var liste = await OffenePostenAsync(ct);
        if (liste.Hinweis is not null && liste.Posten.Count == 0)
            return liste.Hinweis;

        var ueberfaellig = liste.Posten.Where(p => p.TageUeberfaellig is not null).ToList();
        var klaerung = await db.Bankumsaetze.AsNoTracking()
            .CountAsync(u => u.Status == UmsatzStatus.Klaerung || u.Status == UmsatzStatus.BuchungUnklar, ct);
        var text = $"""
            Ausstehende Zahlungen ({heute:dd.MM.yyyy})

            Überfällig: {ueberfaellig.Count} Rechnungen, zusammen {liste.SummeUeberfaellig.ToString("N2", Deutsch)} €
            {(ueberfaellig.Count == 0 ? "- keine" : string.Join("\n", ueberfaellig.Select(p =>
                $"- {p.Nummer} · {p.Kunde} · fällig {p.Faellig:dd.MM.yyyy} (seit {p.TageUeberfaellig} Tagen) · offen {p.Offen.ToString("N2", Deutsch)} {p.Waehrung}")))}

            Insgesamt offen: {liste.Posten.Count} Rechnungen, {liste.SummeOffen.ToString("N2", Deutsch)} €.
            Zahlungseingänge in der Klärung: {klaerung}.
            Einzelheiten in der Kundenzentrale unter „Zahlungen“.
            """;
        try
        {
            await mail.SendenAsync(empfaenger, $"Ausstehende Zahlungen: {ueberfaellig.Count} überfällig, {klaerung} Eingänge zur Klärung", text, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Wochenübersicht ausstehender Zahlungen konnte nicht gesendet werden");
            return "Versand fehlgeschlagen — Einzelheiten stehen im Server-Log.";
        }
        if (letzter is null)
            db.Einstellungen.Add(letzter = new Einstellung
            {
                Schluessel = EinstellungsSchluessel.ZahlungenLetzterBericht,
                Beschreibung = "Datum der letzten Übersicht ausstehender Zahlungen (automatisch).",
            });
        letzter.Wert = heute.ToString("yyyy-MM-dd");
        await db.SaveChangesAsync(ct);
        return null;
    }

    private async Task LetztenLaufSetzenAsync(string wert, CancellationToken ct)
    {
        var eintrag = await db.Einstellungen.FirstOrDefaultAsync(x => x.Schluessel == EinstellungsSchluessel.ZahlungenLetzterLauf, ct);
        if (eintrag is null)
            db.Einstellungen.Add(eintrag = new Einstellung
            {
                Schluessel = EinstellungsSchluessel.ZahlungenLetzterLauf,
                Beschreibung = "Letzter eingelesener Kontoauszug (automatisch).",
            });
        eintrag.Wert = VorgangsHilfen.Kuerzen(wert, 1000);
        await db.SaveChangesAsync(ct);
    }

    // --- Odoo (lesend, Produktiv-Anbindung) -------------------------------------------------

    private async Task<(List<OffeneRechnung>? Rechnungen, string? Hinweis)> RechnungenAsync(CancellationToken ct)
    {
        try
        {
            var (odoo, _) = await OdooImportDienst.VerbindenAsync(scopeFactory, konfiguration);
            return (await OffenePosten.LadenAsync(odoo).WaitAsync(LeseWartezeit, ct), null);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Odoo-Zugangsdaten unvollständig"))
        {
            return (null, "Odoo ist nicht eingerichtet (Administration → Odoo-Anbindung).");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TimeoutException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            log.LogWarning("Offene Posten: Odoo nicht erreichbar ({Art})", ex is OdooFehlerException o ? o.Art : ex.GetType().Name);
            return (null, ex is TimeoutException or TaskCanceledException
                ? "Odoo antwortet nicht (Zeitüberschreitung)."
                : "Odoo ist gerade nicht erreichbar.");
        }
    }

    private async Task<Dictionary<int, int>> KundenAsync(IReadOnlyCollection<OffeneRechnung> rechnungen, CancellationToken ct)
    {
        var firmen = rechnungen.Select(r => r.FirmaId).OfType<int>().Distinct().ToList();
        return (await db.Kunden.AsNoTracking()
                .Where(k => k.OdooId != null && firmen.Contains(k.OdooId.Value))
                .Select(k => new { OdooId = k.OdooId!.Value, k.Id })
                .ToListAsync(ct))
            .GroupBy(k => k.OdooId).ToDictionary(g => g.Key, g => g.First().Id);
    }
}

/// <summary>
/// Takt der Kontoauszüge: alle zwei Minuten nach neuen Dateien sehen (ein Blick ins Verzeichnis,
/// kein Odoo-Aufruf); stündlich liegengebliebene Buchungen nachholen und die Wochenübersicht senden.
/// </summary>
public class ZahlungsHintergrund(IServiceScopeFactory scopeFactory, ILogger<ZahlungsHintergrund> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        for (var runde = 0; !stoppingToken.IsCancellationRequested; runde++)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dienst = scope.ServiceProvider.GetRequiredService<ZahlungsDienst>();
                var stuendlich = runde % 30 == 0;
                var verzeichnis = (await dienst.EinstellungenAsync(stoppingToken)).Verzeichnis;
                if (stuendlich || ZahlungsDienst.Wartend(verzeichnis).Count > 0)
                    await dienst.EinlesenAsync(stoppingToken);
                if (stuendlich && await dienst.WochenberichtAsync(erzwingen: false, stoppingToken) is null)
                    log.LogInformation("Wochenübersicht ausstehender Zahlungen gesendet");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Takt Kontoauszüge fehlgeschlagen");
            }
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
    }
}
