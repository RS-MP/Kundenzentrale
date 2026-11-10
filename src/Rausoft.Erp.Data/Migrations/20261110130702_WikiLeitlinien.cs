using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class WikiLeitlinien : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SprachregelId",
                table: "WikiNeuerungen",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Zielrollen",
                table: "WikiNeuerungen",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Sprachregeln",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Art = table.Column<int>(type: "int", nullable: false),
                    Aussage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Stattdessen = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Anlass = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Begruendung = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: true),
                    BegruendungIntern = table.Column<bool>(type: "bit", nullable: false),
                    BeispielGut = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    BeispielSchlecht = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Varianten = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Zielgruppen = table.Column<int>(type: "int", nullable: false),
                    Thema = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Produkt = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Kanaele = table.Column<int>(type: "int", nullable: false),
                    KiHinweis = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    GueltigAb = table.Column<DateOnly>(type: "date", nullable: true),
                    VorgaengerId = table.Column<int>(type: "int", nullable: true),
                    ErstelltAm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErstelltVonId = table.Column<int>(type: "int", nullable: true),
                    GeaendertAm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GeaendertVonId = table.Column<int>(type: "int", nullable: true),
                    FreigegebenAm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FreigegebenVonId = table.Column<int>(type: "int", nullable: true),
                    ZurueckgezogenAm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ZurueckgezogenVonId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sprachregeln", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sprachregeln_Mitarbeiter_ErstelltVonId",
                        column: x => x.ErstelltVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Sprachregeln_Mitarbeiter_FreigegebenVonId",
                        column: x => x.FreigegebenVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Sprachregeln_Mitarbeiter_GeaendertVonId",
                        column: x => x.GeaendertVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Sprachregeln_Mitarbeiter_ZurueckgezogenVonId",
                        column: x => x.ZurueckgezogenVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WikiNeuerungen_SprachregelId",
                table: "WikiNeuerungen",
                column: "SprachregelId");

            migrationBuilder.CreateIndex(
                name: "IX_Sprachregeln_ErstelltVonId",
                table: "Sprachregeln",
                column: "ErstelltVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Sprachregeln_FreigegebenVonId",
                table: "Sprachregeln",
                column: "FreigegebenVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Sprachregeln_GeaendertVonId",
                table: "Sprachregeln",
                column: "GeaendertVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Sprachregeln_Status_Art",
                table: "Sprachregeln",
                columns: new[] { "Status", "Art" });

            migrationBuilder.CreateIndex(
                name: "IX_Sprachregeln_ZurueckgezogenVonId",
                table: "Sprachregeln",
                column: "ZurueckgezogenVonId");

            migrationBuilder.AddForeignKey(
                name: "FK_WikiNeuerungen_Sprachregeln_SprachregelId",
                table: "WikiNeuerungen",
                column: "SprachregelId",
                principalTable: "Sprachregeln",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Drei Beispiele zum Ausprobieren — ausdrücklich als ENTWURF (Status 0) und mit dem
            // Präfix „Beispiel:"; es gibt keine vorab freigegebenen Firmenregeln. Entwürfe gelten
            // nicht, lösen keine Lesebestätigung aus und erscheinen in keinem Export.
            // Art: 4 = Grundsatz, 1 = Vermeiden, 2 = Verboten; Zielgruppen 7 = Vertrieb + Support + KI.
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [Sprachregeln])
                INSERT INTO [Sprachregeln] ([Art], [Aussage], [Stattdessen], [Begruendung], [BegruendungIntern], [BeispielGut], [BeispielSchlecht], [Varianten], [Zielgruppen], [Thema], [Kanaele], [KiHinweis], [Status], [ErstelltAm])
                VALUES
                (4, N'Beispiel: Kunden und Interessenten sprechen wir mit „Sie“ an – freundlich, sachlich und ohne Werbesuperlative.',
                    NULL,
                    N'Beispiel-Entwurf zum Ausprobieren – bitte prüfen, anpassen oder löschen. Unsere Kunden sind Unternehmen mit Compliance-Aufgaben; ein ruhiger, verlässlicher Ton passt zu dem, was sie von uns erwarten.',
                    0, NULL, NULL, NULL, 7, N'ton', 0, NULL, 0, SYSUTCDATETIME()),
                (1, N'Beispiel: garantiert',
                    N'Beschreiben, was die Software tut – z. B. „ID.prove prüft Ihre Geschäftspartner gegen die abonnierten Sanktionslisten“.',
                    N'Beispiel-Entwurf zum Ausprobieren – bitte prüfen, anpassen oder löschen. Das Wort klingt nach einer rechtlich bindenden Zusage; verbindliche Zusagen stehen im Vertrag, nicht in einer E-Mail.',
                    0,
                    N'ID.prove prüft jede neue Adresse automatisch gegen die abonnierten Listen.',
                    N'Mit ID.prove ist garantiert, dass Ihnen kein Treffer entgeht.',
                    N'garantieren, Garantie, garantierte', 7, N'produktleistung', 0, NULL, 0, SYSUTCDATETIME()),
                (2, N'Beispiel: Zusage rechtlicher Sicherheit bei der Sanktionslistenprüfung (z. B. „Mit ID.prove sind Sie rechtlich auf der sicheren Seite“).',
                    NULL,
                    N'Beispiel-Entwurf zum Ausprobieren – bitte prüfen, anpassen oder löschen. Die Software unterstützt die Prüfung; die Verantwortung für die Einhaltung der Sanktionsvorschriften bleibt beim Kunden. Eine Zusage rechtlicher Sicherheit können wir nicht einhalten.',
                    0, NULL, NULL, NULL, 7, N'recht', 0,
                    N'Beschreiben, was die Prüfung leistet, und für rechtliche Einschätzungen an den Vertrieb verweisen.',
                    0, SYSUTCDATETIME());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WikiNeuerungen_Sprachregeln_SprachregelId",
                table: "WikiNeuerungen");

            migrationBuilder.DropTable(
                name: "Sprachregeln");

            migrationBuilder.DropIndex(
                name: "IX_WikiNeuerungen_SprachregelId",
                table: "WikiNeuerungen");

            migrationBuilder.DropColumn(
                name: "SprachregelId",
                table: "WikiNeuerungen");

            migrationBuilder.DropColumn(
                name: "Zielrollen",
                table: "WikiNeuerungen");
        }
    }
}
