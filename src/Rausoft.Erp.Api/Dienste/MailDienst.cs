using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// E-Mail-Versand über den in den Einstellungen hinterlegten SMTP-Server
/// (Mail.SmtpHost/Port/Benutzer/Ssl/Absender; Passwort verschlüsselt als
/// Geheimnis Mail.SmtpPasswort). Erster Nutzer: Zertifikats-Erinnerungen.
/// Ohne Host gilt der Versand als nicht eingerichtet.
/// </summary>
public class MailDienst(ErpDbContext db, GeheimnisDienst geheimnisse, InstanzOptionen instanz, ILogger<MailDienst> log)
{
    public record Konfiguration(string Host, int Port, string? Benutzer, bool Ssl, string Absender);

    public async Task<Konfiguration?> KonfigurationAsync(CancellationToken ct)
    {
        var werte = await db.Einstellungen.AsNoTracking()
            .Where(e => e.Schluessel.StartsWith("Mail."))
            .ToDictionaryAsync(e => e.Schluessel, e => e.Wert, ct);
        var host = werte.GetValueOrDefault(EinstellungsSchluessel.MailSmtpHost, "").Trim();
        var absender = werte.GetValueOrDefault(EinstellungsSchluessel.MailAbsender, "").Trim();
        if (host.Length == 0 || absender.Length == 0)
            return null;
        _ = int.TryParse(werte.GetValueOrDefault(EinstellungsSchluessel.MailSmtpPort, "587"), out var port);
        var benutzer = werte.GetValueOrDefault(EinstellungsSchluessel.MailSmtpBenutzer, "").Trim();
        var ssl = !string.Equals(werte.GetValueOrDefault(EinstellungsSchluessel.MailSmtpSsl, "true"), "false",
            StringComparison.OrdinalIgnoreCase);
        return new Konfiguration(host, port > 0 ? port : 587, benutzer.Length == 0 ? null : benutzer, ssl, absender);
    }

    public async Task<bool> IstEingerichtetAsync(CancellationToken ct) => await KonfigurationAsync(ct) is not null;

    /// <summary>Sendet eine Text-Mail; wirft bei fehlender Konfiguration oder SMTP-Fehler eine Ausnahme mit lesbarem Text.</summary>
    public async Task SendenAsync(IEnumerable<string> empfaenger, string betreff, string text, CancellationToken ct)
    {
        var konfig = await KonfigurationAsync(ct)
            ?? throw new InvalidOperationException("E-Mail-Versand ist nicht eingerichtet (Administration → E-Mail-Versand: SMTP-Server und Absender).");
        var ziele = empfaenger.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ziele.Count == 0)
            throw new InvalidOperationException("Kein Empfänger angegeben.");

        // Testsystem: nie an echte Empfänger. Mit Umleitungsadresse geht die Mail
        // gekennzeichnet nur dorthin, ohne wird sie verworfen (der Aufrufer sieht
        // „gesendet", damit sich Abläufe wie Erinnerungen durchspielen lassen).
        if (instanz.Testsystem)
        {
            if (string.IsNullOrWhiteSpace(instanz.MailUmleitung))
            {
                log.LogWarning("TESTSYSTEM: E-Mail nicht gesendet: {Betreff} ({Anzahl} Empfänger)", betreff, ziele.Count);
                return;
            }
            text = $"[Testsystem — ursprüngliche Empfänger: {string.Join(", ", ziele)}]\n\n{text}";
            betreff = "[TEST] " + betreff;
            ziele = [instanz.MailUmleitung.Trim()];
        }

        using var client = new SmtpClient(konfig.Host, konfig.Port)
        {
            EnableSsl = konfig.Ssl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 20_000,
        };
        if (konfig.Benutzer is not null)
        {
            var passwort = await geheimnisse.HolenAsync(GeheimnisSchluessel.MailSmtpPasswort, ct)
                ?? throw new InvalidOperationException("SMTP-Passwort ist nicht gesetzt (Administration → E-Mail-Versand).");
            client.Credentials = new NetworkCredential(konfig.Benutzer, passwort);
        }

        using var nachricht = new MailMessage
        {
            From = new MailAddress(konfig.Absender, "Kundenzentrale"),
            Subject = betreff,
            Body = text,
            IsBodyHtml = false,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var ziel in ziele)
            nachricht.To.Add(new MailAddress(ziel));

        // SmtpClient.SendMailAsync kennt kein CancellationToken — Abbruch über Timeout
        await client.SendMailAsync(nachricht, ct);
        log.LogInformation("E-Mail gesendet: {Betreff} an {Anzahl} Empfänger", betreff, ziele.Count);
    }
}
