using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class PartnerAuchKunde : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "KundeId",
                table: "Partner",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Partner_KundeId",
                table: "Partner",
                column: "KundeId",
                unique: true,
                filter: "[KundeId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Partner_Kunden_KundeId",
                table: "Partner",
                column: "KundeId",
                principalTable: "Kunden",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Partner_Kunden_KundeId",
                table: "Partner");

            migrationBuilder.DropIndex(
                name: "IX_Partner_KundeId",
                table: "Partner");

            migrationBuilder.DropColumn(
                name: "KundeId",
                table: "Partner");
        }
    }
}
