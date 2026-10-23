using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class VerlaufStraenge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StrangName",
                table: "Aktivitaeten",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StrangSchluessel",
                table: "Aktivitaeten",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Aktivitaeten_KundeId_StrangSchluessel",
                table: "Aktivitaeten",
                columns: new[] { "KundeId", "StrangSchluessel" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Aktivitaeten_KundeId_StrangSchluessel",
                table: "Aktivitaeten");

            migrationBuilder.DropColumn(
                name: "StrangName",
                table: "Aktivitaeten");

            migrationBuilder.DropColumn(
                name: "StrangSchluessel",
                table: "Aktivitaeten");
        }
    }
}
