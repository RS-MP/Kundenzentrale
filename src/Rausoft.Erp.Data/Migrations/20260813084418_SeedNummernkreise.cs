using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedNummernkreise : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Nummernkreise",
                columns: new[] { "Id", "Format", "NaechsterWert", "Praefix", "Schluessel" },
                values: new object[,]
                {
                    { 1, "D5", 1, "K", "Kunde" },
                    { 2, "D5", 1, "V", "Vertrag" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Nummernkreise",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "Nummernkreise",
                keyColumn: "Id",
                keyValue: 2);
        }
    }
}
