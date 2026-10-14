using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class WikiAbschnittReihenfolge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Abschnitte",
                table: "WikiThemaInfos",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Abschnitte",
                table: "WikiThemaInfos");
        }
    }
}
