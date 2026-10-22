using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChatterImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AutorName",
                table: "Aktivitaeten",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Bezug",
                table: "Aktivitaeten",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OdooId",
                table: "Aktivitaeten",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Quelle",
                table: "Aktivitaeten",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Aktivitaetsanhaenge",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AktivitaetId = table.Column<int>(type: "int", nullable: false),
                    Dateiname = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    MimeType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Groesse = table.Column<long>(type: "bigint", nullable: false),
                    RelativerPfad = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OdooId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Aktivitaetsanhaenge", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Aktivitaetsanhaenge_Aktivitaeten_AktivitaetId",
                        column: x => x.AktivitaetId,
                        principalTable: "Aktivitaeten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Aktivitaeten_KundeId_Quelle",
                table: "Aktivitaeten",
                columns: new[] { "KundeId", "Quelle" });

            migrationBuilder.CreateIndex(
                name: "IX_Aktivitaeten_OdooId",
                table: "Aktivitaeten",
                column: "OdooId",
                unique: true,
                filter: "[OdooId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Aktivitaetsanhaenge_AktivitaetId",
                table: "Aktivitaetsanhaenge",
                column: "AktivitaetId");

            migrationBuilder.CreateIndex(
                name: "IX_Aktivitaetsanhaenge_OdooId",
                table: "Aktivitaetsanhaenge",
                column: "OdooId",
                unique: true,
                filter: "[OdooId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Aktivitaetsanhaenge");

            migrationBuilder.DropIndex(
                name: "IX_Aktivitaeten_KundeId_Quelle",
                table: "Aktivitaeten");

            migrationBuilder.DropIndex(
                name: "IX_Aktivitaeten_OdooId",
                table: "Aktivitaeten");

            migrationBuilder.DropColumn(
                name: "AutorName",
                table: "Aktivitaeten");

            migrationBuilder.DropColumn(
                name: "Bezug",
                table: "Aktivitaeten");

            migrationBuilder.DropColumn(
                name: "OdooId",
                table: "Aktivitaeten");

            migrationBuilder.DropColumn(
                name: "Quelle",
                table: "Aktivitaeten");
        }
    }
}
