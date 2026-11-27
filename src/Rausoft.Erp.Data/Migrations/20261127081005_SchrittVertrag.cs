using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class SchrittVertrag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VertragId",
                table: "Aufgaben",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Aufgaben_VertragId",
                table: "Aufgaben",
                column: "VertragId");

            migrationBuilder.AddForeignKey(
                name: "FK_Aufgaben_Vertraege_VertragId",
                table: "Aufgaben",
                column: "VertragId",
                principalTable: "Vertraege",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Aufgaben_Vertraege_VertragId",
                table: "Aufgaben");

            migrationBuilder.DropIndex(
                name: "IX_Aufgaben_VertragId",
                table: "Aufgaben");

            migrationBuilder.DropColumn(
                name: "VertragId",
                table: "Aufgaben");
        }
    }
}
