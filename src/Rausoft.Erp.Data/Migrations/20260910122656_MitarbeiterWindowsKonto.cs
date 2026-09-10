using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class MitarbeiterWindowsKonto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Mitarbeiter_EntraObjectId",
                table: "Mitarbeiter");

            migrationBuilder.AlterColumn<Guid>(
                name: "EntraObjectId",
                table: "Mitarbeiter",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<string>(
                name: "WindowsKonto",
                table: "Mitarbeiter",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Mitarbeiter_EntraObjectId",
                table: "Mitarbeiter",
                column: "EntraObjectId",
                unique: true,
                filter: "[EntraObjectId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Mitarbeiter_WindowsKonto",
                table: "Mitarbeiter",
                column: "WindowsKonto",
                unique: true,
                filter: "[WindowsKonto] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Mitarbeiter_EntraObjectId",
                table: "Mitarbeiter");

            migrationBuilder.DropIndex(
                name: "IX_Mitarbeiter_WindowsKonto",
                table: "Mitarbeiter");

            migrationBuilder.DropColumn(
                name: "WindowsKonto",
                table: "Mitarbeiter");

            migrationBuilder.AlterColumn<Guid>(
                name: "EntraObjectId",
                table: "Mitarbeiter",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Mitarbeiter_EntraObjectId",
                table: "Mitarbeiter",
                column: "EntraObjectId",
                unique: true);
        }
    }
}
