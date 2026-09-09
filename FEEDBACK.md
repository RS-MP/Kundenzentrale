# Feedback & offene Punkte (Sammelphase)

Gesammelte Beobachtungen aus der Nutzung des Interimsbetriebs — wird
abgearbeitet, wenn die Sammelphase beendet ist. Neue Punkte einfach unten anfügen.

## Nr. 1 · Kundenakte → Vertrag nicht anklickbar (2026-09-01, MP)

Aufklärung: kein Fehler — der Vertragslink sitzt in der Karte „Verträge"
rechts (Faktenspalte); gesucht wurde im Verlauf links im Hauptbild.

Daraus abgeleitete Idee (klein, für später): Verlaufseinträge mit
Vertragsbezug ebenfalls klickbar machen — die Timeline kennt das Sprungziel
bereits (VerweisTyp/VerweisId), es wird nur noch nicht als Link gerendert.

Status: erledigt (kein Fehler) · Folgeidee offen

## Nr. 2 · Kennzeichen „Individualentwicklung" am Kunden (2026-09-01, MP)

Einige Kunden haben eine Individualentwicklung erhalten. Das muss am Kunden
gekennzeichnet und hinterlegt werden können (was wurde entwickelt), sichtbar
in der Kundenakte.

Status: erledigt (09.09.2026) — Feld „Individualentwicklung" am Kunden
(max. 2000 Zeichen), Pflege in der Akte unter Stammdaten & Adressen; gesetzter
Text = Kennzeichen, erscheint in der Faktenzeile (Beschreibung als Tooltip);
jede Änderung erzeugt einen Verlaufseintrag. Kein Import überschreibt das Feld.

## Nr. 3 · Preis-Sonderabsprachen zeitlich befristet (2026-09-01, MP)

Es gibt Sonderabsprachen zum Preis, z. B.:
- 50 % Rabatt in den ersten 2 Jahren (prozentual, befristet)
- fester Betrag wird in den ersten 2 Jahren vom Preis abgezogen (absolut, befristet)

