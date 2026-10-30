using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Tests.Vorgaenge;

/// <summary>
/// Nur in der Kundenzentrale angelegte Aufgaben ohne Vorgang sind änderbar; löschen darf,
/// wer sie angelegt hat, wer zuständig ist, oder ein Admin.
/// </summary>
public class AufgabenRegelnTests
{
    [Theory]
    [InlineData(null, null, true)]
    [InlineData(12, null, false)]   // Vorgangsschritt
    [InlineData(null, 4711, false)] // aus Odoo übernommen
    public void DarfAendern(int? vorgang, int? odoo, bool erwartet) =>
        Assert.Equal(erwartet, AufgabenRegeln.DarfAendern(vorgang, odoo));

    [Theory]
    [InlineData(5, 7, 5, false, true)]           // selbst angelegt
    [InlineData(5, 7, 7, false, true)]           // bei mir
    [InlineData(5, 7, 9, false, false)]          // fremde Aufgabe
    [InlineData(5, 7, 9, true, true)]            // Admin
    [InlineData(null, null, null, false, false)] // kein Mitarbeiter-Datensatz
    public void DarfLoeschen(int? erstellt, int? zustaendig, int? ich, bool admin, bool erwartet) =>
        Assert.Equal(erwartet, AufgabenRegeln.DarfLoeschen(null, null, erstellt, zustaendig, ich, admin));

    [Fact]
    public void OdooAufgabeAuchFuerAdminNichtLoeschbar()
    {
        Assert.False(AufgabenRegeln.DarfLoeschen(null, 4711, null, 5, 5, istAdmin: true));
        Assert.False(AufgabenRegeln.DarfLoeschen(12, null, 5, 5, 5, istAdmin: true));
    }
}
