using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class Partnervertraege : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "KundeId",
                table: "Vertraege",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "PartnerId",
                table: "Vertraege",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vertraege_PartnerId",
                table: "Vertraege",
                column: "PartnerId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Vertraege_Partei",
                table: "Vertraege",
                sql: "([KundeId] IS NULL AND [PartnerId] IS NOT NULL) OR ([KundeId] IS NOT NULL AND [PartnerId] IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_Vertraege_Partner_PartnerId",
                table: "Vertraege",
                column: "PartnerId",
                principalTable: "Partner",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Vertraege_Partner_PartnerId",
                table: "Vertraege");

            migrationBuilder.DropIndex(
                name: "IX_Vertraege_PartnerId",
                table: "Vertraege");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Vertraege_Partei",
                table: "Vertraege");

            migrationBuilder.DropColumn(
                name: "PartnerId",
                table: "Vertraege");

            migrationBuilder.AlterColumn<int>(
                name: "KundeId",
                table: "Vertraege",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
