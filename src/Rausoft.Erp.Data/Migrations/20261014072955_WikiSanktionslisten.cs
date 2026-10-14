using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class WikiSanktionslisten : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sanktionslisten",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Region = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Kuerzel = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    BezeichnungIntern = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Aliase = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Beschreibung = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: true),
                    Art = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Herausgeber = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Geltungsbereich = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Standardumfang = table.Column<bool>(type: "bit", nullable: false),
                    DownloadUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Import = table.Column<int>(type: "int", nullable: false),
                    ImportHinweis = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VerantwortlichId = table.Column<int>(type: "int", nullable: true),
                    VertreterId = table.Column<int>(type: "int", nullable: true),
                    Prozess = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: true),
                    Aktiv = table.Column<bool>(type: "bit", nullable: false),
                    GeaendertAm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GeaendertVonId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sanktionslisten", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sanktionslisten_Mitarbeiter_GeaendertVonId",
                        column: x => x.GeaendertVonId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Sanktionslisten_Mitarbeiter_VerantwortlichId",
                        column: x => x.VerantwortlichId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Sanktionslisten_Mitarbeiter_VertreterId",
                        column: x => x.VertreterId,
                        principalTable: "Mitarbeiter",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sanktionslisten_GeaendertVonId",
                table: "Sanktionslisten",
                column: "GeaendertVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Sanktionslisten_Region_Name",
                table: "Sanktionslisten",
                columns: new[] { "Region", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sanktionslisten_VerantwortlichId",
                table: "Sanktionslisten",
                column: "VerantwortlichId");

            migrationBuilder.CreateIndex(
                name: "IX_Sanktionslisten_VertreterId",
                table: "Sanktionslisten",
                column: "VertreterId");

            // Startbestand: Sanktionslisten-Übersicht von sanktionslisten.de (Stand 16.12.2025).
            // Fakten (Name, Art, Herausgeber, Geltungsbereich, Link) übernommen, Beschreibungen
            // als eigene Kurzfassung; Bezeichnung intern, Import und Zuständigkeiten pflegt rausoft.
            (string Region, string Kuerzel, string Name, string Aliase, string Beschreibung, string Art,
                string Herausgeber, string Geltungsbereich, bool Standard, string Url)[] listen =
            [
                ("UN", "UNSC", "United Nations Security Council Consolidated List", "UN-Liste, UN-Sanktionsliste",
                    "Alle vom UN-Sicherheitsrat mit Finanzsanktionen belegten Personen und Organisationen. In der EU über Verordnungen umgesetzt und in der CFSP-Liste enthalten.",
                    "Finanzsanktionen", "Sicherheitsrat der Vereinten Nationen", "weltweit", true,
                    "https://main.un.org/securitycouncil/en/content/un-sc-consolidated-list"),
                ("EU", "CFSP", "CFSP Consolidated Financial Sanctions List", "EU-Finanzsanktionsliste, EU-Sanktionsliste",
                    "Konsolidierte Liste aller Personen, Unternehmen und Organisationen mit EU-Finanzsanktionen (u. a. Terrorismus, Länderembargos, Cyber, Menschenrechte). Maßgeblich für Unternehmen unter EU-Recht.",
                    "Finanzsanktionen", "European External Action Service (EEAS)", "EU", true,
                    "https://data.europa.eu/data/datasets/consolidated-list-of-persons-groups-and-entities-subject-to-eu-financial-sanctions?locale=en"),
                ("EU", "EU-RU", "EU – Russia Embargo on Critical Goods", "Russland-Embargo, VO 833/2014 Anhang IV",
                    "Anhang IV (militärische Endnutzer) und Anhang XLII („Schattenflotte“) der Verordnung (EU) 833/2014 – relevant bei Dual-Use- und kritischen Gütern, auch für Empfänger außerhalb Russlands.",
                    "Exportkontrolle", "Europäische Union", "EU", true,
                    "https://eur-lex.europa.eu/eli/reg/2014/833"),
                ("EU", "EU-BY", "EU – Belarus Embargo on Critical Goods", "Belarus-Embargo, VO 765/2006 Anhang V",
                    "Anhang V der Verordnung (EU) 765/2006 – Endnutzer, an die Dual-Use- und kritische Güter nicht geliefert werden dürfen.",
                    "Exportkontrolle", "Europäische Union", "EU", true,
                    "https://eur-lex.europa.eu/eli/reg/2006/765"),
                ("US", "DPL", "Denied Persons List", "BIS Denied Persons",
                    "Vom Handel mit US-Gütern (EAR) ausgeschlossene Personen und Unternehmen.",
                    "Exportkontrolle", "Department of Commerce – Bureau of Industry and Security (BIS)", "weltweit", true,
                    "https://www.bis.gov/licensing/end-user-guidance/denied-persons-list-dpl"),
                ("US", "UVL", "Unverified List", null,
                    "Warnliste: Endverbleib konnte von US-Behörden nicht geprüft werden – keine License Exceptions, erhöhte Sorgfalt bei EAR-Gütern.",
                    "Exportkontrolle", "Department of Commerce – Bureau of Industry and Security (BIS)", "weltweit", true,
                    "https://www.ecfr.gov/current/title-15/subtitle-B/chapter-VII/subchapter-C/part-744#Supplement-No.-6-to-Part-744"),
                ("US", "EL", "Entity List", "BIS Entity List",
                    "Genehmigungspflicht für Lieferungen von EAR-Gütern an gelistete Einrichtungen (Risiko für Sicherheit oder Außenpolitik der USA).",
                    "Exportkontrolle", "Department of Commerce – Bureau of Industry and Security (BIS)", "weltweit", true,
                    "https://www.ecfr.gov/current/title-15/subtitle-B/chapter-VII/subchapter-C/part-744#Supplement-No.-4-to-Part-744"),
                ("US", "MEUL", "Military End User List", null,
                    "Militärische Endverwender (u. a. China, Russland, Venezuela) mit besonderen Genehmigungspflichten nach EAR § 744.21.",
                    "Exportkontrolle", "Department of Commerce – Bureau of Industry and Security (BIS)", "weltweit", true,
                    "https://www.ecfr.gov/current/title-15/subtitle-B/chapter-VII/subchapter-C/part-744#Supplement-No.-7-to-Part-744"),
                ("US", "SDN", "Specially Designated Nationals List", "OFAC-Liste, SDN-Liste",
                    "Umfassend von den USA sanktionierte Personen, Unternehmen und Transportmittel; einzelne Programme mit Sekundärsanktionen auch für Nicht-US-Personen.",
                    "Finanzsanktionen, Exportkontrolle", "Department of the Treasury – Office of Foreign Assets Control (OFAC)", "weltweit", true,
                    "https://www.treasury.gov/ofac/downloads/sdnlist.pdf"),
                ("US", "Non-SDN", "OFAC Consolidated Sanctions List (non-SDN Lists)", "Consolidated List, FSE, SSI, CAPTA",
                    "Weitere OFAC-Listen mit spezifischen Einschränkungen, u. a. Foreign Sanctions Evaders (FSE), Sectoral Sanctions (SSI) und CAPTA.",
                    "Exportkontrolle", "Department of the Treasury – Office of Foreign Assets Control (OFAC)", "weltweit", true,
                    "https://ofac.treasury.gov/other-ofac-sanctions-lists"),
                ("US", "ISN", "Nonproliferation Sanctions", null,
                    "Maßnahmen des US-Außenministeriums im Zusammenhang mit Nichtverbreitungsregelungen (Exportbeschränkungen, Geschäftsverbote u. a.).",
                    "Exportkontrolle", "Department of State – Bureau of International Security and Nonproliferation (ISN)", "weltweit", true,
                    "https://www.state.gov/bureau-of-arms-control-and-nonproliferation/nonproliferation-sanctions"),
                ("US", "DDTC", "List of Statutorily Debarred Parties", "AECA/ITAR Debarred Parties",
                    "Vom Handel mit US-Rüstungsgütern (ITAR) ausgeschlossene Personen und Organisationen.",
                    "Exportkontrolle", "Department of State – Directorate of Defense Trade Controls (DDTC)", "weltweit", false,
                    "https://www.pmddtc.state.gov/ddtc_public?id=ddtc_kb_article_page&sys_id=7188dac6db3cd30044f9ff621f961914"),
                ("US", "FDA", "FDA Debarment List", null,
                    "Von der FDA ausgeschlossene Personen und Unternehmen (Arzneimittel, Medizinprodukte).",
                    "Sonstige", "U.S. Food and Drug Administration (FDA)", "USA", false,
                    "https://www.fda.gov/inspections-compliance-enforcement-and-criminal-investigations/compliance-actions-and-activities/fda-debarment-list-drug-product-applications"),
                ("US", "UFLPA", "UFLPA (Uyghur Forced Labor Prevention Act) List", null,
                    "Unternehmen, deren Waren wegen Zwangsarbeit (v. a. Region Xinjiang) nicht in die USA eingeführt werden dürfen.",
                    "Sonstige", "Department of Homeland Security – U.S. Customs and Border Protection (CBP)", "USA", false,
                    "https://www.dhs.gov/uflpa-entity-list"),
                ("JP", "METI", "METI End User List", null,
                    "Endnutzer mit Verdacht auf Beteiligung an Massenvernichtungswaffen – relevant für Exporte aus Japan.",
                    "Exportkontrolle", "Ministry of Economy, Trade and Industry (METI)", "Japan", false,
                    "https://www.meti.go.jp/policy/anpo/law09.html"),
                ("CH", "SECO", "SECO Liste des Staatssekretariats für Wirtschaft der Schweiz", "Schweizer Sanktionsliste",
                    "Finanzsanktionen der Schweiz – relevant für Unternehmen mit Sitz in der Schweiz.",
                    "Finanzsanktionen", "Staatssekretariat für Wirtschaft (SECO)", "Schweiz", false,
                    "https://www.seco.admin.ch/seco/de/home/Aussenwirtschaftspolitik_Wirtschaftliche_Zusammenarbeit/Wirtschaftsbeziehungen/exportkontrollen-und-sanktionen/sanktionen-embargos/sanktionsmassnahmen.html"),
                ("GB", "OFSI", "OFSI Consolidated List of Financial Sanctions Targets in the UK", "UK-Sanktionsliste, HMT-Liste",
                    "Finanzsanktionen des Vereinigten Königreichs – relevant für Unternehmen unter britischem Recht.",
                    "Finanzsanktionen", "HM Treasury – Office of Financial Sanctions Implementation (OFSI)", "Vereinigtes Königreich", false,
                    "https://www.gov.uk/government/collections/financial-sanctions-regime-specific-consolidated-lists-and-releases"),
                ("CA", "CASL", "Consolidated Canadian Autonomous Sanctions List", null,
                    "Kanadische Sanktionen nach SEMA und JVCFOA – relevant für kanadische Unternehmen.",
                    "Finanzsanktionen", "Global Affairs Canada", "Kanada", false,
                    "https://www.international.gc.ca/world-monde/international_relations-relations_internationales/sanctions/consolidated-consolide.aspx?lang=eng"),
                ("AT", "OeNB", "Austrian Sanctions List (Österreichische Sanktionsliste der OeNB)", null,
                    "Österreichische Finanzsanktionen, v. a. zur Terrorismusbekämpfung.",
                    "Finanzsanktionen", "Oesterreichische Nationalbank (OeNB)", "Österreich", false,
                    "https://www.oenb.at/Ueber-Uns/Rechtliche-Grundlagen/Verordnungen-nach-SanktG.html"),
                ("FR", "FR-GEL", "Dispositif national de gel des avoirs à but de lutte contre le terrorisme", "Französische Sanktionsliste",
                    "Nationale französische Liste zur Terrorismusbekämpfung, berücksichtigt zusätzlich die EU-Finanzsanktionen.",
                    "Finanzsanktionen", "Direction générale du Trésor", "Frankreich", false,
                    "https://www.tresor.economie.gouv.fr/services-aux-entreprises/sanctions-economiques/dispositif-national-de-gel-des-avoirs"),
                ("INT", "WBL", "World Bank List", "Weltbank-Liste, Debarred Firms",
                    "Von Projekten und Ausschreibungen der Weltbank ausgeschlossene Unternehmen und Personen.",
                    "Sonstige", "World Bank Group", "weltweit", false,
                    "https://www.worldbank.org/en/projects-operations/procurement/debarred-firms"),
                ("CN", "UEL", "MOFCOM Unreliable Entity List", null,
                    "Ausländische Unternehmen und Personen, die nach Ansicht Chinas dessen Sicherheit oder Interessen gefährden.",
                    "Exportkontrolle", "Ministry of Commerce of the People's Republic of China (MOFCOM)", "China", false,
                    "https://aqygzj.mofcom.gov.cn/flzc/gzjgfxwj/index.html"),
                ("SG", "SG-MAS", "MAS List of the Monetary Authority of Singapore", null,
                    "UN-Finanzsanktionen und eigene Liste Singapurs zur Terrorfinanzierung.",
                    "Finanzsanktionen", "Monetary Authority of Singapore (MAS)", "Singapur", false,
                    "https://www.mas.gov.sg/regulation/anti-money-laundering/targeted-financial-sanctions/lists-of-designated-individuals-and-entities"),
                ("AU", "AUCL", "Australia Consolidated List", null,
                    "UN- und autonome australische Sanktionen (Autonomous Sanctions Act 2011).",
                    "Exportkontrolle, Finanzsanktionen", "Department of Foreign Affairs and Trade (DFAT)", "Australien", false,
                    "https://www.dfat.gov.au/international-relations/security/sanctions/consolidated-list"),
                ("NL", "NLNST", "Nationale sanctielijst terrorisme", null,
                    "Niederländische Liste von Personen und Organisationen mit Terrorismus-Finanzsanktionen.",
                    "Finanzsanktionen", "Ministry of Justice and Security (Niederlande)", "Niederlande", false,
                    "https://www.rijksoverheid.nl/documenten/rapporten/2015/08/27/nationale-terrorismelijst"),
                ("BE", "BENS", "Belgian National Sanctions List", null,
                    "Belgische Terrorismus-Sanktionen, ergänzend zu den EU-Sanktionen.",
                    "Exportkontrolle, Finanzsanktionen", "Federal Public Service (FPS) Foreign Affairs", "Belgien", false,
                    "https://finance.belgium.be/en/about_fps/structure_and_services/general_administrations/treasury/financial-sanctions/national"),
                ("PL", "PLSL", "Poland Sanctions List", null,
                    "Polnische Sanktionen, ergänzend zu den EU-Sanktionen (u. a. Vermögenssperre, Einreiseverbot, Vergabeausschluss).",
                    "Exportkontrolle, Finanzsanktionen", "Ministry of Foreign Affairs (Polen)", "Polen", false,
                    "https://www.gov.pl/web/mswia/lista-osob-i-podmiotow-objetych-sankcjami"),
                ("CZ", "CZNSL", "Czech National Sanctions List", null,
                    "Tschechische nationale Sanktionen, ergänzend zu den EU-Sanktionen; geführt vom tschechischen Außenministerium.",
                    "Finanzsanktionen, Sonstige", "Außenministerium der Tschechischen Republik", "Tschechien", false,
                    "https://mzv.gov.cz/jnp/cz/zahranicni_vztahy/sankcni_politika/sankcni_seznam_cr/index.html"),
            ];
            foreach (var l in listen)
            {
                migrationBuilder.InsertData(
                    table: "Sanktionslisten",
                    columns: ["Region", "Kuerzel", "Name", "Aliase", "Beschreibung", "Art", "Herausgeber",
                        "Geltungsbereich", "Standardumfang", "DownloadUrl", "Import", "Aktiv"],
                    values: [l.Region, l.Kuerzel, l.Name, l.Aliase, l.Beschreibung, l.Art, l.Herausgeber,
                        l.Geltungsbereich, l.Standard, l.Url, 0, true]);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sanktionslisten");
        }
    }
}
