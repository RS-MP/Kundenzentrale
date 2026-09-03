using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class MerkmalStammliste : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Merkmale",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Aktiv = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Merkmale", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Merkmale",
                columns: new[] { "Id", "Aktiv", "Name" },
                values: new object[,]
                {
                    { 1, true, "Insolvenz" },
                    { 2, true, "Einmalkauf" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Merkmale_Name",
                table: "Merkmale",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Merkmale");
        }
    }
}
