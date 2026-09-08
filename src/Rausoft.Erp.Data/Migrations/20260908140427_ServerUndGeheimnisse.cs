using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class ServerUndGeheimnisse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GeheimeEinstellungen",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Schluessel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    WertVerschluesselt = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    GesetztAm = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeheimeEinstellungen", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Server",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Alias = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    PruefMandant = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Aktiv = table.Column<bool>(type: "bit", nullable: false),
                    IstErreichbar = table.Column<bool>(type: "bit", nullable: true),
                    ConnectorAktiv = table.Column<bool>(type: "bit", nullable: true),
                    SanktionslistenStand = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Fehlertext = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    GeprueftAm = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Server", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Einstellungen",
                columns: new[] { "Id", "Beschreibung", "Schluessel", "Wert" },
                values: new object[,]
                {
                    { 5, "Basis-URL des ID.prove Service Portals (Push-Ziel der Server-Status-Meldung).", "ServicePortal.Url", "https://serviceportal.idprove.de" },
                    { 6, "Automatische Server-Status-Meldung ans Portal alle n Minuten (0 = aus; empfohlen 60 oder 120, sobald die Anwendung auf dem Server läuft).", "ServicePortal.IntervallMinuten", "0" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_GeheimeEinstellungen_Schluessel",
                table: "GeheimeEinstellungen",
                column: "Schluessel",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Server_Alias",
                table: "Server",
                column: "Alias",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeheimeEinstellungen");

            migrationBuilder.DropTable(
                name: "Server");

            migrationBuilder.DeleteData(
                table: "Einstellungen",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "Einstellungen",
                keyColumn: "Id",
                keyValue: 6);
        }
    }
}
