using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class Einstellungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Einstellungen",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Schluessel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Wert = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Beschreibung = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Einstellungen", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Einstellungen",
                columns: new[] { "Id", "Beschreibung", "Schluessel", "Wert" },
                values: new object[,]
                {
                    { 1, "Basisverzeichnis der Vertragsablage (UNC- oder lokaler Pfad). Vertragsdokumente werden hier referenziert, Dateien bleiben im Verzeichnis.", "Vertragsablage.Verzeichnis", "" },
                    { 2, "Basisverzeichnis für sonstige Dokumente (z. B. E-Mail-Anhänge ab Phase 2).", "Dokumentablage.Verzeichnis", "" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Einstellungen_Schluessel",
                table: "Einstellungen",
                column: "Schluessel",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Einstellungen");
        }
    }
}
