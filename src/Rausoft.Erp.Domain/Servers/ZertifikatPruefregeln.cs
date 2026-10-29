using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Security.Authentication;

namespace Rausoft.Erp.Domain.Servers;

/// <summary>
/// Schutzregeln der Zertifikatsüberwachung (SICHERHEIT.md, N1): Die Live-Prüfung
/// baut vom Anwendungsserver aus Verbindungen zu frei eingetragenen Zielen auf.
/// Damit sie kein Werkzeug zum Abtasten des Netzes wird, gelten:
/// - Hostname nur als DNS-Name oder IP-Adresse; Loopback, Link-Local, Multicast
///   und unspezifizierte Adressen sind tabu.
/// - Port nur aus der Admin-gepflegten Liste (Standard 443 und 8443).
/// - Empfänger der Statusmails nur aus den erlaubten Domänen (Standard: die
///   Domäne der Absenderadresse), damit Infrastrukturdaten das Haus nicht verlassen.
/// - Fehlertexte der Prüfung sind feste Kategorien ohne IP-Adressen oder Details.
/// </summary>
public static class ZertifikatPruefregeln
{
    public static readonly IReadOnlyList<int> StandardPorts = [443, 8443];

    /// <summary>Erlaubte Ports aus dem Einstellungstext („443, 8443"); ohne gültigen Eintrag die Standardliste.</summary>
    public static List<int> Ports(string? text)
    {
        var ports = (text ?? "")
            .Split([',', ';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => int.TryParse(t, out var p) ? p : 0)
            .Where(p => p is >= 1 and <= 65535)
            .Distinct().Order().ToList();
        return ports.Count > 0 ? ports : [.. StandardPorts];
    }

    /// <summary>Erlaubte Empfänger-Domänen aus dem Einstellungstext; leer = keine Einschränkung durch die Einstellung.</summary>
    public static List<string> Domaenen(string? text) =>
        (text ?? "")
            .Split([',', ';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.TrimStart('@').Trim('.').ToLowerInvariant())
            .Where(d => d.Length > 0 && Uri.CheckHostName(d) == UriHostNameType.Dns)
            .Distinct().ToList();

    /// <summary>Domäne einer Mailadresse (klein geschrieben) oder null, wenn keine gültige Adresse.</summary>
    public static string? Domaene(string? adresse)
    {
        if (string.IsNullOrWhiteSpace(adresse))
            return null;
        try
        {
            var host = new MailAddress(adresse.Trim()).Host;
            return host.Length == 0 ? null : host.ToLowerInvariant();
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Fehlertext, wenn der Hostname nicht geprüft werden darf; null = in Ordnung (auch bei leerem Hostnamen).</summary>
    public static string? HostnameFehler(string? hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname))
            return null;
        var host = hostname.Trim().TrimEnd('.');
        if (host.Length > 253)
            return "Der Hostname ist zu lang.";

        switch (Uri.CheckHostName(host))
        {
            case UriHostNameType.Dns:
                if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
                    || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) && host.StartsWith("localhost.", StringComparison.OrdinalIgnoreCase))
                    return "Der Hostname darf nicht auf den Anwendungsserver selbst zeigen.";
                return null;
            case UriHostNameType.IPv4:
            case UriHostNameType.IPv6:
                if (!IPAddress.TryParse(host.Trim('[', ']'), out var ip))
                    return "Die IP-Adresse ist ungültig.";
                if (ip.IsIPv4MappedToIPv6)
                    ip = ip.MapToIPv4();
                if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)
                    || ip.Equals(IPAddress.Broadcast) || ip.IsIPv6LinkLocal || ip.IsIPv6Multicast
                    || IstIPv4LinkLocalOderMulticast(ip))
                    return "Loopback-, Link-Local-, Multicast- und unspezifizierte Adressen sind nicht zulässig.";
                return null;
            default:
                return "Der Hostname ist kein gültiger DNS-Name und keine IP-Adresse (ohne Schema, Port oder Pfad).";
        }
    }

    private static bool IstIPv4LinkLocalOderMulticast(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork)
            return false;
        var b = ip.GetAddressBytes();
        return b[0] == 169 && b[1] == 254   // Link-Local inkl. Cloud-Metadaten 169.254.169.254
            || b[0] >= 224;                  // Multicast (224/4) und reserviert (240/4)
    }

    /// <summary>Fehlertext, wenn der Port nicht in der erlaubten Liste steht; null = in Ordnung.</summary>
    public static string? PortFehler(int port, IReadOnlyCollection<int> erlaubt) =>
        erlaubt.Contains(port) ? null
            : $"Port {port} ist nicht zulässig. Erlaubt: {string.Join(", ", erlaubt)} (Administration → Zertifikate.ErlaubtePorts).";

    /// <summary>
    /// Adressen aus dem Empfängertext, die keine gültige Mailadresse sind oder nicht in
    /// einer erlaubten Domäne liegen. Leere Domänenliste = nur Formatprüfung.
    /// </summary>
    public static List<string> UnzulaessigeEmpfaenger(string? text, IReadOnlyCollection<string> erlaubteDomaenen) =>
        (text ?? "")
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(e => !IstZulaessig(e, erlaubteDomaenen))
            .ToList();

    /// <summary>Nur die zulässigen Adressen einer Liste (Format gültig, Domäne erlaubt).</summary>
    public static List<string> ZulaessigeEmpfaenger(IEnumerable<string> adressen, IReadOnlyCollection<string> erlaubteDomaenen) =>
        adressen.Where(e => IstZulaessig(e, erlaubteDomaenen)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static bool IstZulaessig(string adresse, IReadOnlyCollection<string> erlaubteDomaenen)
    {
        var domaene = Domaene(adresse);
        if (domaene is null)
            return false;
        return erlaubteDomaenen.Count == 0
            || erlaubteDomaenen.Any(d => domaene == d || domaene.EndsWith("." + d, StringComparison.Ordinal));
    }

    /// <summary>
    /// Fester Fehlertext je Ausnahmeart. Die Originalmeldung (mit IP-Adresse, Port und
    /// Betriebssystemdetails) gehört nur ins Server-Log, nicht in die für alle
    /// Mitarbeiter sichtbaren Felder.
    /// </summary>
    public static string FehlerText(Exception ex) => (ex is AggregateException { InnerException: { } inner } ? inner : ex) switch
    {
        OperationCanceledException => "Zeitüberschreitung (10 s)",
        SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain }
            => "Hostname nicht auflösbar",
        SocketException { SocketErrorCode: SocketError.ConnectionRefused } => "Verbindung abgelehnt",
        SocketException { SocketErrorCode: SocketError.TimedOut } => "Zeitüberschreitung beim Verbindungsaufbau",
        SocketException => "Verbindung nicht möglich",
        AuthenticationException => "TLS-Handshake fehlgeschlagen",
        IOException => "Verbindung wurde abgebrochen",
        InvalidOperationException e => e.Message,   // eigene Texte, z. B. „kein Zertifikat geliefert"
        _ => "Prüfung fehlgeschlagen",
    };
}
