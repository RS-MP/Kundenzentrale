using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class VerkaufTextbausteineZahlungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bankumsaetze",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Fingerabdruck = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Auftragskonto = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Buchungstag = table.Column<DateOnly>(type: "date", nullable: false),
                    Valuta = table.Column<DateOnly>(type: "date", nullable: true),
                    Buchungstext = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Verwendungszweck = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Zahler = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Betrag = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Waehrung = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Datei = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ImportiertAm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Hinweis = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Rechnung = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    RechnungKunde = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    KundeId = table.Column<int>(type: "int", nullable: true),
                    Automatisch = table.Column<bool>(type: "bit", nullable: false),
                    ZugeordnetAm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ZugeordnetVonId = table.Column<int>(type: "int", nullable: true),
                    GebuchtAm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GebuchtZiel = table.Column<int>(type: "int", nullable: true),
                    GebuchtIn = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    OdooZahlungId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bankumsaetze", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bankumsaetze_Kunden_KundeId",
                        column: x => x.KundeId,
                        principalTable: "Kunden",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bankumsaetze_Mitarbeiter_ZugeordnetVonId",
                        column: x => x.ZugeordnetVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Textbausteine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Betreff = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    InhaltHtml = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: false),
                    Verwendung = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Hinweise = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: true),
                    Aktiv = table.Column<bool>(type: "bit", nullable: false),
                    OdooId = table.Column<int>(type: "int", nullable: true),
                    OdooModell = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OdooStand = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LokalGeaendert = table.Column<bool>(type: "bit", nullable: false),
                    GeaendertAm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertVonId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Textbausteine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Textbausteine_Mitarbeiter_GeaendertVonId",
                        column: x => x.GeaendertVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "VerkaufsMerker",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nummer = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AusgeblendetAm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AusgeblendetVonId = table.Column<int>(type: "int", nullable: true),
                    AusblendGrund = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NachfassAufgabeId = table.Column<int>(type: "int", nullable: true),
                    NachfassZuletztAm = table.Column<DateOnly>(type: "date", nullable: true),
                    NachfassAnzahl = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerkaufsMerker", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerkaufsMerker_Aufgaben_NachfassAufgabeId",
                        column: x => x.NachfassAufgabeId,
                        principalTable: "Aufgaben",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_VerkaufsMerker_Mitarbeiter_AusgeblendetVonId",
                        column: x => x.AusgeblendetVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bankumsaetze_Fingerabdruck",
                table: "Bankumsaetze",
                column: "Fingerabdruck",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bankumsaetze_KundeId",
                table: "Bankumsaetze",
                column: "KundeId");

            migrationBuilder.CreateIndex(
                name: "IX_Bankumsaetze_Status_Buchungstag",
                table: "Bankumsaetze",
                columns: new[] { "Status", "Buchungstag" });

            migrationBuilder.CreateIndex(
                name: "IX_Bankumsaetze_ZugeordnetVonId",
                table: "Bankumsaetze",
                column: "ZugeordnetVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Textbausteine_GeaendertVonId",
                table: "Textbausteine",
                column: "GeaendertVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Textbausteine_OdooId",
                table: "Textbausteine",
                column: "OdooId",
                unique: true,
                filter: "[OdooId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_VerkaufsMerker_AusgeblendetVonId",
                table: "VerkaufsMerker",
                column: "AusgeblendetVonId");

            migrationBuilder.CreateIndex(
                name: "IX_VerkaufsMerker_NachfassAufgabeId",
                table: "VerkaufsMerker",
                column: "NachfassAufgabeId");

            migrationBuilder.CreateIndex(
                name: "IX_VerkaufsMerker_Nummer",
                table: "VerkaufsMerker",
                column: "Nummer",
                unique: true);

            // Einstellungen per SQL (kein HasData mit festen IDs): offene Angebote und Kontoauszüge.
            // Geschrieben wird nach Odoo weiterhin nur über das Schreibziel (OdooTest.SchreibZiel, Standard „Aus").
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Verkauf.NachfassTage')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Verkauf.NachfassTage', N'7', N'Nach so vielen Tagen ohne Reaktion entsteht zu einem gesendeten Odoo-Angebot eine Nachfass-Aufgabe beim Verkäufer (0 = keine Aufgaben).');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Verkauf.NachfassWiederholungTage')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Verkauf.NachfassWiederholungTage', N'14', N'Ist die Nachfass-Aufgabe erledigt und das Angebot weiter offen, entsteht nach so vielen Tagen die nächste (0 = nur einmal nachfassen).');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Verkauf.RueckblickTage')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Verkauf.RueckblickTage', N'180', N'Wie weit die Übersicht offener Angebote und Aufträge zurückreicht (Tage ab Angebotsdatum).');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Verkauf.BerichtEmpfaenger')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Verkauf.BerichtEmpfaenger', N'', N'Empfänger der Wochenübersicht offener Angebote und Aufträge (E-Mail-Adressen, Komma-getrennt; nur erlaubte Domänen).');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Verkauf.BerichtTag')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Verkauf.BerichtTag', N'Montag', N'Wochentag der Wochenübersicht offener Angebote (Montag … Sonntag, täglich oder aus).');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Zahlungen.EingangVerzeichnis')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Zahlungen.EingangVerzeichnis', N'', N'Eingangsordner für Kontoauszüge (CSV-MT940). Dateien werden nach dem Einlesen in den Unterordner „verarbeitet“ verschoben. Zugriff per NTFS-Rechten auf Buchhaltung und Dienstkonto beschränken.');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Zahlungen.AutomatischBuchen')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Zahlungen.AutomatischBuchen', N'Ja', N'Eindeutig zugeordnete Zahlungseingänge ohne Rückfrage in Odoo buchen (Ja/Nein). Gebucht wird nur, wenn das Schreiben nach Odoo eingeschaltet ist.');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Zahlungen.OdooJournal')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Zahlungen.OdooJournal', N'', N'Name des Bankjournals in Odoo, auf das Zahlungen gebucht werden. Leer = das einzige Bankjournal; gibt es mehrere, muss der Name hier stehen.');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Zahlungen.Empfaenger')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Zahlungen.Empfaenger', N'', N'Empfänger der Meldung nach jedem eingelesenen Kontoauszug und der Übersicht ausstehender Zahlungen (E-Mail-Adressen, Komma-getrennt; nur erlaubte Domänen).');
                IF NOT EXISTS (SELECT 1 FROM [Einstellungen] WHERE [Schluessel] = N'Zahlungen.BerichtTag')
                    INSERT INTO [Einstellungen] ([Schluessel], [Wert], [Beschreibung])
                    VALUES (N'Zahlungen.BerichtTag', N'Montag', N'Wochentag der Übersicht ausstehender Zahlungen (Montag … Sonntag, täglich oder aus).');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM [Einstellungen] WHERE [Schluessel] LIKE N'Verkauf.%' OR [Schluessel] LIKE N'Zahlungen.%';
                """);

            migrationBuilder.DropTable(
                name: "Bankumsaetze");

            migrationBuilder.DropTable(
                name: "Textbausteine");

            migrationBuilder.DropTable(
                name: "VerkaufsMerker");
        }
    }
}
