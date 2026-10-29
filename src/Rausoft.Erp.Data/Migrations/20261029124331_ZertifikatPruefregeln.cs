using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class ZertifikatPruefregeln : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Schutzregeln der Zertifikats-Live-Prüfung (SICHERHEIT.md N1) — Einstellungen per SQL
            // (kein HasData mit festen IDs — siehe Hinweis im DbContext)
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Zertifikate.ErlaubtePorts')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Zertifikate.ErlaubtePorts', N'443, 8443', N'Ports, zu denen die Live-Prüfung der SSL-Zertifikate verbinden darf (mehrere mit Komma; leer = 443 und 8443). Verhindert, dass die Prüfung zum Abtasten des Netzes benutzt wird.');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Zertifikate.EmpfaengerDomaenen')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Zertifikate.EmpfaengerDomaenen', N'', N'Domänen, an die Zertifikats-Erinnerungen und Statusmails gehen dürfen (mehrere mit Komma; leer = Domäne der Absenderadresse). Adressen außerhalb werden abgelehnt bzw. beim Versand übersprungen.');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM [Einstellungen] WHERE [Schluessel] IN (N'Zertifikate.ErlaubtePorts', N'Zertifikate.EmpfaengerDomaenen');");
        }
    }
}
