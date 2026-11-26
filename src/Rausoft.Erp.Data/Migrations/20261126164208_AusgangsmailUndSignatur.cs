using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AusgangsmailUndSignatur : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MailSignaturHtml",
                table: "Mitarbeiter",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Ausgangsmails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KundeId = table.Column<int>(type: "int", nullable: false),
                    MitarbeiterId = table.Column<int>(type: "int", nullable: false),
                    AnsprechpartnerId = table.Column<int>(type: "int", nullable: true),
                    An = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Cc = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Betreff = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    InhaltHtml = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: false),
                    Anhaenge = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    TextbausteinId = table.Column<int>(type: "int", nullable: true),
                    Kanal = table.Column<int>(type: "int", nullable: false),
                    GraphId = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NachrichtenKennung = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AngelegtAm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GesendetAm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AktivitaetId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ausgangsmails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ausgangsmails_Aktivitaeten_AktivitaetId",
                        column: x => x.AktivitaetId,
                        principalTable: "Aktivitaeten",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Ausgangsmails_Kunden_KundeId",
                        column: x => x.KundeId,
                        principalTable: "Kunden",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Ausgangsmails_Mitarbeiter_MitarbeiterId",
                        column: x => x.MitarbeiterId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ausgangsmails_AktivitaetId",
                table: "Ausgangsmails",
                column: "AktivitaetId");

            migrationBuilder.CreateIndex(
                name: "IX_Ausgangsmails_KundeId",
                table: "Ausgangsmails",
                column: "KundeId");

            migrationBuilder.CreateIndex(
                name: "IX_Ausgangsmails_MitarbeiterId_Status",
                table: "Ausgangsmails",
                columns: new[] { "MitarbeiterId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Ausgangsmails");

            migrationBuilder.DropColumn(
                name: "MailSignaturHtml",
                table: "Mitarbeiter");
        }
    }
}
