using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Servers;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// SSL-Zertifikate:
/// - Live-Prüfung per TLS-Handshake (Ablauf, Aussteller, Protokoll, Hostname-/
///   Kettenfehler, veraltetes TLS); erkennt ein neueres Zertifikat → Erneuerung
///   automatisch übernehmen (AutoErkennung) und per Mail melden.
/// - Erinnerung ab dem Vorlauf, wiederholt alle n Tage, mit Eskalationsstufen
///   (zusätzliche Empfänger kurz vor und nach Ablauf) — bis erneuert.
/// - Überwachung der Überwachung: andauernde Prüfstörung melden, Wochenbericht.
/// - Zertifikate aus der Serverliste automatisch anlegen.
/// Alles protokolliert im Verlauf je Zertifikat.
/// </summary>
public class ZertifikatDienst(ErpDbContext db, MailDienst mail, ILogger<ZertifikatDienst> log)
{
    public record Einstellungen(
        int IntervallTage, int EskalationTage, int PruefstoerungTage,
        string? StandardEmpfaenger, string? EskalationEmpfaenger, string? AbgelaufenEmpfaenger,
        string WochenberichtTag, bool ServerAutomatisch);

    public async Task<Einstellungen> EinstellungenAsync(CancellationToken ct)
    {
        var w = await db.Einstellungen.AsNoTracking()
            .Where(e => e.Schluessel.StartsWith("Zertifikate."))
            .ToDictionaryAsync(e => e.Schluessel, e => e.Wert, ct);
        int Zahl(string schluessel, int standard) =>
            int.TryParse(w.GetValueOrDefault(schluessel), out var z) && z > 0 ? z : standard;
        string? Text(string schluessel) =>
            string.IsNullOrWhiteSpace(w.GetValueOrDefault(schluessel)) ? null : w[schluessel].Trim();
        return new Einstellungen(
            Zahl(EinstellungsSchluessel.ZertifikateErinnerungIntervallTage, 1),
            Zahl(EinstellungsSchluessel.ZertifikateEskalationTage, 3),
            Zahl(EinstellungsSchluessel.ZertifikatePruefstoerungTage, 3),
            Text(EinstellungsSchluessel.ZertifikateStandardEmpfaenger),
            Text(EinstellungsSchluessel.ZertifikateEskalationEmpfaenger),
            Text(EinstellungsSchluessel.ZertifikateAbgelaufenEmpfaenger),
            Text(EinstellungsSchluessel.ZertifikateWochenberichtTag) ?? "Montag",
            !string.Equals(w.GetValueOrDefault(EinstellungsSchluessel.ZertifikateServerAutomatisch), "false", StringComparison.OrdinalIgnoreCase));
    }

    // --- Live-Prüfung ------------------------------------------------------------

