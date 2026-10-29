using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class VorgangRowVersionUndVerweisIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Vorgaenge",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Aktivitaeten_VerweisTyp_VerweisId",
                table: "Aktivitaeten",
                columns: new[] { "VerweisTyp", "VerweisId" },
                filter: "[VerweisId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Aktivitaeten_VerweisTyp_VerweisId",
                table: "Aktivitaeten");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Vorgaenge");
        }
    }
}
