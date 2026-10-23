using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChatterAllesImportieren : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RelativerPfad",
                table: "Aktivitaetsanhaenge",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);

            migrationBuilder.AddColumn<string>(
                name: "FilterGrund",
                table: "Aktivitaeten",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OdooModell",
                table: "Aktivitaeten",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OdooResId",
                table: "Aktivitaeten",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Stufe",
                table: "Aktivitaeten",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Aktivitaeten_KundeId_OdooModell_OdooResId",
                table: "Aktivitaeten",
                columns: new[] { "KundeId", "OdooModell", "OdooResId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Aktivitaeten_KundeId_OdooModell_OdooResId",
                table: "Aktivitaeten");

            migrationBuilder.DropColumn(
                name: "FilterGrund",
                table: "Aktivitaeten");

            migrationBuilder.DropColumn(
                name: "OdooModell",
                table: "Aktivitaeten");

            migrationBuilder.DropColumn(
                name: "OdooResId",
                table: "Aktivitaeten");

            migrationBuilder.DropColumn(
                name: "Stufe",
                table: "Aktivitaeten");

            migrationBuilder.AlterColumn<string>(
                name: "RelativerPfad",
                table: "Aktivitaetsanhaenge",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);
        }
    }
}
