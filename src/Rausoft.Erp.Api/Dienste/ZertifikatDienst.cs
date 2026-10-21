using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Servers;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// SSL-Zertifikate: Live-Prüfung des ausgelieferten Zertifikats (TLS-Handshake,
/// nur Ablaufdatum und Aussteller werden gelesen) und Erinnerungsversand per
/// E-Mail ab dem Vorlauf — täglich wiederholt (Einstellung
/// Zertifikate.ErinnerungIntervallTage), bis jemand das neue Zertifikat bestätigt.
/// </summary>
public class ZertifikatDienst(ErpDbContext db, MailDienst mail, ILogger<ZertifikatDienst> log)
{
    public async Task<int> ErinnerungIntervallTageAsync(CancellationToken ct)
    {
        var wert = (await db.Einstellungen.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Schluessel == EinstellungsSchluessel.ZertifikateErinnerungIntervallTage, ct))?.Wert;
        return int.TryParse(wert, out var tage) && tage > 0 ? tage : 1;
    }

    public async Task<string?> StandardEmpfaengerAsync(CancellationToken ct) =>
        (await db.Einstellungen.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Schluessel == EinstellungsSchluessel.ZertifikateStandardEmpfaenger, ct))?.Wert;

    // --- Live-Prüfung ------------------------------------------------------------

    /// <summary>Liest Ablaufdatum und Aussteller des Zertifikats, das Hostname:Port tatsächlich ausliefert.</summary>
    public async Task PruefeLiveAsync(Zertifikat z, CancellationToken ct)
    {
        z.LiveGeprueftAm = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(z.Hostname))
        {
            z.LiveFehler = null;
            z.LiveGueltigBis = null;
            z.LiveAussteller = null;
            return;
        }
        try
        {
            using var tcp = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await tcp.ConnectAsync(z.Hostname.Trim(), z.Port, timeout.Token);

            X509Certificate2? gefunden = null;
            // Zertifikat nur lesen, Kette nicht erzwingen — auch ein abgelaufenes Zertifikat soll erkannt werden
            await using var ssl = new SslStream(tcp.GetStream(), false, (_, zert, _, _) =>
            {
                if (zert is not null)
                    gefunden = new X509Certificate2(zert);
                return true;
            });
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = z.Hostname.Trim() }, timeout.Token);

            if (gefunden is null)
                throw new InvalidOperationException("Der Server hat kein Zertifikat geliefert.");
            z.LiveGueltigBis = DateOnly.FromDateTime(gefunden.NotAfter.ToUniversalTime());
            z.LiveAussteller = gefunden.GetNameInfo(X509NameType.SimpleName, forIssuer: true);
            z.LiveFehler = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            z.LiveFehler = ex is OperationCanceledException ? "Zeitüberschreitung (10 s)" : ex.Message;
            log.LogWarning("Live-Prüfung Zertifikat {Name} ({Host}:{Port}) fehlgeschlagen: {Fehler}", z.Bezeichnung, z.Hostname, z.Port, z.LiveFehler);
        }
    }

    public async Task PruefeAlleLiveAsync(CancellationToken ct)
    {
        var liste = await db.Zertifikate.Where(z => z.Aktiv && z.Hostname != null && z.Hostname != "").ToListAsync(ct);
        foreach (var z in liste)
            await PruefeLiveAsync(z, ct);
        await db.SaveChangesAsync(ct);
    }

    // --- Erinnerungen ------------------------------------------------------------

    /// <summary>Sendet fällige Erinnerungen; Ergebnis für Protokoll und manuelle Auslösung.</summary>
    public async Task<ErinnerungsLauf> ErinnerungenAsync(CancellationToken ct)
    {
        var jetzt = DateTime.UtcNow;
        var intervall = await ErinnerungIntervallTageAsync(ct);
        var standard = await StandardEmpfaengerAsync(ct);
        var liste = await db.Zertifikate.Where(z => z.Aktiv).OrderBy(z => z.GueltigBis).ToListAsync(ct);
        var fehler = new List<string>();
        var gesendet = 0;

        foreach (var z in liste)
        {
            if (!ZertifikatErinnerung.ErinnerungFaellig(z.GueltigBis, z.VorlaufTage, z.LetzteErinnerungAm, intervall, jetzt))
                continue;
            var empfaenger = ZertifikatErinnerung.Empfaenger(string.IsNullOrWhiteSpace(z.Empfaenger) ? standard : z.Empfaenger);
            if (empfaenger.Count == 0)
            {
                z.LetzterMailFehler = "Keine Empfänger hinterlegt (weder am Zertifikat noch als Standardempfänger).";
                fehler.Add($"{z.Bezeichnung}: {z.LetzterMailFehler}");
                continue;
            }
            try
            {
                var heute = DateOnly.FromDateTime(jetzt);
                var tage = ZertifikatErinnerung.TageBisAblauf(z.GueltigBis, heute);
                await mail.SendenAsync(empfaenger, Betreff(z, tage), Text(z, tage, intervall), ct);
                z.LetzteErinnerungAm = jetzt;
                z.ErinnerungenGesendet++;
                z.LetzterMailFehler = null;
                gesendet++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                z.LetzterMailFehler = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                fehler.Add($"{z.Bezeichnung}: {ex.Message}");
                log.LogError(ex, "Zertifikats-Erinnerung für {Name} konnte nicht gesendet werden", z.Bezeichnung);
            }
        }
        await db.SaveChangesAsync(ct);
        return new ErinnerungsLauf(liste.Count, gesendet, fehler);
    }

    private static string Betreff(Zertifikat z, int tage) => tage switch
    {
        < 0 => $"ABGELAUFEN: SSL-Zertifikat {z.Bezeichnung} seit {-tage} Tag(en)",
        0 => $"HEUTE: SSL-Zertifikat {z.Bezeichnung} läuft ab",
        _ => $"SSL-Zertifikat {z.Bezeichnung} läuft in {tage} Tagen ab ({z.GueltigBis:dd.MM.yyyy})",
    };

    private static string Text(Zertifikat z, int tage, int intervall)
    {
        var lage = tage < 0
            ? $"ist am {z.GueltigBis:dd.MM.yyyy} ABGELAUFEN."
            : tage == 0 ? "läuft HEUTE ab." : $"läuft am {z.GueltigBis:dd.MM.yyyy} ab (in {tage} Tagen).";
        var live = z.LiveGueltigBis is { } l
            ? $"\nLaut Live-Prüfung vom {z.LiveGeprueftAm?.ToLocalTime():dd.MM.yyyy HH:mm} liefert {z.Hostname} ein Zertifikat gültig bis {l:dd.MM.yyyy}" +
              (z.LiveAussteller is not null ? $" (Aussteller: {z.LiveAussteller})." : ".")
            : "";
        return $"""
            Das SSL-Zertifikat „{z.Bezeichnung}"{(z.Hostname is not null ? $" ({z.Hostname})" : "")} {lage}
            Anbieter: {z.Anbieter} · Laufzeit neuer Zertifikate: {z.LaufzeitMonate} Monate{live}

            Bitte das Zertifikat bei {z.Anbieter} verlängern und auf dem Server einspielen.
            Danach in der Kundenzentrale unter Server → SSL-Zertifikate „Neues Zertifikat ist online" bestätigen
            und das neue Ablaufdatum eintragen — erst dann hört diese Erinnerung auf.

            Diese Erinnerung wird alle {intervall} Tag(e) wiederholt, bis die Bestätigung erfolgt ist.
            (Erinnerung Nr. {z.ErinnerungenGesendet + 1}{(z.Notiz is not null ? $" · Notiz: {z.Notiz}" : "")})
            """;
    }
}
