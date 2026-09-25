using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class VorlagenVerwaltung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VorlagenVersionId",
                table: "Vertragsdokumente",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Vorlagen",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Aktiv = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vorlagen", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VorlagenVersionen",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VorlageId = table.Column<int>(type: "int", nullable: false),
                    Nummer = table.Column<int>(type: "int", nullable: false),
                    Dateipfad = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Sha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    FreigegebenAm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FreigegebenVonId = table.Column<int>(type: "int", nullable: true),
                    Notiz = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VorlagenVersionen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VorlagenVersionen_Mitarbeiter_FreigegebenVonId",
                        column: x => x.FreigegebenVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VorlagenVersionen_Vorlagen_VorlageId",
                        column: x => x.VorlageId,
                        principalTable: "Vorlagen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vertragsdokumente_VorlagenVersionId",
                table: "Vertragsdokumente",
                column: "VorlagenVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Vorlagen_Name",
                table: "Vorlagen",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VorlagenVersionen_FreigegebenVonId",
                table: "VorlagenVersionen",
                column: "FreigegebenVonId");

            migrationBuilder.CreateIndex(
                name: "IX_VorlagenVersionen_VorlageId_Nummer",
                table: "VorlagenVersionen",
                columns: new[] { "VorlageId", "Nummer" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Vertragsdokumente_VorlagenVersionen_VorlagenVersionId",
                table: "Vertragsdokumente",
                column: "VorlagenVersionId",
                principalTable: "VorlagenVersionen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Vertragsdokumente_VorlagenVersionen_VorlagenVersionId",
                table: "Vertragsdokumente");

            migrationBuilder.DropTable(
                name: "VorlagenVersionen");

            migrationBuilder.DropTable(
                name: "Vorlagen");

            migrationBuilder.DropIndex(
                name: "IX_Vertragsdokumente_VorlagenVersionId",
                table: "Vertragsdokumente");

            migrationBuilder.DropColumn(
                name: "VorlagenVersionId",
                table: "Vertragsdokumente");
        }
    }
}
