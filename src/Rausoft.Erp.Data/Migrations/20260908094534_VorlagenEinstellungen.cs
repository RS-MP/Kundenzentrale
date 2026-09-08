using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class VorlagenEinstellungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Einstellungen",
                columns: new[] { "Id", "Beschreibung", "Schluessel", "Wert" },
                values: new object[,]
                {
                    { 3, "Verzeichnis der Word-Vorlagen für die Vertragserzeugung. Nur Dateien direkt im Verzeichnis gelten als aktive Vorlagen (keine Unterordner).", "Vorlagen.Verzeichnis", "\\\\RS-W2019-01\\Kunden Archiv\\0_Vorlagen" },
                    { 4, "Zielverzeichnis für aus Vorlagen erzeugte Verträge — muss innerhalb des Vertragsablage-Verzeichnisses liegen.", "Vertragserzeugung.Zielverzeichnis", "\\\\RS-W2019-01\\Kunden Archiv\\2_Wartungsverträge" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Einstellungen",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "Einstellungen",
                keyColumn: "Id",
                keyValue: 4);
        }
    }
}
