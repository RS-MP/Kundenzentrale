using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AkteUeberblick : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AktivitaetTeilnehmer",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AktivitaetId = table.Column<int>(type: "int", nullable: false),
                    Seite = table.Column<int>(type: "int", nullable: false),
                    MitarbeiterId = table.Column<int>(type: "int", nullable: true),
                    AnsprechpartnerId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AktivitaetTeilnehmer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AktivitaetTeilnehmer_Aktivitaeten_AktivitaetId",
                        column: x => x.AktivitaetId,
                        principalTable: "Aktivitaeten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AktivitaetTeilnehmer_Ansprechpartner_AnsprechpartnerId",
                        column: x => x.AnsprechpartnerId,
                        principalTable: "Ansprechpartner",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AktivitaetTeilnehmer_Mitarbeiter_MitarbeiterId",
                        column: x => x.MitarbeiterId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Aufgaben",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KundeId = table.Column<int>(type: "int", nullable: true),
                    Titel = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Art = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Beschreibung = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: true),
                    Faellig = table.Column<DateOnly>(type: "date", nullable: true),
                    ZustaendigId = table.Column<int>(type: "int", nullable: true),
                    ZustaendigBereichId = table.Column<int>(type: "int", nullable: true),
                    ZustaendigName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Bezug = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    StrangSchluessel = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Quelle = table.Column<int>(type: "int", nullable: false),
                    OdooId = table.Column<int>(type: "int", nullable: true),
                    ErstelltAm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErstelltVonId = table.Column<int>(type: "int", nullable: true),
                    ErledigtAm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErledigtVonId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Aufgaben", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Aufgaben_Bereiche_ZustaendigBereichId",
                        column: x => x.ZustaendigBereichId,
                        principalTable: "Bereiche",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Aufgaben_Kunden_KundeId",
                        column: x => x.KundeId,
                        principalTable: "Kunden",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Aufgaben_Mitarbeiter_ErledigtVonId",
                        column: x => x.ErledigtVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Aufgaben_Mitarbeiter_ErstelltVonId",
                        column: x => x.ErstelltVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Aufgaben_Mitarbeiter_ZustaendigId",
                        column: x => x.ZustaendigId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Vereinbarungen",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KundeId = table.Column<int>(type: "int", nullable: false),
                    AktivitaetId = table.Column<int>(type: "int", nullable: true),
                    Text = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Erledigt = table.Column<bool>(type: "bit", nullable: false),
                    ErstelltAm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErstelltVonId = table.Column<int>(type: "int", nullable: true),
                    ErledigtAm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErledigtVonId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vereinbarungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vereinbarungen_Aktivitaeten_AktivitaetId",
                        column: x => x.AktivitaetId,
                        principalTable: "Aktivitaeten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Vereinbarungen_Kunden_KundeId",
                        column: x => x.KundeId,
                        principalTable: "Kunden",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vereinbarungen_Mitarbeiter_ErledigtVonId",
                        column: x => x.ErledigtVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vereinbarungen_Mitarbeiter_ErstelltVonId",
                        column: x => x.ErstelltVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AktivitaetTeilnehmer_AktivitaetId",
                table: "AktivitaetTeilnehmer",
                column: "AktivitaetId");

            migrationBuilder.CreateIndex(
                name: "IX_AktivitaetTeilnehmer_AnsprechpartnerId",
                table: "AktivitaetTeilnehmer",
                column: "AnsprechpartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_AktivitaetTeilnehmer_MitarbeiterId",
                table: "AktivitaetTeilnehmer",
                column: "MitarbeiterId");

            migrationBuilder.CreateIndex(
                name: "IX_Aufgaben_ErledigtVonId",
                table: "Aufgaben",
                column: "ErledigtVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Aufgaben_ErstelltVonId",
                table: "Aufgaben",
                column: "ErstelltVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Aufgaben_KundeId_Status",
                table: "Aufgaben",
                columns: new[] { "KundeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Aufgaben_OdooId",
                table: "Aufgaben",
                column: "OdooId",
                unique: true,
                filter: "[OdooId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Aufgaben_ZustaendigBereichId",
                table: "Aufgaben",
                column: "ZustaendigBereichId");

            migrationBuilder.CreateIndex(
                name: "IX_Aufgaben_ZustaendigId",
                table: "Aufgaben",
                column: "ZustaendigId");

            migrationBuilder.CreateIndex(
                name: "IX_Vereinbarungen_AktivitaetId",
                table: "Vereinbarungen",
                column: "AktivitaetId");

            migrationBuilder.CreateIndex(
                name: "IX_Vereinbarungen_ErledigtVonId",
                table: "Vereinbarungen",
                column: "ErledigtVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Vereinbarungen_ErstelltVonId",
                table: "Vereinbarungen",
                column: "ErstelltVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Vereinbarungen_KundeId_Erledigt",
                table: "Vereinbarungen",
                columns: new[] { "KundeId", "Erledigt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AktivitaetTeilnehmer");

            migrationBuilder.DropTable(
                name: "Aufgaben");

            migrationBuilder.DropTable(
                name: "Vereinbarungen");
        }
    }
}
