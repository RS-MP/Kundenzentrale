using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialesSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Zeitpunkt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MitarbeiterId = table.Column<int>(type: "int", nullable: true),
                    Entitaet = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntitaetId = table.Column<int>(type: "int", nullable: false),
                    Aktion = table.Column<int>(type: "int", nullable: false),
                    AenderungenJson = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Bereiche",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Aktiv = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bereiche", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Nummernkreise",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Schluessel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Praefix = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    NaechsterWert = table.Column<int>(type: "int", nullable: false),
                    Format = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nummernkreise", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Partner",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Strasse = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Plz = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Ort = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Land = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Telefon = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Provisionsmodell = table.Column<int>(type: "int", nullable: false),
                    ProvisionProzent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    OdooId = table.Column<int>(type: "int", nullable: true),
                    Aktiv = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Partner", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Mitarbeiter",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntraObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Anzeigename = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BereichId = table.Column<int>(type: "int", nullable: true),
                    Rollen = table.Column<int>(type: "int", nullable: false),
                    Aktiv = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mitarbeiter", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Mitarbeiter_Bereiche_BereichId",
                        column: x => x.BereichId,
                        principalTable: "Bereiche",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Kunden",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kundennummer = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Firmenname = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kundengruppe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Betriebsart = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    VermitteltDurchPartnerId = table.Column<int>(type: "int", nullable: true),
                    RechnungsempfaengerPartnerId = table.Column<int>(type: "int", nullable: true),
                    OdooId = table.Column<int>(type: "int", nullable: true),
                    Notiz = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kunden", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Kunden_Partner_RechnungsempfaengerPartnerId",
                        column: x => x.RechnungsempfaengerPartnerId,
                        principalTable: "Partner",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Kunden_Partner_VermitteltDurchPartnerId",
                        column: x => x.VermitteltDurchPartnerId,
                        principalTable: "Partner",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Adressen",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KundeId = table.Column<int>(type: "int", nullable: false),
                    Typ = table.Column<int>(type: "int", nullable: false),
                    Zusatz = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Strasse = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Plz = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Ort = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Land = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: false),
                    Aktiv = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Adressen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Adressen_Kunden_KundeId",
                        column: x => x.KundeId,
                        principalTable: "Kunden",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Ansprechpartner",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KundeId = table.Column<int>(type: "int", nullable: false),
                    Anrede = table.Column<int>(type: "int", nullable: false),
                    Titel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Vorname = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Nachname = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Position = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TelefonFestnetz = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TelefonMobil = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Rollen = table.Column<int>(type: "int", nullable: false),
                    Aktiv = table.Column<bool>(type: "bit", nullable: false),
                    OdooId = table.Column<int>(type: "int", nullable: true),
                    Notiz = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ansprechpartner", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ansprechpartner_Kunden_KundeId",
                        column: x => x.KundeId,
                        principalTable: "Kunden",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Vertraege",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Vertragsnummer = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    KundeId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Beginn = table.Column<DateOnly>(type: "date", nullable: false),
                    MindestlaufzeitMonate = table.Column<int>(type: "int", nullable: true),
                    VerlaengerungMonate = table.Column<int>(type: "int", nullable: true),
                    KuendigungsfristMonate = table.Column<int>(type: "int", nullable: true),
                    GekuendigtZum = table.Column<DateOnly>(type: "date", nullable: true),
                    Notiz = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: true),
                    OdooId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vertraege", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vertraege_Kunden_KundeId",
                        column: x => x.KundeId,
                        principalTable: "Kunden",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Aktivitaeten",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KundeId = table.Column<int>(type: "int", nullable: false),
                    Typ = table.Column<int>(type: "int", nullable: false),
                    Zeitpunkt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MitarbeiterId = table.Column<int>(type: "int", nullable: true),
                    AnsprechpartnerId = table.Column<int>(type: "int", nullable: true),
                    Richtung = table.Column<int>(type: "int", nullable: true),
                    Betreff = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Inhalt = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: true),
                    VerweisTyp = table.Column<int>(type: "int", nullable: true),
                    VerweisId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Aktivitaeten", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Aktivitaeten_Ansprechpartner_AnsprechpartnerId",
                        column: x => x.AnsprechpartnerId,
                        principalTable: "Ansprechpartner",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Aktivitaeten_Kunden_KundeId",
                        column: x => x.KundeId,
                        principalTable: "Kunden",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Aktivitaeten_Mitarbeiter_MitarbeiterId",
                        column: x => x.MitarbeiterId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Vertragsbestandteile",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VertragId = table.Column<int>(type: "int", nullable: false),
                    Typ = table.Column<int>(type: "int", nullable: false),
                    Titel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Beschreibung = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: true),
                    UnterzeichnetAm = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vertragsbestandteile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vertragsbestandteile_Vertraege_VertragId",
                        column: x => x.VertragId,
                        principalTable: "Vertraege",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Vertragsdokumente",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VertragsbestandteilId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Dateipfad = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DateinameOriginal = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Sha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ErstelltAm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErstelltVonId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vertragsdokumente", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vertragsdokumente_Mitarbeiter_ErstelltVonId",
                        column: x => x.ErstelltVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vertragsdokumente_Vertragsbestandteile_VertragsbestandteilId",
                        column: x => x.VertragsbestandteilId,
                        principalTable: "Vertragsbestandteile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Adressen_KundeId",
                table: "Adressen",
                column: "KundeId");

            migrationBuilder.CreateIndex(
                name: "IX_Aktivitaeten_AnsprechpartnerId",
                table: "Aktivitaeten",
                column: "AnsprechpartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Aktivitaeten_KundeId_Zeitpunkt",
                table: "Aktivitaeten",
                columns: new[] { "KundeId", "Zeitpunkt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Aktivitaeten_MitarbeiterId",
                table: "Aktivitaeten",
                column: "MitarbeiterId");

            migrationBuilder.CreateIndex(
                name: "IX_Ansprechpartner_Email",
                table: "Ansprechpartner",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Ansprechpartner_KundeId",
                table: "Ansprechpartner",
                column: "KundeId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_Entitaet_EntitaetId",
                table: "AuditLog",
                columns: new[] { "Entitaet", "EntitaetId" });

            migrationBuilder.CreateIndex(
                name: "IX_Bereiche_Name",
                table: "Bereiche",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Kunden_Kundennummer",
                table: "Kunden",
                column: "Kundennummer",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Kunden_OdooId",
                table: "Kunden",
                column: "OdooId",
                unique: true,
                filter: "[OdooId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Kunden_RechnungsempfaengerPartnerId",
                table: "Kunden",
                column: "RechnungsempfaengerPartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Kunden_VermitteltDurchPartnerId",
                table: "Kunden",
                column: "VermitteltDurchPartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Mitarbeiter_BereichId",
                table: "Mitarbeiter",
                column: "BereichId");

            migrationBuilder.CreateIndex(
                name: "IX_Mitarbeiter_Email",
                table: "Mitarbeiter",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Mitarbeiter_EntraObjectId",
                table: "Mitarbeiter",
                column: "EntraObjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Nummernkreise_Schluessel",
                table: "Nummernkreise",
                column: "Schluessel",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Partner_OdooId",
                table: "Partner",
                column: "OdooId",
                unique: true,
                filter: "[OdooId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Vertraege_KundeId",
                table: "Vertraege",
                column: "KundeId");

            migrationBuilder.CreateIndex(
                name: "IX_Vertraege_OdooId",
                table: "Vertraege",
                column: "OdooId",
                unique: true,
                filter: "[OdooId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Vertraege_Vertragsnummer",
                table: "Vertraege",
                column: "Vertragsnummer",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vertragsbestandteile_VertragId",
                table: "Vertragsbestandteile",
                column: "VertragId");

            migrationBuilder.CreateIndex(
                name: "IX_Vertragsdokumente_ErstelltVonId",
                table: "Vertragsdokumente",
                column: "ErstelltVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Vertragsdokumente_VertragsbestandteilId_Version",
                table: "Vertragsdokumente",
                columns: new[] { "VertragsbestandteilId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Adressen");

            migrationBuilder.DropTable(
                name: "Aktivitaeten");

            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "Nummernkreise");

            migrationBuilder.DropTable(
                name: "Vertragsdokumente");

            migrationBuilder.DropTable(
                name: "Ansprechpartner");

            migrationBuilder.DropTable(
                name: "Mitarbeiter");

            migrationBuilder.DropTable(
                name: "Vertragsbestandteile");

            migrationBuilder.DropTable(
                name: "Bereiche");

            migrationBuilder.DropTable(
                name: "Vertraege");

            migrationBuilder.DropTable(
                name: "Kunden");

            migrationBuilder.DropTable(
                name: "Partner");
        }
    }
}
