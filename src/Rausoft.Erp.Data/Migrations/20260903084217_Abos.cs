using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class Abos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Abos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KundeId = table.Column<int>(type: "int", nullable: false),
                    Nummer = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IntervallMonate = table.Column<int>(type: "int", nullable: false),
                    Beginn = table.Column<DateOnly>(type: "date", nullable: true),
                    LaufzeitBeginn = table.Column<DateOnly>(type: "date", nullable: true),
                    NaechsteVerlaengerung = table.Column<DateOnly>(type: "date", nullable: true),
                    Ende = table.Column<DateOnly>(type: "date", nullable: true),
                    OdooId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Abos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Abos_Kunden_KundeId",
                        column: x => x.KundeId,
                        principalTable: "Kunden",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AboPositionen",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AboId = table.Column<int>(type: "int", nullable: false),
                    Produkt = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Menge = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AboPositionen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AboPositionen_Abos_AboId",
                        column: x => x.AboId,
                        principalTable: "Abos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AboPositionen_AboId",
                table: "AboPositionen",
                column: "AboId");

            migrationBuilder.CreateIndex(
                name: "IX_Abos_KundeId",
                table: "Abos",
                column: "KundeId");

            migrationBuilder.CreateIndex(
                name: "IX_Abos_OdooId",
                table: "Abos",
                column: "OdooId",
                unique: true,
                filter: "[OdooId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AboPositionen");

            migrationBuilder.DropTable(
                name: "Abos");
        }
    }
}
