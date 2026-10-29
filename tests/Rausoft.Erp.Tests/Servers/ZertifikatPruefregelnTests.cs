using System.Net.Sockets;
using System.Security.Authentication;
using Rausoft.Erp.Domain.Servers;

namespace Rausoft.Erp.Tests.Servers;

/// <summary>
/// Schutzregeln der Zertifikats-Live-Prüfung (SICHERHEIT.md N1): Hostname nur DNS
/// oder öffentliche/private IP, nie Loopback/Link-Local; Port nur aus der Liste;
/// Empfänger nur in erlaubten Domänen; Fehlertexte ohne Details.
/// </summary>
public class ZertifikatPruefregelnTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("idprove.rausoft.de")]
    [InlineData("rs-w2019-01")]
    [InlineData("kunde.example.")]
    [InlineData("192.168.0.203")]
    [InlineData("10.0.0.5")]
    [InlineData("2001:db8::1")]
    [InlineData("[2001:db8::1]")]
    public void ZulaessigeHostnamen(string? host) => Assert.Null(ZertifikatPruefregeln.HostnameFehler(host));

    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("app.localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("127.1.2.3")]
    [InlineData("::1")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("169.254.169.254")]
    [InlineData("fe80::1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("https://host.de")]
    [InlineData("host.de:443")]
    [InlineData("host.de/pfad")]
    [InlineData("host name")]
    public void UnzulaessigeHostnamen(string host) => Assert.NotNull(ZertifikatPruefregeln.HostnameFehler(host));

    [Fact]
    public void PortsAusEinstellungMitStandard()
    {
        Assert.Equal([443, 8443], ZertifikatPruefregeln.Ports(null));
        Assert.Equal([443, 8443], ZertifikatPruefregeln.Ports("abc, 0, 70000"));
        Assert.Equal([443, 993, 8443], ZertifikatPruefregeln.Ports("8443; 443 993 443"));
    }

    [Fact]
    public void PortNurAusListe()
    {
        var erlaubt = ZertifikatPruefregeln.Ports("443,8443");
        Assert.Null(ZertifikatPruefregeln.PortFehler(443, erlaubt));
        Assert.Null(ZertifikatPruefregeln.PortFehler(8443, erlaubt));
        Assert.Contains("1433", ZertifikatPruefregeln.PortFehler(1433, erlaubt));
        Assert.Contains("3389", ZertifikatPruefregeln.PortFehler(3389, erlaubt));
    }

    [Fact]
    public void DomaenenAusEinstellung()
    {
        Assert.Empty(ZertifikatPruefregeln.Domaenen(null));
        Assert.Equal(["rausoft.de", "kunde.example"], ZertifikatPruefregeln.Domaenen("@rausoft.de, Kunde.Example; ungültig!"));
        Assert.Equal("rausoft.de", ZertifikatPruefregeln.Domaene("Kundenzentrale <noreply@Rausoft.de>"));
        Assert.Null(ZertifikatPruefregeln.Domaene("kein-at"));
    }

    [Fact]
    public void EmpfaengerNurInErlaubtenDomaenen()
    {
        var domaenen = ZertifikatPruefregeln.Domaenen("rausoft.de");
        Assert.Empty(ZertifikatPruefregeln.UnzulaessigeEmpfaenger("technik@rausoft.de, mp@sub.rausoft.de", domaenen));
        Assert.Equal(["x@fremd.de", "ohne-at"],
            ZertifikatPruefregeln.UnzulaessigeEmpfaenger("technik@rausoft.de; x@fremd.de, ohne-at", domaenen));
        // „rausoft.de.evil.com" ist keine Unterdomäne
        Assert.Single(ZertifikatPruefregeln.UnzulaessigeEmpfaenger("a@rausoft.de.evil.com", domaenen));
        Assert.Equal(["technik@rausoft.de"],
            ZertifikatPruefregeln.ZulaessigeEmpfaenger(["technik@rausoft.de", "x@fremd.de", "TECHNIK@rausoft.de"], domaenen));
    }

    [Fact]
    public void OhneDomaenenlisteNurFormatpruefung()
    {
        Assert.Empty(ZertifikatPruefregeln.UnzulaessigeEmpfaenger("a@fremd.de, b@rausoft.de", []));
        Assert.Equal(["ohne-at"], ZertifikatPruefregeln.UnzulaessigeEmpfaenger("a@fremd.de, ohne-at", []));
    }

    [Fact]
    public void FehlertexteOhneDetails()
    {
        Assert.Equal("Verbindung abgelehnt", ZertifikatPruefregeln.FehlerText(new SocketException((int)SocketError.ConnectionRefused)));
        Assert.Equal("Hostname nicht auflösbar", ZertifikatPruefregeln.FehlerText(new SocketException((int)SocketError.HostNotFound)));
        Assert.Equal("Zeitüberschreitung (10 s)", ZertifikatPruefregeln.FehlerText(new OperationCanceledException()));
        Assert.Equal("TLS-Handshake fehlgeschlagen", ZertifikatPruefregeln.FehlerText(new AuthenticationException("Details 10.0.0.5:443")));
        Assert.Equal("Verbindung wurde abgebrochen", ZertifikatPruefregeln.FehlerText(new IOException("EOF von 10.0.0.5")));
        Assert.Equal("Der Server hat kein Zertifikat geliefert.", ZertifikatPruefregeln.FehlerText(new InvalidOperationException("Der Server hat kein Zertifikat geliefert.")));
        Assert.Equal("Verbindung abgelehnt", ZertifikatPruefregeln.FehlerText(new AggregateException(new SocketException((int)SocketError.ConnectionRefused))));
        Assert.DoesNotContain("10.0.0.5", ZertifikatPruefregeln.FehlerText(new Exception("x 10.0.0.5")));
    }
}
