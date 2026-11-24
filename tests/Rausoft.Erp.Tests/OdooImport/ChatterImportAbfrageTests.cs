using Rausoft.Erp.OdooImport;

namespace Rausoft.Erp.Tests.OdooImport;

/// <summary>
/// Der Verlaufs-Import liest fast alles einschließlich archivierter Datensätze —
/// aber keine archivierten Aktivitäten: Odoo archiviert eine erledigte Aktivität,
/// statt sie zu löschen. Mit archivierten stünde jede erledigte Aktivität bei jedem
/// Lauf wieder als offene, überfällige Aufgabe in der Akte (SKD, 24.11.2026).
/// </summary>
public class ChatterImportAbfrageTests
{
    [Fact]
    public void GeplanteAktivitaeten_WerdenOhneArchivierteGelesen()
    {
        Assert.False(ChatterImporter.LiestArchivierte("mail.activity"));

        var optionen = ChatterImporter.SuchOptionen("mail.activity", ["id", "summary"], 2000, 0, "id");

        // Kein eigener Kontext → Odoos Standard gilt: archivierte (= erledigte) bleiben weg
        Assert.False(optionen.ContainsKey("context"));
    }

    [Theory]
    [InlineData("mail.message")]
    [InlineData("res.partner")]
    [InlineData("crm.lead")]
    [InlineData("sale.order")]
    [InlineData("ir.attachment")]
    public void AllesAndere_WeiterhinMitArchivierten(string modell)
    {
        Assert.True(ChatterImporter.LiestArchivierte(modell));

        var optionen = ChatterImporter.SuchOptionen(modell, ["id"], 2000, 0, "id");

        var kontext = Assert.IsType<Dictionary<string, object>>(optionen["context"]);
        Assert.Equal(false, kontext["active_test"]);
    }

    [Fact]
    public void SuchOptionen_GebenFelderSeiteUndSortierungWeiter()
    {
        var optionen = ChatterImporter.SuchOptionen("mail.activity", ["id", "date_deadline"], 500, 1000, "id desc");

        Assert.Equal(new[] { "id", "date_deadline" }, optionen["fields"]);
        Assert.Equal(500, optionen["limit"]);
        Assert.Equal(1000, optionen["offset"]);
        Assert.Equal("id desc", optionen["order"]);
    }
}