Muss am Kunden/Vertrag hinterlegbar sein, in der Akte sichtbar, auswertbar
(„wer hat Sonderabsprachen?") und später in Abo-Abrechnung + MRR-Auswertung
(Phase 4) korrekt einfließen — Rabatt mit Gültig-von/Gültig-bis, Typ
Prozent oder Betrag.

Status: offen (Anforderung; Datenmodell bei Phase-4-Vorbereitung, Kennzeichnung ggf. früher)

## Nr. 4 · Software wirkt unübersichtlich → Konzept „Kundenakte ruhig" (2026-09-01, MP)

Konzept-Mockup mit Beruhigungsprinzipien aus Variante B erstellt:
`mockup/akte-ruhig.html` · https://claude.ai/code/artifact/5646e699-a66b-4778-abae-7da8eadc5ebb
(Kundenliste 4 Spalten, Akte mit Faktenzeile + Aufklappern, Verträge als
Arbeitsliste, Farbe nur für Status, Verlauf klickbar).

Status: erledigt — Konzept vom Nutzer abgesegnet und am 03.09.2026 in die App
übernommen (Kundenliste 4 Spalten + Chips, Akte mit Faktenzeile/Aufklappern/
klickbarem Verlauf, Verträge mit KPI-Zeile + Arbeitsfiltern, Farbe nur für Status)

## Nr. 5 · Weitere Bestände aufnehmen (2026-09-01, MP)

Im Kunden Archiv liegen weitere Ordner mit demselben KundenNr-Dateinamen-Muster:
1_Bestellungen (1.929), 3_Installations-Protokolle (27), 9_Kündigungen (2),
Verträge_NTT Data_Kunden (8), 5_Softwaremiete (1), 6_DORA (1), 4_Datenschutz (2).
In Odoo außerdem 1.150 Bestellungen (sale.order) und 1.542 Ausgangsrechnungen.
→ Scanner um Dokumentarten je Ordner verallgemeinern; Import nach Mockup-Entscheid
(Konzept sieht Dokumentarten in Akte + Verlauf bereits vor).

Status: erledigt (03.09.2026) — Scanner verallgemeinert (Dokumentart je Ordner,
auch .doc/.docx), 1.903 Dokumente für 1.138 Kunden importiert; 396 neue
Vertragsakten (nur-Bestellungen → Status Entwurf). Manuell offen: 53 Dateien
ohne Nummer (v. a. Schweizer „Bestellung_Abfrageversion …_<Firma>.pdf") und
20 mit unbekannter Nummer — Liste per Dry-Run (`vertragsscan --dry-run`) abrufbar.
Odoo-Bestellungen (sale.order) und Rechnungen bleiben für Phase 2/4.

## Nr. 6 · Ansprechpartner-Rollen aus Odoo (2026-09-03, MP)

Hinweis: In Odoo gibt es Hauptansprechpartner und Technische Ansprechpartner
(x_studio_hauptansprechpartner / x_studio_technischer_ansprechpartner).
Stand heute: beide werden beim Kontakt-Import als Rollen übernommen und in
der Akte bevorzugt angezeigt. ZU KLÄREN mit MP: Was fehlt konkret — sollen
die Rollen prominenter erscheinen, in Odoo gepflegte Änderungen schneller
ankommen (Import-Rhythmus), oder gibt es Kunden, bei denen die Rolle fehlt?

Status: geschlossen mit Annahme (03.09.2026) — Haupt- und Technik-Rolle
werden importiert und in der Akte bevorzugt angezeigt; keine weitere
Anforderung genannt. Bei Bedarf einfach neu melden, was fehlt.

## Nr. 7 · Vertragsseite unübersichtlich (2026-09-03, MP, Screenshot)

Beobachtungen am Beispiel V00435 (Abel + Schäfer):
- Typ-Pill und Titel doppeln sich („Wartungsvertrag Wartungsvertrag").
- Das Eingabefeld „Neue Version … / Version registrieren" steht unter JEDEM
  Bestandteil → viel Formular-Rauschen; besser erst auf Klick zeigen
  (z. B. kleiner Link „Version hinzufügen").
- Bestellungs-Bestandteile heißen wie die Datei („6816") — nichtssagend;
  zudem heißen Wartungsvertrag- und Bestellungs-Datei beide „6816.pdf"
  (verschiedene Ordner) → verwechselbar. Ordner/Dokumentart am Dokument
  anzeigen, Titel sprechender machen (z. B. „Bestellung vom <Dateidatum>").

Status: erledigt (03.09.2026) — Titel-Dopplung entfernt (Typ-Pill nur noch,
wenn er nicht schon im Titel steht), Versions-Formular hinter „+ Version
hinzufügen", Herkunfts-Ordner steht jetzt am Dokument („— 1_Bestellungen").

## Nr. 8 · Karte Betrieb & Lizenzen zu luftig (2026-09-03, MP, Screenshot)

Die Lizenz-Positionen wirken verstreut (große Abstände zwischen den Zeilen,
Beispiel S00919). Kompakter setzen, Positionen optisch als Block unter dem
Abo-Kopf gruppieren.

Status: erledigt (03.09.2026) — Positionen als kompakter, dezent hinterlegter
Block unter dem Abo-Kopf.

## Nr. 9 · Insolvenz-Kennzeichnung über Odoo-Stichwörter (2026-09-03, MP)

Prozess: In Odoo Stichwort „Insolvenz" am Kunden setzen (Kontakte →
Stichwörter), Details in die interne Notiz; Vertragsfolge über Abo-Kündigung
(Kategorie wird automatisch „Ehemaliger Kunde").

Status: NEU ENTSCHIEDEN (03.09.2026, MP): Odoo-Stichwörter enthalten zu viele
fremde Infos (147 Kunden hätten Tags mitgebracht) → Übernahme zurückgebaut,
importierte Werte bereinigt. Stattdessen: Merkmale werden MANUELL in der
Kundenzentrale gepflegt (Akte → Stammdaten & Adressen → Merkmale), bleiben
bis zur bewussten Änderung; ändert der Odoo-Import Kundendaten eines
Merkmal-Trägers, wird das Merkmal gelb als „(prüfen)" markiert + Timeline-
Eintrag — Speichern setzt die Markierung zurück. Jede Vergabe/Änderung
erzeugt einen Verlaufseintrag. Air Berlin (12439) trägt „Insolvenz".

## Nr. 10 · Kunden-Archiv (2026-09-03, MP)

Kunden mit Merkmal (Einmalkauf, Insolvenz …) sollen in ein Archiv verschoben
werden können — per Button am Kunden; Rückkehr automatisch bei neuer
Geschäftsaktivität (z. B. neues Abo).

Status: erledigt (03.09.2026) — Button „Ins Archiv verschieben"/„Aus dem
Archiv holen" im Akten-Kopf, Archiv-Chip in der Kundenliste, archivierte
Kunden sind aus allen Standard-Sichten ausgeblendet. Automatische Rückkehr
bei laufendem Abo aus dem Odoo-Import oder neuem Vertrag (mit Timeline-
Eintrag); bewusst NICHT bei bloßen Stammdaten-Änderungen in Odoo. Air Berlin
(12439) ist archiviert.

## Nr. 11 · Filter geht beim Zurückspringen verloren (2026-09-03, MP)

Nach Kunde öffnen + Browser-zurück war die Chip-Auswahl (Kunden/Alle/Zu
klären …) wieder auf Standard.

Status: erledigt (03.09.2026) — Filter, Suche und Seite stehen jetzt in der
URL (?filter=…&suche=…&seite=…) und überleben Zurück-Navigation, Reload und
Lesezeichen. Verträge-Liste seit 08.09.2026 analog.

## Nr. 12 · Merkmale als Auswahlliste statt Freitext (2026-09-03, MP)

Bei den Merkmalen (Tags) soll eine Auswahl angeboten werden statt Freitext —
vordefiniert mindestens „Insolvenz" und „Einmalkauf"; neue Tags können zur
Auswahl hinzugefügt werden (und stehen dann allen zur Verfügung).

Umsetzungsidee: Merkmal-Stammliste als Einstellung/eigene Tabelle, Pflege im
Admin-Bereich; in der Akte Auswahl (Mehrfachauswahl?) + „Neues Merkmal…".
Freitext-Wildwuchs („Insolvent", „insolvenz", „Insolvenz!") wird so vermieden.

Status: erledigt (03.09.2026) — Merkmal-Stammliste als Tabelle (Seed:
Insolvenz, Einmalkauf), in der Akte Mehrfachauswahl per Chips + „Neues
Merkmal für die Auswahl" (steht danach allen zur Verfügung); kein Freitext
mehr am Kunden.

## Nr. 13 · Regel V2: gültiger Zeitraum + Kategorie Einmalbestellung (2026-09-03, MP)

Auslöser 11304/adquas: -1-Datei war eine Kündigung, kein Nachtrag — aktiver
Bestandsvertrag ohne Laufzeit ist kein Beleg für einen Kunden.

Status: erledigt (03.09.2026) — Kunde = laufendes Abo ODER aktiver Vertrag
MIT Laufzeit; aktive Verträge ohne Laufzeit → „Zu klären" (304). Neue
berechnete Kategorie „Einmalbestellung" (nur Bestellungen, nie Abo; 140).
Verteilung: 810 Kunden / 304 zu klären / 140 Einmalbestellung / 4.225
Interessenten. adquas: Vertrag auf Gekündigt gesetzt → Ehemaliger Kunde.
Beim Klären gilt: Kündigung gefunden → Vertragsstatus „Gekündigt" setzen.

## Nr. 14 · Standardverträge aus Word-Vorlage erstellen (2026-09-08, MP)

Anforderung: Verträge aus Word-Vorlagen im Portal erzeugen (Kundendaten
automatisch eingesetzt), als PDF, optional direkt per E-Mail an einen
Ansprechpartner verschicken und den Rücklauf überwachen.

Stufenplan:
- Stufe 1a (umgesetzt): Vorlagen-Verzeichnis als Admin-Einstellung
  (\\RS-W2019-01\Orga\13_Lizenzbedingungen - Vertrag IDprove, nur
  Root-Dateien = aktive Vorlagen); „Vertrag aus Vorlage" in der Kundenakte
  füllt die Word-Datei (bestehende Muster-Texte UND {{Platzhalter}}),
  legt sie im Zielverzeichnis ab, registriert sie an einer neuen
  Vertragsakte (Entwurf) mit Prüfsumme + Timeline. Ausgabe vorerst .docx.
- Stufe 1b (wartet auf Telerik-Zugang, Entwicklungsleiter im Urlaub):
  PDF-Export über Telerik Document Processing (RadWordsProcessing,
  von der Reporting-Lizenz abgedeckt).
- Stufe 2 (Phase-2-Bausteine): Versand über die E-Mail-Warteschlange
  (SMTP-Zugangsdaten verschlüsselt!) + Rücklauf-Überwachung über
  Aufgaben/Wiedervorlage.

Status: Stufe 1a umgesetzt · 1b und 2 offen

## Nr. 15 · Chip-Reihenfolge in der Kundenliste durcheinander (2026-09-09, MP)

Die Reihenfolge der Filter-Chips wirkt unsortiert: „Alle" steht mitten drin,
„Gekündigt/Ehemalig" gleich an zweiter Stelle.

Idee: nach Arbeitslogik ordnen — erst die Kategorien im Lebenszyklus
(Kunden → Interessenten → Zu klären → Einmalbestellung → Gekündigt/Ehemalig),
dann die Meta-Sichten ans Ende (Alle · Mit Partner · Archiv), ggf. optisch
leicht abgesetzt.

Status: erledigt (09.09.2026) — Reihenfolge Kunden → Interessenten → Zu klären
→ Einmalbestellung → Gekündigt/Ehemalig, danach Trennstrich und die
Meta-Sichten Alle · Mit Partner · Archiv.

## Nr. 16 · „Ins Archiv verschieben" zu dominant in der Kundenakte (2026-09-09, MP)

Der Archiv-Button im Akten-Kopf ist ein riesiger Knopf — für eine seltene
Aktion viel zu präsent.

Idee: als kleinen, dezenten Textlink/Kleinknopf rechts im Akten-Kopf setzen
(oder in ein „…"-Menü), Beschriftung kürzen; „Aus dem Archiv holen" darf
sichtbarer bleiben, weil es im Archiv-Kontext die Hauptaktion ist.

Status: erledigt (09.09.2026) — „Ins Archiv" als dezenter Kleinknopf;
„Aus dem Archiv holen" bleibt in normaler Größe.

## Backlog (Aufgaben beim Nutzer / für später)

- **Entra + IIS:** Klick-Anleitung liegt in `DEPLOYMENT.md` — sobald die
  App-Registrierungen existieren (Tenant-/Client-IDs), baut Claude MSAL ins
  Frontend + Produktions-CORS ein und das Deployment kann laufen.
- Klärungsliste abarbeiten (Chip „Zu klären", 304): Kündigung gefunden →
  Vertragsstatus „Gekündigt"; echtes Abo → in Odoo pflegen + abos-Import.
- 37 nicht zugeordnete Vertragsdateien manuell erfassen (Liste per
  `vertragsscan --dry-run`; Namens-Zuordnung hat 36 Schweizer Bestellungen
  bereits automatisch erledigt).
- KundenNr-Dubletten in Odoo bereinigen (13 Stück, siehe Import-Warnungen)
- Backup-Plan für die Produktiv-DB einrichten (Hinweise in DEPLOYMENT.md)
- SQL Server 2017 auf RS-W2019-01: Support endet Okt 2027, Upgrade einplanen
- 7 Odoo-Abos hängen am Ansprechpartner statt an der Firma (Import überspringt
  sie) — in Odoo umhängen oder Import um commercial_partner erweitern
