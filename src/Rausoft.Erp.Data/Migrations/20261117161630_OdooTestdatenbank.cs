using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class OdooTestdatenbank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Odoo-Testdatenbank (wechselt alle 14 Tage) und Ziel schreibender Odoo-Funktionen —
            // Einstellungen per SQL (kein HasData mit festen IDs). Der API-Key der Testdatenbank
            // liegt verschlüsselt bei den Geheimnissen (OdooTest.ApiKey).
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'OdooTest.Url')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'OdooTest.Url', N'', N'Adresse der Odoo-Testdatenbank (https). Wird unter Administration → Odoo-Testdatenbank gepflegt.');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'OdooTest.Datenbank')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'OdooTest.Datenbank', N'', N'Name der Odoo-Testdatenbank.');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'OdooTest.Benutzer')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'OdooTest.Benutzer', N'', N'API-Benutzer (Login) der Odoo-Testdatenbank.');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'OdooTest.GueltigBis')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'OdooTest.GueltigBis', N'', N'Tag, bis zu dem die Odoo-Testdatenbank gültig ist (JJJJ-MM-TT). Danach wird nicht mehr in sie geschrieben.');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'OdooTest.SchreibZiel')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'OdooTest.SchreibZiel', N'Aus', N'Wohin schreibende Odoo-Funktionen gehen: Aus, Test (Odoo-Testdatenbank) oder Produktiv. Lesen und Importe nutzen immer die Produktiv-Anbindung.');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM [Einstellungen] WHERE [Schluessel] IN
                    (N'OdooTest.Url', N'OdooTest.Datenbank', N'OdooTest.Benutzer', N'OdooTest.GueltigBis', N'OdooTest.SchreibZiel');
                DELETE FROM [GeheimeEinstellungen] WHERE [Schluessel] = N'OdooTest.ApiKey';
                """);
        }
    }
}
