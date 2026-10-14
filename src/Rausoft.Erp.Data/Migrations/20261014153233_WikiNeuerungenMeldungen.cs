using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class WikiNeuerungenMeldungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AbschnitteOffen",
                table: "WikiThemaInfos",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WikiMeldungen",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Thema = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Bezug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Text = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: false),
                    ErstelltAm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErstelltVonId = table.Column<int>(type: "int", nullable: true),
                    ErledigtAm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErledigtVonId = table.Column<int>(type: "int", nullable: true),
                    Antwort = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WikiMeldungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WikiMeldungen_Mitarbeiter_ErledigtVonId",
                        column: x => x.ErledigtVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WikiMeldungen_Mitarbeiter_ErstelltVonId",
                        column: x => x.ErstelltVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WikiNeuerungen",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Thema = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Titel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: false),
                    PreislistenVersionId = table.Column<int>(type: "int", nullable: true),
                    BestaetigungErforderlich = table.Column<bool>(type: "bit", nullable: false),
                    ErstelltAm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErstelltVonId = table.Column<int>(type: "int", nullable: true),
                    GeaendertAm = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WikiNeuerungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WikiNeuerungen_Mitarbeiter_ErstelltVonId",
                        column: x => x.ErstelltVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WikiNeuerungen_PreislistenVersionen_PreislistenVersionId",
                        column: x => x.PreislistenVersionId,
                        principalTable: "PreislistenVersionen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "WikiNeuerungLesungen",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NeuerungId = table.Column<int>(type: "int", nullable: false),
                    MitarbeiterId = table.Column<int>(type: "int", nullable: false),
                    GesehenAm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BestaetigtAm = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WikiNeuerungLesungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WikiNeuerungLesungen_Mitarbeiter_MitarbeiterId",
                        column: x => x.MitarbeiterId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WikiNeuerungLesungen_WikiNeuerungen_NeuerungId",
                        column: x => x.NeuerungId,
                        principalTable: "WikiNeuerungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WikiMeldungen_ErledigtVonId",
                table: "WikiMeldungen",
                column: "ErledigtVonId");

            migrationBuilder.CreateIndex(
                name: "IX_WikiMeldungen_ErstelltVonId",
                table: "WikiMeldungen",
                column: "ErstelltVonId");

            migrationBuilder.CreateIndex(
                name: "IX_WikiMeldungen_Thema_ErledigtAm",
                table: "WikiMeldungen",
                columns: new[] { "Thema", "ErledigtAm" });

            migrationBuilder.CreateIndex(
                name: "IX_WikiNeuerungen_ErstelltVonId",
                table: "WikiNeuerungen",
                column: "ErstelltVonId");

            migrationBuilder.CreateIndex(
                name: "IX_WikiNeuerungen_PreislistenVersionId",
                table: "WikiNeuerungen",
                column: "PreislistenVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_WikiNeuerungen_Thema",
                table: "WikiNeuerungen",
                column: "Thema");

            migrationBuilder.CreateIndex(
                name: "IX_WikiNeuerungLesungen_MitarbeiterId",
                table: "WikiNeuerungLesungen",
                column: "MitarbeiterId");

            migrationBuilder.CreateIndex(
                name: "IX_WikiNeuerungLesungen_NeuerungId_MitarbeiterId",
                table: "WikiNeuerungLesungen",
                columns: new[] { "NeuerungId", "MitarbeiterId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WikiMeldungen");

            migrationBuilder.DropTable(
                name: "WikiNeuerungLesungen");

            migrationBuilder.DropTable(
                name: "WikiNeuerungen");

            migrationBuilder.DropColumn(
                name: "AbschnitteOffen",
                table: "WikiThemaInfos");
        }
    }
}