    /// <summary>
    /// Liest das live ausgelieferte Zertifikat. Übernimmt bei AutoErkennung ein
    /// neueres Zertifikat als Erneuerung (Erinnerungen enden, Meldung per Mail).
    /// </summary>
    public async Task PruefeLiveAsync(Zertifikat z, Einstellungen? einstellungen, CancellationToken ct)
    {
        var jetzt = DateTime.UtcNow;
        z.LiveGeprueftAm = jetzt;
        if (string.IsNullOrWhiteSpace(z.Hostname))
        {
            z.LiveFehler = null;
            z.LiveFehlerSeit = null;
            z.LiveGueltigBis = null;
            z.LiveAussteller = null;
            z.LiveProtokoll = null;
            z.LiveWarnungen = null;
            return;
        }

        var host = z.Hostname.Trim();
        try
        {
            var (zert, fehler, protokoll) = await HandshakeAsync(host, z.Port, SslProtocols.None, ct);
            if (zert is null)
                throw new InvalidOperationException("Der Server hat kein Zertifikat geliefert.");

            var warnungen = new List<string>();
            if (fehler.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
                warnungen.Add($"Zertifikat passt nicht zum Hostnamen (ausgestellt für {zert.GetNameInfo(X509NameType.DnsName, false)})");
            if (fehler.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors) && zert.NotAfter > jetzt)
                warnungen.Add("Zertifikatskette unvollständig oder nicht vertrauenswürdig");
#pragma warning disable SYSLIB0039 // Veraltete Protokolle werden bewusst erkannt, nicht verwendet
            if (protokoll is SslProtocols.Tls or SslProtocols.Tls11)
                warnungen.Add($"Verbindung nur mit veraltetem {ProtokollName(protokoll)}");
#pragma warning restore SYSLIB0039
            // Bietet der Server noch TLS 1.0/1.1 an? (Prüfung schlägt fehl, wenn das Betriebssystem sie nicht mehr kann — dann unbekannt)
#pragma warning disable SYSLIB0039
            foreach (var alt in new[] { SslProtocols.Tls11, SslProtocols.Tls })
#pragma warning restore SYSLIB0039
            {
                try
                {
                    var (altZert, _, altProt) = await HandshakeAsync(host, z.Port, alt, ct);
                    if (altZert is not null && altProt == alt)
                        warnungen.Add($"Server bietet veraltetes {ProtokollName(alt)} an");
                }
                catch (Exception) { /* abgelehnt oder nicht prüfbar — gut bzw. unbekannt */ }
            }

            var neuesLive = DateOnly.FromDateTime(zert.NotAfter.ToUniversalTime());
            z.LiveGueltigBis = neuesLive;
            z.LiveAussteller = zert.GetNameInfo(X509NameType.SimpleName, forIssuer: true);
            z.LiveProtokoll = ProtokollName(protokoll);
            var neueWarnungen = warnungen.Count == 0 ? null : string.Join(" · ", warnungen);
            if (neueWarnungen is not null && neueWarnungen != z.LiveWarnungen)
                z.Protokoll(ZertifikatEreignisArt.Warnung, neueWarnungen, null, jetzt);
            z.LiveWarnungen = neueWarnungen;
            if (z.LiveFehlerSeit is not null)
                z.Protokoll(ZertifikatEreignisArt.Pruefstoerung, "Prüfung wieder erfolgreich", null, jetzt);
            z.LiveFehler = null;
            z.LiveFehlerSeit = null;
            z.LiveFehlerGemeldetAm = null;

            // Erneuerung automatisch erkannt?
            if (z.AutoErkennung && ZertifikatErinnerung.ErneuerungErkannt(z.GueltigBis, neuesLive))
            {
                var alt = z.GueltigBis;
                var erinnert = z.ErinnerungenGesendet;
                z.Erneuern(neuesLive, null, jetzt);
                z.Protokoll(ZertifikatEreignisArt.ErneuertErkannt,
                    $"Neues Zertifikat erkannt: gültig bis {neuesLive:dd.MM.yyyy} (vorher {alt:dd.MM.yyyy}), Aussteller {z.LiveAussteller}", null, jetzt);
                log.LogInformation("Zertifikat {Name}: Erneuerung erkannt, gültig bis {Bis}", z.Bezeichnung, neuesLive);
                einstellungen ??= await EinstellungenAsync(ct);
                await MailVersuchAsync(z, einstellungen.StandardEmpfaenger,
                    $"Erledigt: SSL-Zertifikat {z.Bezeichnung} erneuert (gültig bis {neuesLive:dd.MM.yyyy})",
                    $"""
                    Die Live-Prüfung hat für „{z.Bezeichnung}" ({host}:{z.Port}) ein neues Zertifikat erkannt.

                    Gültig bis: {neuesLive:dd.MM.yyyy} (vorher {alt:dd.MM.yyyy})
                    Aussteller: {z.LiveAussteller}
                    {(erinnert > 0 ? $"Die {erinnert} Erinnerung(en) sind damit beendet." : "Es war keine Erinnerung offen.")}

                    Es ist nichts weiter zu tun. Die nächste Erinnerung beginnt {z.VorlaufTage} Tage vor dem neuen Ablauf.
                    """, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var text = ex is OperationCanceledException ? "Zeitüberschreitung (10 s)" : ex.Message;
            if (z.LiveFehlerSeit is null)
            {
                z.LiveFehlerSeit = jetzt;
                z.Protokoll(ZertifikatEreignisArt.Pruefstoerung, $"Prüfung fehlgeschlagen: {text}", null, jetzt);
            }
            z.LiveFehler = text.Length > 500 ? text[..500] : text;
            log.LogWarning("Live-Prüfung Zertifikat {Name} ({Host}:{Port}) fehlgeschlagen: {Fehler}", z.Bezeichnung, host, z.Port, text);
        }
    }

    private static async Task<(X509Certificate2? Zertifikat, SslPolicyErrors Fehler, SslProtocols Protokoll)> HandshakeAsync(
        string host, int port, SslProtocols protokolle, CancellationToken ct)
    {
        using var tcp = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await tcp.ConnectAsync(host, port, timeout.Token);

        X509Certificate2? gefunden = null;
        var fehler = SslPolicyErrors.None;
        // Zertifikat nur lesen, Validierung nicht erzwingen — auch ein abgelaufenes oder falsches Zertifikat soll erkannt werden
        await using var ssl = new SslStream(tcp.GetStream(), false, (_, zert, _, policyErrors) =>
        {
            if (zert is not null)
                gefunden = new X509Certificate2(zert);
            fehler = policyErrors;
            return true;
        });
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = host,
            EnabledSslProtocols = protokolle,
        }, timeout.Token);
        return (gefunden, fehler, ssl.SslProtocol);
    }

