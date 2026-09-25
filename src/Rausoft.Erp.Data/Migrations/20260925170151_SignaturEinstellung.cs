using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <summary>
    /// Einstellung fürs PDF-Signatur-Zertifikat — per SQL statt HasData
    /// (Laufzeit-Zeilen belegen die Identity-Werte, siehe OdooEinstellungen).
    /// </summary>
    public partial class SignaturEinstellung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Vertragserzeugung.SignaturZertifikat')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Vertragserzeugung.SignaturZertifikat', N'',
                            N'Pfad zur PFX-Zertifikatsdatei für die digitale Signatur erzeugter Vertrags-PDFs (leer = ohne Signatur). Das PFX-Passwort wird als Geheimnis verschlüsselt gespeichert.');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM [Einstellungen] WHERE [Schluessel] = N'Vertragserzeugung.SignaturZertifikat';");
        }
    }
}
