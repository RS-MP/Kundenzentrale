using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class VertraulicheBestandteile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kurzinfo",
                table: "Vertragsbestandteile",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Vertraulich",
                table: "Vertragsbestandteile",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Einstellung per SQL statt HasData (feste Seed-IDs kollidieren mit Laufzeit-Zeilen)
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'VertraulicheAblage.Verzeichnis')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'VertraulicheAblage.Verzeichnis', N'',
                            N'Separates Verzeichnis für vertrauliche Verträge (Sondervereinbarungen, Partnerverträge). Dokumente daraus sind nur mit der Rolle „Vertrauliche Verträge“ abrufbar; NTFS-Rechte auf dem Ordner zusätzlich einschränken.');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [Einstellungen] WHERE [Schluessel] = N'VertraulicheAblage.Verzeichnis';");

            migrationBuilder.DropColumn(
                name: "Kurzinfo",
                table: "Vertragsbestandteile");

            migrationBuilder.DropColumn(
                name: "Vertraulich",
                table: "Vertragsbestandteile");
        }
    }
}