    private static string ProtokollName(SslProtocols p) => p switch
    {
        SslProtocols.Tls13 => "TLS 1.3",
        SslProtocols.Tls12 => "TLS 1.2",
#pragma warning disable SYSLIB0039
        SslProtocols.Tls11 => "TLS 1.1",
        SslProtocols.Tls => "TLS 1.0",
#pragma warning restore SYSLIB0039
        _ => p.ToString(),
    };

    public async Task PruefeAlleLiveAsync(CancellationToken ct)
    {
        var einstellungen = await EinstellungenAsync(ct);
        var liste = await db.Zertifikate.Where(z => z.Aktiv && z.Hostname != null && z.Hostname != "").ToListAsync(ct);
        foreach (var z in liste)
            await PruefeLiveAsync(z, einstellungen, ct);
        await db.SaveChangesAsync(ct);
    }

    // --- Zertifikate aus der Serverliste ------------------------------------------

    /// <summary>Legt für jeden aktiven ID.prove-Server mit noch unbekanntem Hostnamen ein Zertifikat an (Ablauf aus der Live-Prüfung).</summary>
    public async Task<ServerZertifikateErgebnis> ServerZertifikateAnlegenAsync(int? vonId, CancellationToken ct)
    {
        var einstellungen = await EinstellungenAsync(ct);
        var server = await db.Server.AsNoTracking().Where(s => s.Aktiv).OrderBy(s => s.Alias).ToListAsync(ct);
        var vorhanden = (await db.Zertifikate.AsNoTracking().Select(z => new { z.Hostname, z.ServerId }).ToListAsync(ct));
        var hosts = vorhanden.Select(v => v.Hostname).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var serverIds = vorhanden.Select(v => v.ServerId).OfType<int>().ToHashSet();
        var jetzt = DateTime.UtcNow;
        var heute = DateOnly.FromDateTime(jetzt);
        var neu = new List<string>();

        foreach (var s in server)
        {
            var host = ZertifikatErinnerung.HostAusUrl(s.Url);
            if (host is null || hosts.Contains(host) || serverIds.Contains(s.Id))
                continue;
            var z = new Zertifikat
            {
                Bezeichnung = s.Alias,
                Hostname = host,
                Port = Uri.TryCreate(s.Url, UriKind.Absolute, out var uri) && !uri.IsDefaultPort ? uri.Port : 443,
                ServerId = s.Id,
                GueltigBis = heute.AddMonths(6),
                AutomatischAngelegt = true,
            };
            z.Protokoll(ZertifikatEreignisArt.Angelegt, $"Automatisch aus der Serverliste angelegt ({host})", vonId, jetzt);
            db.Zertifikate.Add(z);
            // Live-Prüfung liefert das echte Ablaufdatum; AutoErkennung übernimmt es, wenn es später liegt —
            // liegt es früher (kürzer als 6 Monate), direkt übernehmen, damit die Erinnerung stimmt
            await PruefeLiveAsync(z, einstellungen, ct);
            if (z.LiveGueltigBis is { } live && live < z.GueltigBis)
                z.GueltigBis = live;
            hosts.Add(host);
            neu.Add(host);
        }
        if (neu.Count > 0)
            await db.SaveChangesAsync(ct);
        return new ServerZertifikateErgebnis(neu.Count, neu);
    }

