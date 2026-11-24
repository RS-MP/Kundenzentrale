using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class Margenrechner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MargenrechnerStaende",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DatenVerschluesselt = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: false),
                    GeaendertAm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertVonId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MargenrechnerStaende", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MargenrechnerStaende_Mitarbeiter_GeaendertVonId",
                        column: x => x.GeaendertVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MargenrechnerStaende_GeaendertAm",
                table: "MargenrechnerStaende",
                column: "GeaendertAm");

            migrationBuilder.CreateIndex(
                name: "IX_MargenrechnerStaende_GeaendertVonId",
                table: "MargenrechnerStaende",
                column: "GeaendertVonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MargenrechnerStaende");
        }
    }
}
