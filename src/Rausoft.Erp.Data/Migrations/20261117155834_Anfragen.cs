using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class Anfragen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Anfragen",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ErfasstAm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErfasstVonId = table.Column<int>(type: "int", nullable: true),
                    EingegangenAm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Formular = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Website = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Firma = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Vorname = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Nachname = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Telefon = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Nachricht = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DatenschutzBestaetigt = table.Column<bool>(type: "bit", nullable: false),
                    KundeId = table.Column<int>(type: "int", nullable: true),
                    Quelle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Medium = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Kampagne = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    KampagnenNr = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Suchbegriff = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Anzeigenvariante = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Verweis = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Einstiegsseite = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LetzteSeite = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ErsterBesuch = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UeberAnzeige = table.Column<bool>(type: "bit", nullable: false),
                    Gclid = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Gbraid = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Wbraid = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    KlickKennungenGeloeschtAm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Anfragen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Anfragen_Kunden_KundeId",
                        column: x => x.KundeId,
                        principalTable: "Kunden",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Anfragen_Mitarbeiter_ErfasstVonId",
                        column: x => x.ErfasstVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Anfragen_EingegangenAm",
                table: "Anfragen",
                column: "EingegangenAm");

            migrationBuilder.CreateIndex(
                name: "IX_Anfragen_Email",
                table: "Anfragen",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Anfragen_ErfasstVonId",
                table: "Anfragen",
                column: "ErfasstVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Anfragen_KundeId",
                table: "Anfragen",
                column: "KundeId");

            // Namen der Google-Ads-Kampagnen — Einstellung per SQL (kein HasData mit festen IDs)
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Anfragen.Kampagnen')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Anfragen.Kampagnen', N'', N'Namen der Google-Ads-Kampagnen für die Herkunft von Website-Anfragen, in der Form „Nummer = Name; Nummer = Name“. Ohne Eintrag zeigt die Kundenzentrale die Nummer der Kampagne.');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [Einstellungen] WHERE [Schluessel] = N'Anfragen.Kampagnen';");

            migrationBuilder.DropTable(
                name: "Anfragen");
        }
    }
}
