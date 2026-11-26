using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class KundenmailsFreigabe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Schalter für Mails aus der Kundenakte (MP 26.11.2026): Startet gesperrt — jede Mail geht
            // an den Mitarbeiter selbst, bis ein Administrator Kundenmails freigibt. So lässt sich
            // auf dem Produktivsystem testen, ohne dass ein Kunde Post bekommt.
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Outlook.KundenmailsFreigegeben')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Outlook.KundenmailsFreigegeben', N'Nein', N'Ja = Mails aus der Kundenakte gehen an den Kunden. Nein = Probebetrieb: Jede Mail geht an den Mitarbeiter selbst.');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [Einstellungen] WHERE [Schluessel] = N'Outlook.KundenmailsFreigegeben';");
        }
    }
}
