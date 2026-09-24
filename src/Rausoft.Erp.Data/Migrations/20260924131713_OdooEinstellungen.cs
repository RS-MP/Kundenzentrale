using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <summary>
    /// Odoo-Einstellungen bewusst per SQL statt HasData: Die Laufzeit legt in
    /// Einstellungen eigene Zeilen an (z. B. ServicePortal.LetzterPush) und
    /// belegt Identity-Werte — Seeds mit festen IDs kollidieren dann (PK-
    /// Verletzung auf der Produktiv-DB, 24.09.2026). Einfügen ohne feste Id,
    /// idempotent über den Schlüssel.
    /// </summary>
    public partial class OdooEinstellungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Odoo.Url')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Odoo.Url', N'', N'Basis-URL der Odoo-Instanz für den Import, z. B. https://rausoft.odoo.com.');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Odoo.Datenbank')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Odoo.Datenbank', N'', N'Name der Odoo-Datenbank.');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Odoo.Benutzer')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Odoo.Benutzer', N'', N'Login (E-Mail) des Odoo-API-Benutzers — idealerweise ein eigener, nur lesend berechtigter Benutzer. Der API-Key wird als Geheimnis verschlüsselt gespeichert (nie im Klartext).');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM [Einstellungen] WHERE [Schluessel] IN (N'Odoo.Url', N'Odoo.Datenbank', N'Odoo.Benutzer');");
        }
    }
}
