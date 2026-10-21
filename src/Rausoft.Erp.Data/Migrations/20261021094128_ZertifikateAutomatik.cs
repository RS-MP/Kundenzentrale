using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class ZertifikateAutomatik : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Einstellungen per SQL (kein HasData mit festen IDs — siehe Hinweis im DbContext)
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Zertifikate.EskalationTage')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Zertifikate.EskalationTage', N'3', N'Ab so vielen Tagen vor Ablauf gehen Erinnerungen zusätzlich an die Eskalationsempfänger (Betreff DRINGEND).');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Zertifikate.EskalationEmpfaenger')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Zertifikate.EskalationEmpfaenger', N'', N'Zusätzliche E-Mail-Empfänger ab der Eskalationsstufe (mehrere mit Komma).');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Zertifikate.AbgelaufenEmpfaenger')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Zertifikate.AbgelaufenEmpfaenger', N'', N'Zusätzliche E-Mail-Empfänger, sobald ein Zertifikat abgelaufen ist (z. B. Geschäftsführung).');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Zertifikate.PruefstoerungTage')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Zertifikate.PruefstoerungTage', N'3', N'Scheitert die Live-Prüfung eines Zertifikats so viele Tage am Stück, geht eine Warnmail an die Empfänger (höchstens einmal pro Woche).');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Zertifikate.WochenberichtTag')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Zertifikate.WochenberichtTag', N'Montag', N'Wochentag des Wochenberichts (Lebenszeichen der Überwachung) an die Standardempfänger; „aus“ = kein Bericht.');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Zertifikate.ServerAutomatisch')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Zertifikate.ServerAutomatisch', N'true', N'Für neue aktive ID.prove-Server täglich automatisch ein Zertifikat anlegen (Hostname aus der Server-URL).');
                """);

            migrationBuilder.AddColumn<bool>(
                name: "AutoErkennung",
                table: "Zertifikate",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutomatischAngelegt",
                table: "Zertifikate",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LiveFehlerGemeldetAm",
                table: "Zertifikate",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LiveFehlerSeit",
                table: "Zertifikate",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LiveProtokoll",
                table: "Zertifikate",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LiveWarnungen",
                table: "Zertifikate",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ZertifikatEreignisse",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ZertifikatId = table.Column<int>(type: "int", nullable: false),
                    Zeitpunkt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Art = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    VonId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZertifikatEreignisse", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ZertifikatEreignisse_Mitarbeiter_VonId",
                        column: x => x.VonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ZertifikatEreignisse_Zertifikate_ZertifikatId",
                        column: x => x.ZertifikatId,
                        principalTable: "Zertifikate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ZertifikatEreignisse_VonId",
                table: "ZertifikatEreignisse",
                column: "VonId");

            migrationBuilder.CreateIndex(
                name: "IX_ZertifikatEreignisse_ZertifikatId_Zeitpunkt",
                table: "ZertifikatEreignisse",
                columns: new[] { "ZertifikatId", "Zeitpunkt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM [Einstellungen] WHERE [Schluessel] IN (N'Zertifikate.EskalationTage', N'Zertifikate.EskalationEmpfaenger', N'Zertifikate.AbgelaufenEmpfaenger', N'Zertifikate.PruefstoerungTage', N'Zertifikate.WochenberichtTag', N'Zertifikate.ServerAutomatisch', N'Zertifikate.LetzterWochenbericht');");
            migrationBuilder.DropTable(
                name: "ZertifikatEreignisse");

            migrationBuilder.DropColumn(
                name: "AutoErkennung",
                table: "Zertifikate");

            migrationBuilder.DropColumn(
                name: "AutomatischAngelegt",
                table: "Zertifikate");

            migrationBuilder.DropColumn(
                name: "LiveFehlerGemeldetAm",
                table: "Zertifikate");

            migrationBuilder.DropColumn(
                name: "LiveFehlerSeit",
                table: "Zertifikate");

            migrationBuilder.DropColumn(
                name: "LiveProtokoll",
                table: "Zertifikate");

            migrationBuilder.DropColumn(
                name: "LiveWarnungen",
                table: "Zertifikate");
        }
    }
}
