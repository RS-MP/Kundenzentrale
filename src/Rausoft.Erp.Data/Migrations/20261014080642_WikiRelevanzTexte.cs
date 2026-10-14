using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class WikiRelevanzTexte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Relevanz",
                table: "Sanktionslisten",
                type: "nvarchar(max)",
                maxLength: 200,
                nullable: true);

            // Ausführliche Texte in eigenen Worten (Fakten aus der Übersicht von sanktionslisten.de,
            // Stand 16.12.2025). Nur für Listen, die noch niemand im Wiki bearbeitet hat.
            (string Region, string Name, string Beschreibung, string Relevanz)[] texte =
            [
                ("UN", "United Nations Security Council Consolidated List",
                    "Zusammenführung aller Personen und Organisationen, gegen die der UN-Sicherheitsrat Finanzsanktionen verhängt hat. Die Mitgliedstaaten sind verpflichtet, diese Sanktionen in nationales Recht zu übernehmen; in der EU geschieht das über Verordnungen, die unmittelbar in allen Mitgliedstaaten gelten. Neue UN-Einträge gelten in der EU übergangsweise bis zu sechs Tage bzw. bis zur Umsetzung in EU-Recht.",
                    "Für Unternehmen nicht direkt verbindlich. In der EU ist eine gesonderte Prüfung meist entbehrlich, weil die UN-Sanktionen vollständig in die CFSP-Liste übernommen werden. Sinnvoll als Ergänzung in der kurzen Übergangszeit nach UN-Beschlüssen."),
                ("EU", "CFSP Consolidated Financial Sanctions List",
                    "Konsolidierte Liste aller Personen, Unternehmen und Organisationen, gegen die EU-Finanzsanktionen bestehen. Sie bündelt die Antiterror-Verordnungen, die länderbezogenen Embargoverordnungen sowie Verordnungen etwa zu Cyberangriffen und schweren Menschenrechtsverletzungen.",
                    "Pflicht für alle Unternehmen, die in der EU ansässig sind oder nach EU-Recht organisiert sind und Geschäftsbeziehungen pflegen oder Zahlungen leisten. Die wichtigste Liste für unsere Kunden in Deutschland und der EU."),
                ("EU", "EU – Russia Embargo on Critical Goods",
                    "Russland-Embargo nach Verordnung (EU) 833/2014. Anhang IV nennt natürliche und juristische Personen, die militärische Endnutzer sind, zum militärisch-industriellen Komplex Russlands gehören oder den russischen Verteidigungs- und Sicherheitssektor unterstützen – darunter auch Unternehmen außerhalb Russlands. Anhang XLII listet Schiffe der sogenannten Schattenflotte, die weder Güter, Mittel noch Dienstleistungen erhalten und keine EU-Häfen anlaufen dürfen.",
                    "Für Unternehmen, die Dual-Use-Güter oder sonstige kritische Güter nach Anhang VII verkaufen, liefern oder ausführen – unabhängig vom Empfängerland – sowie für alle mit Bezug zu den gelisteten Schiffen. In der Geschäftspartnerprüfung wichtig, um Umgehungslieferungen über Drittländer zu erkennen."),
                ("EU", "EU – Belarus Embargo on Critical Goods",
                    "Belarus-Embargo nach Verordnung (EU) 765/2006. Anhang V nennt Personen, Organisationen und Einrichtungen, die als militärische Endnutzer gelten oder den militärisch-industriellen Komplex unterstützen. An sie wird keine Genehmigung zum Verkauf, zur Lieferung oder zur Ausfuhr der betroffenen Güter erteilt.",
                    "Für Unternehmen, die Dual-Use-Güter oder sonstige kritische Güter nach Anhang Va verkaufen, liefern oder ausführen – unabhängig vom Empfängerland. In der Geschäftspartnerprüfung hilfreich, um kritische Endverbleibe und Umgehungsrisiken zu erkennen."),
                ("US", "Denied Persons List",
                    "Personen, Unternehmen und Organisationen, denen die US-Behörden die Exportprivilegien entzogen haben. Sie dürfen Güter, die den Export Administration Regulations (EAR) unterliegen, weder exportieren noch reexportieren noch anderweitig damit handeln.",
                    "Für alle Unternehmen, die mit EAR-Gütern handeln – insbesondere beim Export von US-Produkten oder im Handel mit US-Personen. Geschäfte mit gelisteten Parteien sind auszuschließen."),
                ("US", "Unverified List",
                    "Warnliste des BIS mit Parteien, bei denen die US-Behörden den Endverbleib von US-Gütern nicht ausreichend prüfen konnten. Für Exporte oder Reexporte von EAR-Gütern an diese Parteien sind keine License Exceptions zulässig; zusätzlich ist eine besonders sorgfältige Endverbleibsprüfung nötig.",
                    "Für alle Unternehmen, die mit Gütern handeln, die den Export Administration Regulations der USA unterliegen."),
                ("US", "Entity List",
                    "Personen, Unternehmen und Organisationen, die nach Einschätzung der US-Behörden ein erhebliches Risiko für die nationale Sicherheit oder die Außenpolitik der USA darstellen. Export, Reexport und Inlandslieferungen von EAR-Gütern an diese Parteien sind genehmigungspflichtig; je Eintrag gelten eigene Auflagen, License Exceptions sind nur ausnahmsweise möglich.",
                    "Für Unternehmen, die mit EAR-Gütern handeln – unabhängig davon, ob es sich um Dual-Use-Güter oder nicht gelistete Güter handelt."),
                ("US", "Military End User List",
                    "Juristische Personen, die die US-Regierung als militärische Endverwender einstuft. Lieferungen an sie unterliegen besonderen Genehmigungspflichten nach § 744.21 EAR (China, Russland, Venezuela), soweit die Güter in Supplement 2 to Part 744 EAR genannt sind.",
                    "Für Unternehmen, die mit EAR-Gütern handeln, die in Supplement 2 to Part 744 EAR aufgeführt sind."),
                ("US", "Specially Designated Nationals List",
                    "Zentrale Sanktionsliste des OFAC mit Personen, Unternehmen, Organisationen und Transportmitteln, die von den USA umfassend sanktioniert sind. Sie deckt zahlreiche Programme ab, etwa Terrorismus, Drogenhandel, Cyberkriminalität und internationale Bedrohungen. Vermögen in den USA wird eingefroren; US-Personen dürfen keine Gelder oder wirtschaftlichen Ressourcen bereitstellen. Bei bestimmten Programmen entstehen auch für Nicht-US-Personen Genehmigungspflichten für den Handel mit EAR-Gütern.",
                    "Für alle Unternehmen weltweit, die mit EAR-Gütern handeln. Einige Programme enthalten Sekundärsanktionen, die auch Nicht-US-Personen und den Handel mit Nicht-US-Produkten treffen können. Für US-Personen gilt ein vollständiges Geschäftsverbot."),
                ("US", "OFAC Consolidated Sanctions List (non-SDN Lists)",
                    "Zusammenfassung der weiteren OFAC-Listen, die nicht den umfassenden Sanktionen der SDN-Liste unterliegen, aber spezifische Beschränkungen auslösen – darunter die Foreign Sanctions Evaders (FSE) List, die Sectoral Sanctions Identification (SSI) List und die Liste der Finanzinstitute unter CAPTA-Sanktionen. Die Einschränkungen in Handel und Finanzwesen richten sich nach der jeweiligen Unterliste.",
                    "Für alle Unternehmen weltweit, die mit EAR-Gütern handeln oder als US-Personen gelten. Einzelne Programme enthalten Sekundärsanktionen, die auch Nicht-US-Personen und Nicht-US-Produkte betreffen können."),
                ("US", "Nonproliferation Sanctions",
                    "Sanktionsmaßnahmen des US-Außenministeriums gegen Personen, Unternehmen, Organisationen und staatliche Akteure im Zusammenhang mit der Nichtverbreitung von Massenvernichtungswaffen. Sie beruhen auf verschiedenen Rechtsgrundlagen; je nach Programm drohen Exportbeschränkungen, Lizenzauflagen oder Geschäftsverbote.",
                    "Für Unternehmen, die mit EAR-Gütern handeln – unabhängig davon, ob Dual-Use-Güter oder nicht gelistete Güter betroffen sind."),
                ("US", "List of Statutorily Debarred Parties",
                    "Personen und Organisationen, die wegen Verstößen gegen den Arms Export Control Act (AECA) vom Handel mit US-Verteidigungsgütern ausgeschlossen wurden. Erfasst sind auch technische Daten und Dienstleistungen, die den International Traffic in Arms Regulations (ITAR) unterliegen.",
                    "Für Unternehmen der Verteidigungsindustrie und alle, die US-Rüstungsgüter oder ITAR-relevante Daten und Dienstleistungen handeln oder exportieren."),
                ("US", "FDA Debarment List",
                    "Öffentliche Liste der U.S. Food and Drug Administration mit Unternehmen und Personen, die nach Abschnitt 306(a) oder (b) des Federal Food, Drug, and Cosmetic Act ausgeschlossen wurden. Sie dürfen wegen Verstößen gegen FDA-Vorschriften keine Arzneimittel oder Medizinprodukte herstellen oder vertreiben.",
                    "Für Unternehmen, die FDA-Regularien unterliegen – vor allem Arzneimittel, Medizinprodukte und Lebensmittel – oder mit FDA-regulierten Produkten handeln."),
                ("US", "UFLPA (Uyghur Forced Labor Prevention Act) List",
                    "Liste des US-Heimatschutzministeriums mit Unternehmen, deren Waren nach dem Uyghur Forced Labor Prevention Act nicht in die USA eingeführt werden dürfen. Hintergrund ist die Vermutung, dass die Waren ganz oder teilweise durch Zwangsarbeit in der Volksrepublik China, insbesondere in Xinjiang, hergestellt wurden (19 U.S.C. § 1307).",
                    "Für Unternehmen in Lieferketten von Asien in die USA, vor allem beim Import chinesischer Waren. Produkte aus Zwangsarbeit führen zu Einfuhrverboten."),
                ("JP", "METI End User List",
                    "Liste des japanischen Wirtschaftsministeriums mit natürlichen und juristischen Personen, bei denen der Verdacht besteht, dass sie an der Entwicklung oder dem Bau von Massenvernichtungswaffen beteiligt sind. Für Exporte an gelistete Endnutzer ist eine besonders sorgfältige Endverbleibsprüfung erforderlich.",
                    "Für Unternehmen, die aus Japan exportieren – vor allem in der Nuklear-, Chemie- und Rüstungsindustrie."),
                ("CH", "SECO Liste des Staatssekretariats für Wirtschaft der Schweiz",
                    "Personen und Organisationen, gegen die die Schweiz Finanzsanktionen verhängt hat. Grundlage sind Beschlüsse der Vereinten Nationen, der OSZE oder der wichtigsten Handelspartner der Schweiz, insbesondere der EU. Die Sanktionen dienen der Durchsetzung des Völkerrechts und der Menschenrechte.",
                    "Für Unternehmen mit Sitz in der Schweiz."),
                ("GB", "OFSI Consolidated List of Financial Sanctions Targets in the UK",
                    "Konsolidierte Liste aller Personen, Organisationen und Vereinigungen, gegen die das Vereinigte Königreich Finanzsanktionen verhängt hat. Die Sanktionen umfassen das Einfrieren von Vermögen sowie das Verbot, gelisteten Parteien Gelder oder wirtschaftliche Ressourcen bereitzustellen.",
                    "Für Unternehmen mit Sitz im Vereinigten Königreich oder nach britischem Recht gegründete Unternehmen."),
                ("CA", "Consolidated Canadian Autonomous Sanctions List",
                    "Konsolidierte Liste der kanadischen Regierung mit Personen und Organisationen, die unter dem Special Economic Measures Act (SEMA) und dem Justice for Victims of Corrupt Foreign Officials Act (JVCFOA) sanktioniert sind. Die Sanktionen umfassen Vermögenssperren und das Verbot, Gelder oder wirtschaftliche Ressourcen bereitzustellen.",
                    "Für kanadische Unternehmen – sie dürfen keine Geschäftsbeziehungen zu gelisteten Parteien unterhalten."),
                ("AT", "Austrian Sanctions List (Österreichische Sanktionsliste der OeNB)",
                    "Liste der Oesterreichischen Nationalbank mit Personen und Organisationen, gegen die Finanzsanktionen bestehen – insbesondere kapital- und zahlungsverkehrsbeschränkende Maßnahmen zur Bekämpfung der Terrorismusfinanzierung.",
                    "Für Unternehmen mit Sitz in Österreich oder nach österreichischem Recht gegründete Unternehmen, besonders im Finanz- und Zahlungsverkehr."),
                ("FR", "Dispositif national de gel des avoirs à but de lutte contre le terrorisme",
                    "Französische Sanktionsliste der Direction générale du Trésor mit Personen und Organisationen, die nach Auffassung der französischen Behörden mit terroristischen Handlungen in Verbindung stehen. Sie umfasst Vermögenssperren und Bereitstellungsverbote und berücksichtigt zusätzlich die EU-Finanzsanktionen.",
                    "Für Unternehmen mit Sitz in Frankreich oder nach französischem Recht gegründete Unternehmen."),
                ("INT", "World Bank List",
                    "Liste der Weltbank-Gruppe mit Unternehmen und Einzelpersonen, die wegen Verstößen gegen ethische Standards oder Missbrauch von Finanzmitteln ausgeschlossen wurden. Gelistete Parteien dürfen nicht an Projekten und Ausschreibungen der Weltbank teilnehmen.",
                    "Für Unternehmen und Organisationen, die an Weltbank-Projekten beteiligt sind oder Weltbank-Finanzierungen erhalten."),
                ("CN", "MOFCOM Unreliable Entity List",
                    "Liste des chinesischen Handelsministeriums mit ausländischen Unternehmen, Organisationen und Einzelpersonen, die nach Ansicht der chinesischen Regierung die nationale Sicherheit oder die Interessen Chinas gefährden. Gelistete Parteien können Exportbeschränkungen und wirtschaftlichen Sanktionen unterliegen.",
                    "Für Unternehmen mit Sitz in China oder Unternehmen, die chinesischem Recht unterliegen."),
                ("SG", "MAS List of the Monetary Authority of Singapore",
                    "Liste der singapurischen Zentralbank. Sie enthält die Finanzsanktionen des UN-Sicherheitsrats sowie eine eigene Liste von Personen, die der Terrorismusfinanzierung verdächtigt werden.",
                    "Für Unternehmen mit Sitz in Singapur oder Unternehmen, die singapurischem Recht unterliegen."),
                ("AU", "Australia Consolidated List",
                    "Konsolidierte Liste des australischen Außen- und Handelsministeriums mit allen sanktionierten Personen und Organisationen. Sie umfasst die Finanzsanktionen des UN-Sicherheitsrats und die autonomen australischen Sanktionen nach dem Autonomous Sanctions Act 2011.",
                    "Für Unternehmen mit Sitz in Australien oder Unternehmen, die australischem Recht unterliegen."),
                ("NL", "Nationale sanctielijst terrorisme",
                    "Niederländische Liste des Justiz- und Sicherheitsministeriums mit Personen und Organisationen, die wegen ihrer Verbindung zu terroristischen Aktivitäten mit Finanzsanktionen belegt sind.",
                    "Für Unternehmen in den Niederlanden, insbesondere im Finanzsektor."),
                ("BE", "Belgian National Sanctions List",
                    "Belgische nationale Sanktionsliste des Föderalen Öffentlichen Dienstes Auswärtige Angelegenheiten mit Personen und Organisationen, die des Terrorismus verdächtigt werden. Sie gilt zusätzlich zu den EU-Sanktionen.",
                    "Für Unternehmen mit Sitz in Belgien oder Unternehmen, die belgischem Recht unterliegen."),
                ("PL", "Poland Sanctions List",
                    "Offizielle polnische Sanktionsliste mit Personen und Organisationen, gegen die Sanktionen verhängt wurden – zusätzlich zu den EU-Sanktionen. Die Maßnahmen reichen vom Einfrieren von Geldern und Vermögen über Bereitstellungsverbote bis zu Einreiseverboten und dem Ausschluss von öffentlichen Aufträgen.",
                    "Für Unternehmen mit Sitz in Polen oder Unternehmen, die polnischem Recht unterliegen."),
                ("CZ", "Czech National Sanctions List",
                    "Tschechische nationale Sanktionsliste des Außenministeriums mit natürlichen und juristischen Personen. Sie ergänzt die EU-Sanktionen um restriktive Maßnahmen gegen bestimmte schwerwiegende Handlungen in den internationalen Beziehungen.",
                    "Für Unternehmen mit Sitz in der Tschechischen Republik oder Unternehmen, die tschechischem Recht unterliegen."),
            ];
            foreach (var t in texte)
            {
                migrationBuilder.UpdateData(
                    table: "Sanktionslisten",
                    keyColumns: ["Region", "Name"],
                    keyValues: [t.Region, t.Name],
                    columns: ["Beschreibung", "Relevanz"],
                    values: [t.Beschreibung, t.Relevanz]);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Relevanz",
                table: "Sanktionslisten");
        }
    }
}
