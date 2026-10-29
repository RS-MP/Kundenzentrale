using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class WikiFaq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FaqEintraege",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Titel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Produkt = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Kategorie = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Symptom = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: false),
                    Ursache = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: true),
                    Loesung = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: false),
                    Schlagwoerter = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    KundeId = table.Column<int>(type: "int", nullable: true),
                    Vorkommen = table.Column<int>(type: "int", nullable: false),
                    ZuletztVorgekommenAm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TicketId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ErstelltAm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErstelltVonId = table.Column<int>(type: "int", nullable: true),
                    GeaendertAm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GeaendertVonId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaqEintraege", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaqEintraege_Kunden_KundeId",
                        column: x => x.KundeId,
                        principalTable: "Kunden",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FaqEintraege_Mitarbeiter_ErstelltVonId",
                        column: x => x.ErstelltVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FaqEintraege_Mitarbeiter_GeaendertVonId",
                        column: x => x.GeaendertVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FaqEintraege_ErstelltVonId",
                table: "FaqEintraege",
                column: "ErstelltVonId");

            migrationBuilder.CreateIndex(
                name: "IX_FaqEintraege_GeaendertVonId",
                table: "FaqEintraege",
                column: "GeaendertVonId");

            migrationBuilder.CreateIndex(
                name: "IX_FaqEintraege_KundeId",
                table: "FaqEintraege",
                column: "KundeId");

            migrationBuilder.CreateIndex(
                name: "IX_FaqEintraege_Produkt_Kategorie",
                table: "FaqEintraege",
                columns: new[] { "Produkt", "Kategorie" });

            migrationBuilder.CreateIndex(
                name: "IX_FaqEintraege_Status",
                table: "FaqEintraege",
                column: "Status");

            // Erster Fall (Anlass der FAQ, 29.10.2026) — freigegeben, ohne Ersteller;
            // per SQL statt HasData, damit die Identity-Spalte frei bleibt.
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [FaqEintraege] WHERE [Titel] = N'2FA lässt sich nicht einrichten – alter ID.prove-Eintrag in der Authenticator-App')
                INSERT INTO [FaqEintraege] ([Titel], [Produkt], [Kategorie], [Symptom], [Ursache], [Loesung], [Schlagwoerter], [Status], [Vorkommen], [ZuletztVorgekommenAm], [ErstelltAm])
                VALUES (
                    N'2FA lässt sich nicht einrichten – alter ID.prove-Eintrag in der Authenticator-App',
                    N'idprove-saas', N'anmeldung-2fa',
                    N'Der Kunde richtet die Zwei-Faktor-Authentifizierung ein, scannt den QR-Code und gibt den sechsstelligen Code aus seiner Authenticator-App ein. Der Code wird als ungültig abgelehnt, die Einrichtung lässt sich nicht abschließen – auch nach mehreren Versuchen.',
                    N'In der Authenticator-App des Kunden (z. B. Microsoft oder Google Authenticator) existiert bereits ein **älterer Eintrag zu ID.prove** – aus einer früheren Einrichtung, einer Testphase oder einem früheren Konto. Beim Scannen entsteht ein zweiter Eintrag mit gleichem Namen; der Kunde liest den Code aus dem **alten** Eintrag ab. Der gehört zu einem anderen Geheimnis und wird deshalb abgelehnt.',
                    N'1. Den Kunden fragen, ob in der Authenticator-App **mehrere Einträge zu ID.prove** stehen (gleicher Name, ggf. andere Kontobezeichnung).' + CHAR(10) +
                    N'2. Den **alten Eintrag löschen** lassen (in der App: Eintrag öffnen → Konto entfernen).' + CHAR(10) +
                    N'3. In ID.prove die 2FA-Einrichtung **neu starten** und den QR-Code erneut scannen.' + CHAR(10) +
                    N'4. Den Code aus dem **neu angelegten** Eintrag eingeben – die Einrichtung schließt ab.' + CHAR(10) + CHAR(10) +
                    N'Falls der Kunde nicht unterscheiden kann, welcher Eintrag neu ist: alle ID.prove-Einträge löschen und die Einrichtung einmal komplett neu durchlaufen.' + CHAR(10) +
                    N'Hinweis: Die Uhrzeit des Smartphones muss stimmen (automatisch stellen) – ein falsch gestellter Zeitgeber führt zum gleichen Symptom.',
                    N'2FA, Authenticator, QR-Code, Code ungültig, TOTP, Anmeldung',
                    1, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FaqEintraege");
        }
    }
}
