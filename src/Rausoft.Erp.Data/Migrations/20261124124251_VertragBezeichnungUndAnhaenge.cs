using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class VertragBezeichnungUndAnhaenge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Bezeichnung",
                table: "Vertraege",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HauptvertragId",
                table: "Vertraege",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vertraege_HauptvertragId",
                table: "Vertraege",
                column: "HauptvertragId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Vertraege_KeinSelbstanhang",
                table: "Vertraege",
                sql: "[HauptvertragId] IS NULL OR [HauptvertragId] <> [Id]");

            migrationBuilder.AddForeignKey(
                name: "FK_Vertraege_Vertraege_HauptvertragId",
                table: "Vertraege",
                column: "HauptvertragId",
                principalTable: "Vertraege",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Einmalige Vorbelegung: Verträge aus einer Vorlage mit genau einem, nicht
            // vertraulichen Bestandteil bekommen dessen Titel (= Vorlagenname) als
            // Bezeichnung. Alles andere bleibt leer und wird von Hand benannt.
            migrationBuilder.Sql("""
                UPDATE v
                SET v.Bezeichnung = b.Titel
                FROM Vertraege v
                JOIN Vertragsbestandteile b ON b.VertragId = v.Id
                WHERE v.Bezeichnung IS NULL
                  AND v.Notiz LIKE N'Aus Vorlage %'
                  AND b.Vertraulich = 0
                  AND (SELECT COUNT(*) FROM Vertragsbestandteile x WHERE x.VertragId = v.Id) = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Vertraege_Vertraege_HauptvertragId",
                table: "Vertraege");

            migrationBuilder.DropIndex(
                name: "IX_Vertraege_HauptvertragId",
                table: "Vertraege");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Vertraege_KeinSelbstanhang",
                table: "Vertraege");

            migrationBuilder.DropColumn(
                name: "Bezeichnung",
                table: "Vertraege");

            migrationBuilder.DropColumn(
                name: "HauptvertragId",
                table: "Vertraege");
        }
    }
}
