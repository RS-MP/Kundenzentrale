using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class ImportFelder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UStIdNr",
                table: "Kunden",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OdooId",
                table: "Adressen",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Adressen_OdooId",
                table: "Adressen",
                column: "OdooId",
                unique: true,
                filter: "[OdooId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Adressen_OdooId",
                table: "Adressen");

            migrationBuilder.DropColumn(
                name: "UStIdNr",
                table: "Kunden");

            migrationBuilder.DropColumn(
                name: "OdooId",
                table: "Adressen");
        }
    }
}
