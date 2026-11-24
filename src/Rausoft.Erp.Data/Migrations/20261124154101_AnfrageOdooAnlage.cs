using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AnfrageOdooAnlage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnfrageOdooAnlagen",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnfrageId = table.Column<int>(type: "int", nullable: false),
                    Ziel = table.Column<int>(type: "int", nullable: false),
                    Datenbank = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Stand = table.Column<int>(type: "int", nullable: false),
                    BegonnenAm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BegonnenVonId = table.Column<int>(type: "int", nullable: true),
                    AbgeschlossenAm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LaeuftSeit = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FirmaId = table.Column<int>(type: "int", nullable: true),
                    FirmaNeu = table.Column<bool>(type: "bit", nullable: false),
                    KontaktId = table.Column<int>(type: "int", nullable: true),
                    KontaktNeu = table.Column<bool>(type: "bit", nullable: false),
                    HauptansprechpartnerErledigt = table.Column<bool>(type: "bit", nullable: false),
                    LeadId = table.Column<int>(type: "int", nullable: true),
                    LeadNeu = table.Column<bool>(type: "bit", nullable: false),
                    AktivitaetId = table.Column<int>(type: "int", nullable: true),
                    AktivitaetErledigt = table.Column<bool>(type: "bit", nullable: false),
                    LetzterFehler = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnfrageOdooAnlagen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnfrageOdooAnlagen_Anfragen_AnfrageId",
                        column: x => x.AnfrageId,
                        principalTable: "Anfragen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AnfrageOdooAnlagen_Mitarbeiter_BegonnenVonId",
                        column: x => x.BegonnenVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnfrageOdooAnlagen_AnfrageId_Datenbank",
                table: "AnfrageOdooAnlagen",
                columns: new[] { "AnfrageId", "Datenbank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnfrageOdooAnlagen_BegonnenVonId",
                table: "AnfrageOdooAnlagen",
                column: "BegonnenVonId");

            // Gemerkte letzte Auswahl beim Anlegen in Odoo — Einstellung per SQL (kein HasData mit festen IDs).
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Anfragen.OdooVorbelegung')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Anfragen.OdooVorbelegung', N'', N'Zuletzt gewählte Phase, Aktivitätsart, Zuweisung, Quelle und Medium beim Anlegen einer Anfrage in Odoo (als Namen, „Schlüssel = Name; …“). Die Kundenzentrale pflegt das selbst; leeren setzt die Vorbelegung zurück.');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [Einstellungen] WHERE [Schluessel] = N'Anfragen.OdooVorbelegung';");

            migrationBuilder.DropTable(
                name: "AnfrageOdooAnlagen");
        }
    }
}
