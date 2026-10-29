using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Tests.Contracts;

/// <summary>Bereinigung fremder Werte (Odoo-ref, Anhangsnamen) vor dem Einsatz als Dateiname (SICHERHEIT.md N5).</summary>
public class DateinamenTests
{
    [Theory]
    [InlineData("25757", "25757")]
    [InlineData("K-00123", "K-00123")]
    [InlineData("  Angebot 2026.pdf ", "Angebot 2026.pdf")]
    [InlineData("..\\..\\x", "_.._x")]
    [InlineData("../etc/passwd", "_etc_passwd")]
    [InlineData("C:\\Windows\\win.ini", "C__Windows_win.ini")]
    [InlineData("a/b", "a_b")]
    [InlineData("...", "datei")]
    [InlineData("", "datei")]
    [InlineData(null, "datei")]
    public void Bereinigt(string? eingabe, string erwartet) => Assert.Equal(erwartet, Dateinamen.Sicher(eingabe));

    [Fact]
    public void LaengeUndErsatz()
    {
        Assert.Equal(150, Dateinamen.Sicher(new string('x', 400)).Length);
        Assert.Equal("kunde-7", Dateinamen.Sicher("\\\\", ersatz: "kunde-7"));
        Assert.DoesNotContain(Path.DirectorySeparatorChar, Dateinamen.Sicher("a\\b/c:d"));
    }
}
