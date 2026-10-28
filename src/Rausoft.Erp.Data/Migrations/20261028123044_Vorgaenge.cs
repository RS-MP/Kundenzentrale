using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class Vorgaenge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Aufgaben_ZustaendigBereichId",
                table: "Aufgaben");

            migrationBuilder.DropIndex(
                name: "IX_Aufgaben_ZustaendigId",
                table: "Aufgaben");

            migrationBuilder.AddColumn<int>(
                name: "LeiterId",
                table: "Bereiche",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AutoAbhaken",
                table: "Aufgaben",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Benachrichtigung",
                table: "Aufgaben",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EntfallenGrund",
                table: "Aufgaben",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FristTage",
                table: "Aufgaben",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Herkunft",
                table: "Aufgaben",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Pflichtfeld",
                table: "Aufgaben",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PflichtfeldWert",
                table: "Aufgaben",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Reihenfolge",
                table: "Aufgaben",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VorgangId",
                table: "Aufgaben",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WartetAufId",
                table: "Aufgaben",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "Wiedervorlage",
                table: "Aufgaben",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Vorgangstypen",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Beschreibung = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Aktiv = table.Column<bool>(type: "bit", nullable: false),
                    Stand = table.Column<int>(type: "int", nullable: false),
                    GeaendertAm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertVonId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vorgangstypen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vorgangstypen_Mitarbeiter_GeaendertVonId",
                        column: x => x.GeaendertVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Vorgaenge",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KundeId = table.Column<int>(type: "int", nullable: false),
                    VorgangstypId = table.Column<int>(type: "int", nullable: false),
                    VorgangstypStand = table.Column<int>(type: "int", nullable: false),
                    Titel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OdooAuftrag = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Hinweis = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Start = table.Column<DateOnly>(type: "date", nullable: false),
                    ErstelltAm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErstelltVonId = table.Column<int>(type: "int", nullable: true),
                    AbgeschlossenAm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StrangSchluessel = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    StrangName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vorgaenge", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vorgaenge_Kunden_KundeId",
                        column: x => x.KundeId,
                        principalTable: "Kunden",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vorgaenge_Mitarbeiter_ErstelltVonId",
                        column: x => x.ErstelltVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Vorgaenge_Vorgangstypen_VorgangstypId",
                        column: x => x.VorgangstypId,
                        principalTable: "Vorgangstypen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VorgangstypSchritte",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VorgangstypId = table.Column<int>(type: "int", nullable: false),
                    Nr = table.Column<int>(type: "int", nullable: false),
                    Titel = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    BereichId = table.Column<int>(type: "int", nullable: false),
                    FristTage = table.Column<int>(type: "int", nullable: false),
                    WartetAufNr = table.Column<int>(type: "int", nullable: true),
                    Bedingung = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Benachrichtigung = table.Column<int>(type: "int", nullable: false),
                    Pflichtfeld = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AutoAbhaken = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VorgangstypSchritte", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VorgangstypSchritte_Bereiche_BereichId",
                        column: x => x.BereichId,
                        principalTable: "Bereiche",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VorgangstypSchritte_Vorgangstypen_VorgangstypId",
                        column: x => x.VorgangstypId,
                        principalTable: "Vorgangstypen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bereiche_LeiterId",
                table: "Bereiche",
                column: "LeiterId");

            migrationBuilder.CreateIndex(
                name: "IX_Aufgaben_VorgangId",
                table: "Aufgaben",
                column: "VorgangId");

            migrationBuilder.CreateIndex(
                name: "IX_Aufgaben_WartetAufId",
                table: "Aufgaben",
                column: "WartetAufId");

            migrationBuilder.CreateIndex(
                name: "IX_Aufgaben_ZustaendigBereichId_Status",
                table: "Aufgaben",
                columns: new[] { "ZustaendigBereichId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Aufgaben_ZustaendigId_Status",
                table: "Aufgaben",
                columns: new[] { "ZustaendigId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Vorgaenge_AbgeschlossenAm",
                table: "Vorgaenge",
                column: "AbgeschlossenAm");

            migrationBuilder.CreateIndex(
                name: "IX_Vorgaenge_ErstelltVonId",
                table: "Vorgaenge",
                column: "ErstelltVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Vorgaenge_KundeId",
                table: "Vorgaenge",
                column: "KundeId");

            migrationBuilder.CreateIndex(
                name: "IX_Vorgaenge_OdooAuftrag",
                table: "Vorgaenge",
                column: "OdooAuftrag",
                filter: "[OdooAuftrag] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Vorgaenge_VorgangstypId",
                table: "Vorgaenge",
                column: "VorgangstypId");

            migrationBuilder.CreateIndex(
                name: "IX_Vorgangstypen_GeaendertVonId",
                table: "Vorgangstypen",
                column: "GeaendertVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Vorgangstypen_Name",
                table: "Vorgangstypen",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VorgangstypSchritte_BereichId",
                table: "VorgangstypSchritte",
                column: "BereichId");

            migrationBuilder.CreateIndex(
                name: "IX_VorgangstypSchritte_VorgangstypId_Nr",
                table: "VorgangstypSchritte",
                columns: new[] { "VorgangstypId", "Nr" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Aufgaben_Aufgaben_WartetAufId",
                table: "Aufgaben",
                column: "WartetAufId",
                principalTable: "Aufgaben",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Aufgaben_Vorgaenge_VorgangId",
                table: "Aufgaben",
                column: "VorgangId",
                principalTable: "Vorgaenge",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Bereiche_Mitarbeiter_LeiterId",
                table: "Bereiche",
                column: "LeiterId",
                principalTable: "Mitarbeiter",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Aufgaben_Aufgaben_WartetAufId",
                table: "Aufgaben");

            migrationBuilder.DropForeignKey(
                name: "FK_Aufgaben_Vorgaenge_VorgangId",
                table: "Aufgaben");

            migrationBuilder.DropForeignKey(
                name: "FK_Bereiche_Mitarbeiter_LeiterId",
                table: "Bereiche");

            migrationBuilder.DropTable(
                name: "Vorgaenge");

            migrationBuilder.DropTable(
                name: "VorgangstypSchritte");

            migrationBuilder.DropTable(
                name: "Vorgangstypen");

            migrationBuilder.DropIndex(
                name: "IX_Bereiche_LeiterId",
                table: "Bereiche");

            migrationBuilder.DropIndex(
                name: "IX_Aufgaben_VorgangId",
                table: "Aufgaben");

            migrationBuilder.DropIndex(
                name: "IX_Aufgaben_WartetAufId",
                table: "Aufgaben");

            migrationBuilder.DropIndex(
                name: "IX_Aufgaben_ZustaendigBereichId_Status",
                table: "Aufgaben");

            migrationBuilder.DropIndex(
                name: "IX_Aufgaben_ZustaendigId_Status",
                table: "Aufgaben");

            migrationBuilder.DropColumn(
                name: "LeiterId",
                table: "Bereiche");

            migrationBuilder.DropColumn(
                name: "AutoAbhaken",
                table: "Aufgaben");

            migrationBuilder.DropColumn(
                name: "Benachrichtigung",
                table: "Aufgaben");

            migrationBuilder.DropColumn(
                name: "EntfallenGrund",
                table: "Aufgaben");

            migrationBuilder.DropColumn(
                name: "FristTage",
                table: "Aufgaben");

            migrationBuilder.DropColumn(
                name: "Herkunft",
                table: "Aufgaben");

            migrationBuilder.DropColumn(
                name: "Pflichtfeld",
                table: "Aufgaben");

            migrationBuilder.DropColumn(
                name: "PflichtfeldWert",
                table: "Aufgaben");

            migrationBuilder.DropColumn(
                name: "Reihenfolge",
                table: "Aufgaben");

            migrationBuilder.DropColumn(
                name: "VorgangId",
                table: "Aufgaben");

            migrationBuilder.DropColumn(
                name: "WartetAufId",
                table: "Aufgaben");

            migrationBuilder.DropColumn(
                name: "Wiedervorlage",
                table: "Aufgaben");

            migrationBuilder.CreateIndex(
                name: "IX_Aufgaben_ZustaendigBereichId",
                table: "Aufgaben",
                column: "ZustaendigBereichId");

            migrationBuilder.CreateIndex(
                name: "IX_Aufgaben_ZustaendigId",
                table: "Aufgaben",
                column: "ZustaendigId");
        }
    }
}
