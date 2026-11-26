using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Tests.Shared;

/// <summary>
/// Das Datenmodell gibt jedem Textfeld ohne eigene Angabe 200 Zeichen. Formatierter Text passt
/// da nicht hinein — ein vergessener Eintrag fällt erst beim Speichern auf dem Server auf
/// (26.11.2026: Signatur aus Odoo ließ sich nicht speichern). Diese Tests bauen nur das Modell,
/// eine Datenbank brauchen sie nicht.
/// </summary>
public class DatenmodellTests
{
    // Maßgeblich ist der Spaltentyp: Er gilt auch dann, wenn die Standardlänge am Feld stehen bleibt
    private const string Unbegrenzt = "nvarchar(max)";

    private static ErpDbContext Modell() =>
        new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer("Server=unbenutzt;Database=unbenutzt").Options);

    [Fact]
    public void FormatierterTextHatKeineLaengengrenze()
    {
        using var db = Modell();

        var begrenzt = db.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(string) && p.Name.EndsWith("Html", StringComparison.Ordinal) && p.GetColumnType() != Unbegrenzt)
            .Select(p => $"{p.DeclaringType.ClrType.Name}.{p.Name} ({p.GetColumnType()})")
            .ToList();

        Assert.Empty(begrenzt);
    }

    [Theory]
    [InlineData(typeof(Mitarbeiter), nameof(Mitarbeiter.MailSignaturHtml))]
    [InlineData(typeof(Ausgangsmail), nameof(Ausgangsmail.InhaltHtml))]
    [InlineData(typeof(Textbaustein), nameof(Textbaustein.InhaltHtml))]
    [InlineData(typeof(Aktivitaet), nameof(Aktivitaet.Inhalt))]
    public void LangeTexteSindUnbegrenzt(Type entitaet, string eigenschaft)
    {
        using var db = Modell();

        Assert.Equal(Unbegrenzt, db.Model.FindEntityType(entitaet)!.FindProperty(eigenschaft)!.GetColumnType());
    }
}
