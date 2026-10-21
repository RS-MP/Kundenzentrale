using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class Zertifikate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Einstellungen per SQL (kein HasData mit festen IDs — siehe Hinweis im DbContext)
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Mail.SmtpHost')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Mail.SmtpHost', N'', N'SMTP-Server für Benachrichtigungen der Kundenzentrale (z. B. smtp.ionos.de). Leer = E-Mail-Versand aus.');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Mail.SmtpPort')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Mail.SmtpPort', N'587', N'SMTP-Port: 587 (StartTLS), 465 (SSL) oder 25 (ohne Verschlüsselung, nur intern).');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Mail.SmtpBenutzer')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Mail.SmtpBenutzer', N'', N'Anmeldename am SMTP-Server (meist die Absenderadresse). Leer = ohne Anmeldung (internes Relay). Das Passwort liegt verschlüsselt als Geheimnis Mail.SmtpPasswort.');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Mail.SmtpSsl')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Mail.SmtpSsl', N'true', N'Verschlüsselte Verbindung zum SMTP-Server (true/false).');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Mail.Absender')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Mail.Absender', N'', N'Absenderadresse der Kundenzentrale, z. B. kundenzentrale@rausoft.de.');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Zertifikate.ErinnerungIntervallTage')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Zertifikate.ErinnerungIntervallTage', N'1', N'Eine fällige Zertifikats-Erinnerung wird alle n Tage wiederholt, bis das neue Zertifikat bestätigt wurde.');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Zertifikate.StandardEmpfaenger')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Zertifikate.StandardEmpfaenger', N'', N'E-Mail-Empfänger der Zertifikats-Erinnerungen, wenn am Zertifikat keine eingetragen sind (mehrere mit Komma).');
                """);

            migrationBuilder.CreateTable(
                name: "Zertifikate",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Bezeichnung = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Hostname = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Port = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: true),
                    Anbieter = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LaufzeitMonate = table.Column<int>(type: "int", nullable: false),
                    GueltigBis = table.Column<DateOnly>(type: "date", nullable: false),
                    VorlaufTage = table.Column<int>(type: "int", nullable: false),
                    Empfaenger = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Notiz = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Aktiv = table.Column<bool>(type: "bit", nullable: false),
                    ErneuertAm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErneuertVonId = table.Column<int>(type: "int", nullable: true),
                    LetzteErinnerungAm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErinnerungenGesendet = table.Column<int>(type: "int", nullable: false),
                    LetzterMailFehler = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LiveGeprueftAm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LiveGueltigBis = table.Column<DateOnly>(type: "date", nullable: true),
                    LiveAussteller = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    LiveFehler = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Zertifikate", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Zertifikate_Mitarbeiter_ErneuertVonId",
                        column: x => x.ErneuertVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Zertifikate_Server_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Server",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Zertifikate_ErneuertVonId",
                table: "Zertifikate",
                column: "ErneuertVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Zertifikate_ServerId",
                table: "Zertifikate",
                column: "ServerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM [Einstellungen] WHERE [Schluessel] IN (N'Mail.SmtpHost', N'Mail.SmtpPort', N'Mail.SmtpBenutzer', N'Mail.SmtpSsl', N'Mail.Absender', N'Zertifikate.ErinnerungIntervallTage', N'Zertifikate.StandardEmpfaenger');");
            migrationBuilder.DropTable(
                name: "Zertifikate");
        }
    }
}
