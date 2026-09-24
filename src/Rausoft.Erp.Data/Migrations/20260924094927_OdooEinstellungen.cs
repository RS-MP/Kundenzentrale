using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class OdooEinstellungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Einstellungen",
                columns: new[] { "Id", "Beschreibung", "Schluessel", "Wert" },
                values: new object[,]
                {
                    { 7, "Basis-URL der Odoo-Instanz für den Import, z. B. https://rausoft.odoo.com.", "Odoo.Url", "" },
                    { 8, "Name der Odoo-Datenbank.", "Odoo.Datenbank", "" },
                    { 9, "Login (E-Mail) des Odoo-API-Benutzers — idealerweise ein eigener, nur lesend berechtigter Benutzer. Der API-Key wird als Geheimnis verschlüsselt gespeichert (nie im Klartext).", "Odoo.Benutzer", "" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Einstellungen",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "Einstellungen",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "Einstellungen",
                keyColumn: "Id",
                keyValue: 9);
        }
    }
}
