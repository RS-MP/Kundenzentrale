using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AnfragenAusOdoo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Ergebnis",
                table: "Anfragen",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Msclkid",
                table: "Anfragen",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OdooLeadId",
                table: "Anfragen",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerlustGrund",
                table: "Anfragen",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Anfragen_OdooLeadId",
                table: "Anfragen",
                column: "OdooLeadId",
                unique: true,
                filter: "[OdooLeadId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Anfragen_OdooLeadId",
                table: "Anfragen");

            migrationBuilder.DropColumn(
                name: "Ergebnis",
                table: "Anfragen");

            migrationBuilder.DropColumn(
                name: "Msclkid",
                table: "Anfragen");

            migrationBuilder.DropColumn(
                name: "OdooLeadId",
                table: "Anfragen");

            migrationBuilder.DropColumn(
                name: "VerlustGrund",
                table: "Anfragen");
        }
    }
}