    // --- Erinnerungen mit Eskalation ---------------------------------------------

    public async Task<ErinnerungsLauf> ErinnerungenAsync(CancellationToken ct)
    {
        var jetzt = DateTime.UtcNow;
        var heute = DateOnly.FromDateTime(jetzt);
        var e = await EinstellungenAsync(ct);
        var liste = await db.Zertifikate.Where(z => z.Aktiv).OrderBy(z => z.GueltigBis).ToListAsync(ct);
        var fehler = new List<string>();
        var gesendet = 0;

        foreach (var z in liste)
        {
            if (!ZertifikatErinnerung.ErinnerungFaellig(z.GueltigBis, z.VorlaufTage, z.LetzteErinnerungAm, e.IntervallTage, jetzt))
                continue;
            var stufe = ZertifikatErinnerung.Stufe(z.GueltigBis, z.VorlaufTage, e.EskalationTage, heute);
            var empfaenger = ZertifikatErinnerung.Empfaenger(string.IsNullOrWhiteSpace(z.Empfaenger) ? e.StandardEmpfaenger : z.Empfaenger);
            if (stufe is "eskalation" or "abgelaufen")
                empfaenger = empfaenger.Union(ZertifikatErinnerung.Empfaenger(e.EskalationEmpfaenger), StringComparer.OrdinalIgnoreCase).ToList();
            if (stufe is "abgelaufen")
                empfaenger = empfaenger.Union(ZertifikatErinnerung.Empfaenger(e.AbgelaufenEmpfaenger), StringComparer.OrdinalIgnoreCase).ToList();
            if (empfaenger.Count == 0)
            {
                z.LetzterMailFehler = "Keine Empfänger hinterlegt (weder am Zertifikat noch als Standardempfänger).";
                fehler.Add($"{z.Bezeichnung}: {z.LetzterMailFehler}");
                continue;
            }
            var tage = ZertifikatErinnerung.TageBisAblauf(z.GueltigBis, heute);
            try
            {
                await mail.SendenAsync(empfaenger, Betreff(z, tage, stufe), ErinnerungsText(z, tage, stufe, e.IntervallTage), ct);
                z.LetzteErinnerungAm = jetzt;
                z.ErinnerungenGesendet++;
                z.LetzterMailFehler = null;
                z.Protokoll(stufe switch
                {
                    "abgelaufen" => ZertifikatEreignisArt.Abgelaufen,
                    "eskalation" => ZertifikatEreignisArt.Eskalation,
                    _ => ZertifikatEreignisArt.Erinnerung,
                }, $"{StufenText(stufe)} Nr. {z.ErinnerungenGesendet} an {string.Join(", ", empfaenger)} ({(tage < 0 ? $"seit {-tage} Tagen abgelaufen" : $"noch {tage} Tage")})", null, jetzt);
                gesendet++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                z.LetzterMailFehler = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                z.Protokoll(ZertifikatEreignisArt.Versandfehler, ex.Message, null, jetzt);
                fehler.Add($"{z.Bezeichnung}: {ex.Message}");
                log.LogError(ex, "Zertifikats-Erinnerung für {Name} konnte nicht gesendet werden", z.Bezeichnung);
            }
        }
        await db.SaveChangesAsync(ct);
        return new ErinnerungsLauf(liste.Count, gesendet, fehler);
    }

