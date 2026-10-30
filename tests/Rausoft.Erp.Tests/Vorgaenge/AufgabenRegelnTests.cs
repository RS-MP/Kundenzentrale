using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Tests.Vorgaenge;

/// <summary>Wer eine Aufgabe löschen darf: wer sie angelegt hat, wer zuständig ist, oder ein Admin.</summary>
public class AufgabenRegelnTests
{
    [Theory]
    [InlineData(5, 7, 5, false, true)]      // selbst angelegt
    [InlineData(5, 7, 7, false, true)]      // bei mir
    [InlineData(5, 7, 9, false, false)]     // fremde Aufgabe
    [InlineData(5, 7, 9, true, true)]       // Admin
    [InlineData(null, null, 9, false, false)] // Pool-Aufgabe aus Odoo ohne Ersteller
    [InlineData(null, null, null, false, false)] // kein Mitarbeiter-Datensatz
    public void DarfLoeschen(int? erstellt, int? zustaendig, int? ich, bool admin, bool erwartet) =>
        Assert.Equal(erwartet, AufgabenRegeln.DarfLoeschen(erstellt, zustaendig, ich, admin));
}
