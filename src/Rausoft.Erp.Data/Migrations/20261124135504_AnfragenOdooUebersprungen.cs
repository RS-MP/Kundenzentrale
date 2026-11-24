using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AnfragenOdooUebersprungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Verkaufschancen, die die Übernahme aus Odoo auslässt — Einstellung per SQL
            // (kein HasData mit festen IDs). Gefüllt wird sie beim Löschen einer übernommenen Anfrage.
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Anfragen.OdooUebersprungen')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Anfragen.OdooUebersprungen', N'', N'Nummern der Odoo-Verkaufschancen, die nicht als Website-Anfrage übernommen werden (durch Komma getrennt). Die Kundenzentrale trägt hier ein, was unter „Anfragen“ gelöscht wurde; eine Nummer herausnehmen holt die Anfrage beim nächsten Lauf zurück.');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [Einstellungen] WHERE [Schluessel] = N'Anfragen.OdooUebersprungen';");
        }
    }
}