    private static string StufenText(string stufe) => stufe switch
    {
        "abgelaufen" => "Abgelaufen-Meldung",
        "eskalation" => "Eskalation",
        _ => "Erinnerung",
    };

    private static string Betreff(Zertifikat z, int tage, string stufe) => stufe switch
    {
        "abgelaufen" => $"ABGELAUFEN: SSL-Zertifikat {z.Bezeichnung} seit {-tage} Tag(en)",
        "eskalation" => tage == 0 ? $"DRINGEND: SSL-Zertifikat {z.Bezeichnung} läuft HEUTE ab"
                                  : $"DRINGEND: SSL-Zertifikat {z.Bezeichnung} läuft in {tage} Tagen ab ({z.GueltigBis:dd.MM.yyyy})",
        _ => $"SSL-Zertifikat {z.Bezeichnung} läuft in {tage} Tagen ab ({z.GueltigBis:dd.MM.yyyy})",
    };

    private static string ErinnerungsText(Zertifikat z, int tage, string stufe, int intervall)
    {
        var lage = tage < 0
            ? $"ist am {z.GueltigBis:dd.MM.yyyy} ABGELAUFEN."
            : tage == 0 ? "läuft HEUTE ab." : $"läuft am {z.GueltigBis:dd.MM.yyyy} ab (in {tage} Tagen).";
        var live = z.LiveGueltigBis is { } l
            ? $"\nLaut Live-Prüfung vom {z.LiveGeprueftAm?.ToLocalTime():dd.MM.yyyy HH:mm} liefert {z.Hostname} ein Zertifikat gültig bis {l:dd.MM.yyyy}" +
              (z.LiveAussteller is not null ? $" (Aussteller: {z.LiveAussteller})." : ".")
            : z.LiveFehler is not null ? $"\nAchtung: Die Live-Prüfung von {z.Hostname} schlägt fehl ({z.LiveFehler})." : "";
        var ende = z.AutoErkennung && z.Hostname is not null
            ? "Sobald der Server das neue Zertifikat ausliefert, erkennt die Kundenzentrale das bei der nächsten Prüfung selbst und beendet diese Erinnerung. " +
              "Alternativ unter Server → SSL-Zertifikate „Neues Zertifikat ist online“ bestätigen."
            : "Danach in der Kundenzentrale unter Server → SSL-Zertifikate „Neues Zertifikat ist online“ bestätigen und das neue Ablaufdatum eintragen — erst dann hört diese Erinnerung auf.";
        return $"""
            {(stufe == "eskalation" ? "DRINGEND — " : stufe == "abgelaufen" ? "ABGELAUFEN — " : "")}Das SSL-Zertifikat „{z.Bezeichnung}"{(z.Hostname is not null ? $" ({z.Hostname})" : "")} {lage}
            Anbieter: {z.Anbieter} · Laufzeit neuer Zertifikate: {z.LaufzeitMonate} Monate{live}

            Bitte das Zertifikat bei {z.Anbieter} verlängern und auf dem Server einspielen.
            {ende}

            Diese Erinnerung wird alle {intervall} Tag(e) wiederholt, bis das neue Zertifikat online ist.
            (Erinnerung Nr. {z.ErinnerungenGesendet + 1}{(z.Notiz is not null ? $" · Notiz: {z.Notiz}" : "")})
            """;
    }

    // --- Überwachung der Überwachung ---------------------------------------------

