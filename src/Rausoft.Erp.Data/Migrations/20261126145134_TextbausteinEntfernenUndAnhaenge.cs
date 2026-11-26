using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class TextbausteinEntfernenUndAnhaenge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EntferntAm",
                table: "Textbausteine",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EntferntVonId",
                table: "Textbausteine",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TextbausteinAnhaenge",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TextbausteinId = table.Column<int>(type: "int", nullable: false),
                    Dateiname = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Groesse = table.Column<long>(type: "bigint", nullable: false),
                    Inhalt = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    OdooId = table.Column<int>(type: "int", nullable: true),
                    OdooStand = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    UebernommenAm = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TextbausteinAnhaenge", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TextbausteinAnhaenge_Textbausteine_TextbausteinId",
                        column: x => x.TextbausteinId,
                        principalTable: "Textbausteine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Textbausteine_EntferntVonId",
                table: "Textbausteine",
                column: "EntferntVonId");

            migrationBuilder.CreateIndex(
                name: "IX_TextbausteinAnhaenge_TextbausteinId_OdooId",
                table: "TextbausteinAnhaenge",
                columns: new[] { "TextbausteinId", "OdooId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Textbausteine_Mitarbeiter_EntferntVonId",
                table: "Textbausteine",
                column: "EntferntVonId",
                principalTable: "Mitarbeiter",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Textbausteine_Mitarbeiter_EntferntVonId",
                table: "Textbausteine");

            migrationBuilder.DropTable(
                name: "TextbausteinAnhaenge");

            migrationBuilder.DropIndex(
                name: "IX_Textbausteine_EntferntVonId",
                table: "Textbausteine");

            migrationBuilder.DropColumn(
                name: "EntferntAm",
                table: "Textbausteine");

            migrationBuilder.DropColumn(
                name: "EntferntVonId",
                table: "Textbausteine");
        }
    }
}
