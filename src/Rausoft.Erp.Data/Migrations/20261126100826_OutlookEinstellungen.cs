using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class OutlookEinstellungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // App-Registrierung der Outlook-Anbindung (Entra ID) — öffentlicher Client, keine Geheimnisse.
            // Einstellungen per SQL (kein HasData mit festen IDs).
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Outlook.TenantId')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Outlook.TenantId', N'', N'Verzeichnis-ID (Mandant) der App-Registrierung in Entra ID für die Outlook-Anbindung.');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Outlook.ClientId')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Outlook.ClientId', N'', N'Anwendungs-ID (Client) der App-Registrierung in Entra ID für die Outlook-Anbindung.');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [Einstellungen] WHERE [Schluessel] LIKE N'Outlook.%';");
        }
    }
}
