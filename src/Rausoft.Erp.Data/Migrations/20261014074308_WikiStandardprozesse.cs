using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class WikiStandardprozesse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SanktionslistenStandardprozesse",
                columns: table => new
                {
                    Import = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: true),
                    GeaendertAm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GeaendertVonId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SanktionslistenStandardprozesse", x => x.Import);
                    table.ForeignKey(
                        name: "FK_SanktionslistenStandardprozesse_Mitarbeiter_GeaendertVonId",
                        column: x => x.GeaendertVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SanktionslistenStandardprozesse_GeaendertVonId",
                table: "SanktionslistenStandardprozesse",
                column: "GeaendertVonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SanktionslistenStandardprozesse");
        }
    }
}
