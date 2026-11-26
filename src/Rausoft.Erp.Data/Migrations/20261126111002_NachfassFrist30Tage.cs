using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class NachfassFrist30Tage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nachfass-Fristen für offene Angebote: 30 Tage statt 7 bzw. 14 (MP 26.11.2026 — Angebote
            // laufen bei uns eher einen Monat und länger). Nur die unveränderten Startwerte werden
            // angehoben; wer die Frist schon selbst eingestellt hat (auch 0 = aus), behält seinen Wert.
            migrationBuilder.Sql("""
                UPDATE [Einstellungen] SET [Wert] = N'30'
                WHERE [Schluessel] = N'Verkauf.NachfassTage' AND [Wert] = N'7';
                UPDATE [Einstellungen] SET [Wert] = N'30'
                WHERE [Schluessel] = N'Verkauf.NachfassWiederholungTage' AND [Wert] = N'14';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [Einstellungen] SET [Wert] = N'7'
                WHERE [Schluessel] = N'Verkauf.NachfassTage' AND [Wert] = N'30';
                UPDATE [Einstellungen] SET [Wert] = N'14'
                WHERE [Schluessel] = N'Verkauf.NachfassWiederholungTage' AND [Wert] = N'30';
                """);
        }
    }
}
