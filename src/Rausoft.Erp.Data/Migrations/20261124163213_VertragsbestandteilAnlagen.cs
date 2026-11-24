using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class VertragsbestandteilAnlagen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GehoertZuId",
                table: "Vertragsbestandteile",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vertragsbestandteile_GehoertZuId",
                table: "Vertragsbestandteile",
                column: "GehoertZuId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Vertragsbestandteile_KeineSelbstanlage",
                table: "Vertragsbestandteile",
                sql: "[GehoertZuId] IS NULL OR [GehoertZuId] <> [Id]");

            migrationBuilder.AddForeignKey(
                name: "FK_Vertragsbestandteile_Vertragsbestandteile_GehoertZuId",
                table: "Vertragsbestandteile",
                column: "GehoertZuId",
                principalTable: "Vertragsbestandteile",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Vertragsbestandteile_Vertragsbestandteile_GehoertZuId",
                table: "Vertragsbestandteile");

            migrationBuilder.DropIndex(
                name: "IX_Vertragsbestandteile_GehoertZuId",
                table: "Vertragsbestandteile");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Vertragsbestandteile_KeineSelbstanlage",
                table: "Vertragsbestandteile");

            migrationBuilder.DropColumn(
                name: "GehoertZuId",
                table: "Vertragsbestandteile");
        }
    }
}
