using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class WikiPreisliste : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PreislistenVersionen",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Stand = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    GueltigAb = table.Column<DateOnly>(type: "date", nullable: true),
                    Produkt = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Schema = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Daten = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: false),
                    Sha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    DateiIntern = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    DateiExtern = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    ImportiertAm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ImportiertVonId = table.Column<int>(type: "int", nullable: true),
                    Notiz = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreislistenVersionen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PreislistenVersionen_Mitarbeiter_ImportiertVonId",
                        column: x => x.ImportiertVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WikiThemaInfos",
                columns: table => new
                {
                    Thema = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    VerantwortlichId = table.Column<int>(type: "int", nullable: true),
                    VertreterId = table.Column<int>(type: "int", nullable: true),
                    Prozess = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: true),
                    GeaendertAm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GeaendertVonId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WikiThemaInfos", x => x.Thema);
                    table.ForeignKey(
                        name: "FK_WikiThemaInfos_Mitarbeiter_GeaendertVonId",
                        column: x => x.GeaendertVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WikiThemaInfos_Mitarbeiter_VerantwortlichId",
                        column: x => x.VerantwortlichId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WikiThemaInfos_Mitarbeiter_VertreterId",
                        column: x => x.VertreterId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PreislistenVersionen_ImportiertVonId",
                table: "PreislistenVersionen",
                column: "ImportiertVonId");

            migrationBuilder.CreateIndex(
                name: "IX_PreislistenVersionen_Version",
                table: "PreislistenVersionen",
                column: "Version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WikiThemaInfos_GeaendertVonId",
                table: "WikiThemaInfos",
                column: "GeaendertVonId");

            migrationBuilder.CreateIndex(
                name: "IX_WikiThemaInfos_VerantwortlichId",
                table: "WikiThemaInfos",
                column: "VerantwortlichId");

            migrationBuilder.CreateIndex(
                name: "IX_WikiThemaInfos_VertreterId",
                table: "WikiThemaInfos",
                column: "VertreterId");

            // Einstellung per SQL statt HasData (feste Seed-IDs kollidieren mit Laufzeit-Zeilen)
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Preisliste.Verzeichnis')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Preisliste.Verzeichnis', N'\\192.168.0.203\Produktiv\01_Vertrieb\Preisliste',
                            N'Ordner mit den Preislisten (interne und externe PDF + JSON-Export aus dem Preislisten-Projekt). Das Wiki verlinkt die PDFs von dort; der JSON-Import läuft per Datei-Upload.');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [Einstellungen] WHERE [Schluessel] = N'Preisliste.Verzeichnis';");

            migrationBuilder.DropTable(
                name: "PreislistenVersionen");

            migrationBuilder.DropTable(
                name: "WikiThemaInfos");
        }
    }
}