    /// <summary>Meldet Zertifikate, deren Live-Prüfung seit n Tagen scheitert (höchstens einmal pro Woche je Zertifikat).</summary>
    public async Task<int> PruefstoerungenMeldenAsync(CancellationToken ct)
    {
        var jetzt = DateTime.UtcNow;
        var e = await EinstellungenAsync(ct);
        var gestoert = (await db.Zertifikate.Where(z => z.Aktiv && z.LiveFehlerSeit != null).ToListAsync(ct))
            .Where(z => ZertifikatErinnerung.PruefstoerungMelden(z.LiveFehlerSeit, z.LiveFehlerGemeldetAm, e.PruefstoerungTage, jetzt))
            .ToList();
        var gemeldet = 0;
        foreach (var z in gestoert)
        {
            var empfaenger = ZertifikatErinnerung.Empfaenger(string.IsNullOrWhiteSpace(z.Empfaenger) ? e.StandardEmpfaenger : z.Empfaenger);
            if (empfaenger.Count == 0)
                continue;
            var ok = await MailVersuchAsync(z, string.Join(",", empfaenger),
                $"Prüfstörung: SSL-Zertifikat {z.Bezeichnung} seit {(jetzt - z.LiveFehlerSeit!.Value).Days} Tagen nicht prüfbar",
                $"""
                Die Live-Prüfung des Zertifikats „{z.Bezeichnung}" ({z.Hostname}:{z.Port}) schlägt seit dem {z.LiveFehlerSeit:dd.MM.yyyy HH:mm} fehl:
                {z.LiveFehler}

                Solange ist die Überwachung für dieses Zertifikat blind — eine Erneuerung wird nicht automatisch erkannt,
                die Erinnerung läuft nur nach dem eingetragenen Ablaufdatum ({z.GueltigBis:dd.MM.yyyy}).
                Mögliche Ursachen: Firewall/ausgehender Port {z.Port}, DNS, Server nicht erreichbar, Hostname falsch eingetragen.
                """, ct);
            if (ok)
            {
                z.LiveFehlerGemeldetAm = jetzt;
                gemeldet++;
            }
        }
        await db.SaveChangesAsync(ct);
        return gemeldet;
    }

