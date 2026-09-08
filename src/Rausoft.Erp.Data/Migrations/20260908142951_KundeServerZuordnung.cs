using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class KundeServerZuordnung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MandantenKennung",
                table: "Kunden",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ServerId",
                table: "Kunden",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Kunden_ServerId",
                table: "Kunden",
                column: "ServerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Kunden_Server_ServerId",
                table: "Kunden",
                column: "ServerId",
                principalTable: "Server",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Kunden_Server_ServerId",
                table: "Kunden");

            migrationBuilder.DropIndex(
                name: "IX_Kunden_ServerId",
                table: "Kunden");

            migrationBuilder.DropColumn(
                name: "MandantenKennung",
                table: "Kunden");

            migrationBuilder.DropColumn(
                name: "ServerId",
                table: "Kunden");
        }
    }
}