    /// <summary>Wochenbericht am eingestellten Wochentag: Lebenszeichen der Überwachung mit allen Zertifikaten.</summary>
    public async Task<bool> WochenberichtAsync(bool erzwingen, CancellationToken ct)
    {
        var jetzt = DateTime.UtcNow;
        var heute = DateOnly.FromDateTime(jetzt);
        var e = await EinstellungenAsync(ct);
        var letzter = await db.Einstellungen.FirstOrDefaultAsync(x => x.Schluessel == EinstellungsSchluessel.ZertifikateLetzterWochenbericht, ct);
        if (!erzwingen)
        {
            if (!WochentagPasst(e.WochenberichtTag, heute))
                return false;
            if (letzter is not null && DateOnly.TryParse(letzter.Wert, out var d) && heute.DayNumber - d.DayNumber < 6)
                return false;
        }
        var empfaenger = ZertifikatErinnerung.Empfaenger(e.StandardEmpfaenger);
        if (empfaenger.Count == 0)
            return false;

        var liste = await db.Zertifikate.AsNoTracking().Where(z => z.Aktiv).OrderBy(z => z.GueltigBis).ToListAsync(ct);
        var zeilen = liste.Select(z =>
        {
            var tage = ZertifikatErinnerung.TageBisAblauf(z.GueltigBis, heute);
            var lage = tage < 0 ? $"ABGELAUFEN seit {-tage} Tagen" : $"noch {tage} Tage";
            var pruefung = z.Hostname is null ? "ohne Live-Prüfung"
                : z.LiveFehlerSeit is not null ? $"PRÜFUNG GESTÖRT seit {z.LiveFehlerSeit:dd.MM.}"
                : $"live bis {z.LiveGueltigBis:dd.MM.yyyy}, {z.LiveProtokoll}";
            var warn = z.LiveWarnungen is not null ? $" · WARNUNG: {z.LiveWarnungen}" : "";
            return $"- {z.Bezeichnung}: gültig bis {z.GueltigBis:dd.MM.yyyy} ({lage}) · {pruefung}{warn}";
        });
        var naechstes = liste.FirstOrDefault();
        var problem = liste.Count(z => ZertifikatErinnerung.TageBisAblauf(z.GueltigBis, heute) <= z.VorlaufTage || z.LiveFehlerSeit is not null || z.LiveWarnungen is not null);
        var text = $"""
            Wochenbericht SSL-Zertifikate der Kundenzentrale ({heute:dd.MM.yyyy})
            {liste.Count} Zertifikate überwacht, {(problem == 0 ? "alle in Ordnung" : $"{problem} mit Handlungsbedarf")}.
            {(naechstes is not null ? $"Nächster Ablauf: {naechstes.Bezeichnung} am {naechstes.GueltigBis:dd.MM.yyyy}." : "")}

            {string.Join("\n", zeilen)}

            Diese Mail bestätigt, dass die Überwachung läuft. Bleibt sie aus, bitte den Dienst der Kundenzentrale prüfen.
            """;
        try
        {
            await mail.SendenAsync(empfaenger, $"Wochenbericht SSL-Zertifikate: {liste.Count} überwacht, {(problem == 0 ? "alle in Ordnung" : $"{problem} mit Handlungsbedarf")}", text, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Wochenbericht Zertifikate konnte nicht gesendet werden");
            return false;
        }
        if (letzter is null)
            db.Einstellungen.Add(letzter = new Einstellung { Schluessel = EinstellungsSchluessel.ZertifikateLetzterWochenbericht, Beschreibung = "Datum des letzten Wochenberichts (automatisch)." });
        letzter.Wert = heute.ToString("yyyy-MM-dd");
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static bool WochentagPasst(string einstellung, DateOnly heute)
    {
        var tag = einstellung.Trim().ToLowerInvariant();
        if (tag is "" or "aus" or "nie")
            return false;
        var heutiger = heute.DayOfWeek switch
        {
            DayOfWeek.Monday => "montag", DayOfWeek.Tuesday => "dienstag", DayOfWeek.Wednesday => "mittwoch",
            DayOfWeek.Thursday => "donnerstag", DayOfWeek.Friday => "freitag", DayOfWeek.Saturday => "samstag", _ => "sonntag",
        };
        return tag == heutiger || tag == "täglich";
    }

    /// <summary>Mail senden und Fehler am Zertifikat protokollieren statt werfen.</summary>
    private async Task<bool> MailVersuchAsync(Zertifikat z, string? empfaengerText, string betreff, string text, CancellationToken ct)
    {
        var empfaenger = ZertifikatErinnerung.Empfaenger(empfaengerText);
        if (empfaenger.Count == 0)
            return false;
        try
        {
            await mail.SendenAsync(empfaenger, betreff, text, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            z.LetzterMailFehler = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            z.Protokoll(ZertifikatEreignisArt.Versandfehler, $"{betreff}: {ex.Message}", null, DateTime.UtcNow);
            log.LogError(ex, "Mail „{Betreff}“ konnte nicht gesendet werden", betreff);
            return false;
        }
    }

    // --- Hinweise für Abzeichen ----------------------------------------------------

    public async Task<ZertifikatHinweise> HinweiseAsync(CancellationToken ct)
    {
        var heute = DateOnly.FromDateTime(DateTime.UtcNow);
        var liste = await db.Zertifikate.AsNoTracking().Where(z => z.Aktiv)
            .Select(z => new { z.GueltigBis, z.VorlaufTage, z.LiveFehlerSeit, z.LiveWarnungen }).ToListAsync(ct);
        return new ZertifikatHinweise(
            liste.Count(z => ZertifikatErinnerung.Status(z.GueltigBis, z.VorlaufTage, heute) == "bald"),
            liste.Count(z => ZertifikatErinnerung.Status(z.GueltigBis, z.VorlaufTage, heute) == "abgelaufen"),
            liste.Count(z => z.LiveFehlerSeit != null),
            liste.Count(z => z.LiveWarnungen != null));
    }
}
