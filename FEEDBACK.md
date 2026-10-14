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
- Stufe 1b (umgesetzt 25.09.2026): PDF-Export über Telerik Document
  Processing — die Vertragserzeugung legt jetzt ein PDF ab (Metadaten:
  rausoft GmbH, Vertragsnummer, Vorlagen-Stand) und registriert es; die
  gefüllte .docx liegt als bearbeitbare Quelle gleichnamig daneben.
  Optional wird das PDF digital signiert (PFX-Zertifikat als Einstellung,
  Passwort verschlüsselt) — Änderungen an der Datei machen die Signatur
  ungültig. Mit Testzertifikat verifiziert; echtes Zertifikat noch offen.
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

## Nr. 17 · Vertragsvorlagen-Verwaltung mit Compliance-Rolle (2026-09-10, MP)

Anforderung (MP):
- Verwaltung für Vertragsvorlagen: Compliance erstellt eine neue Word-Vorlage,
  ordnet sie einem Typ zu (z. B. NDA) und schaltet sie **produktiv**.
- Vorlagen werden **versioniert**; eine Version wird für die Verwendung durch
  bestimmte Rollen freigeschaltet (z. B. Vertrieb).
- In der Vertragsverwaltung sichtbar, **welcher Kunde welchen Versionsstand**
  (z. B. des NDA) unterschrieben/erhalten hat.
- Neue Rolle **Compliance** — nur sie verwaltet die Vorlagen.
- Quelle bleibt ein Server-Verzeichnis (heute Vorlagen.Verzeichnis).

Ergänzungsvorschläge (Claude, zur Diskussion beim Abarbeiten):
1. **Vorlagen-Stammsatz in der DB** (Name, Vertragstyp/BestandteilTyp,
   Beschreibung) + je Version eine eingefrorene Dateikopie mit SHA-256 in einer
   Versionsablage — nachträgliche Änderungen an der Word-Datei können so nie
   unbemerkt in eine freigegebene Version fließen (gleiches Prinzip wie bei
   Vertragsdokumenten).
2. **Status-Workflow je Version:** Entwurf → Produktiv → Zurückgezogen;
   höchstens eine produktive Version je Vorlage; beim Produktivschalten
   Pflichtfeld „Änderungsnotiz" (was ist neu) + Gültig-ab-Datum.
3. **Platzhalter-Prüfung vor Freigabe:** System meldet unbekannte {{…}}-Platz-
   halter (VorlagenDienst kann das schon) — Compliance sieht Warnungen, bevor
   die Version produktiv geht.
4. **Versionsstand am erzeugten Dokument speichern** → Auswertungen:
   „Welcher Kunde hat welche NDA-Version?" — ~~Arbeitsliste „Kunden mit
   veralteter Version"~~ entschieden 25.09.2026 (MP): nicht gewünscht, ältere
   unterschriebene Stände bleiben gültig.
5. **Nutzungsfreigabe je Rolle und Vorlage** (Vertrieb darf NDA + Wartungs-
   vertrag, Support nur X …); Erzeugen-Knopf in der Akte zeigt nur, was die
   eigene Rolle darf.
6. ~~Vier-Augen-Prinzip~~ — entschieden 10.09.2026 (MP): brauchen wir nicht.
7. **Bestandsübernahme:** heutige Root-Dateien im Vorlagen-Verzeichnis werden
   als jeweils Version 1 (produktiv) übernommen.
8. Rolle Compliance als neues Flag in MitarbeiterRolle (Benutzerverwaltung
   existiert seit 10.09.); Vorlagen-Verwaltung als eigener Admin-/Compliance-
   Bereich.

Status: KERN UMGESETZT (25.09.2026) — Seite „Vorlagen" mit Freigabe durch
Rolle Compliance (Admins dürfen auch): Entwurfsdateien im Vorlagen-Verzeichnis,
Freigabe friert Kopie als Version vN ein (Unterordner Freigegeben, SHA-256,
Freigeber + Änderungsnotiz); Kundenakte-Auswahl zeigt nur freigegebene
aktuellste Versionen; erzeugte Dokumente speichern die VorlagenVersion →
Auswertung „welcher Kunde hat welchen Stand" je Vorlage; Zurückziehen möglich.
Status: ERLEDIGT (25.09.2026) — Reste umgesetzt: **Dokumentart je Vorlage**
(bei Freigabe/Einstellungen; der erzeugte Bestandteil bekommt diese Art statt
pauschal „Lizenzvertrag"), **Nutzungsfreigabe je Rolle** (Vertrieb/Support/
Buchhaltung; keine Auswahl = alle, Compliance/Admin immer — Auswahl in der
Akte gefiltert UND serverseitig beim Erzeugen geprüft), **Gültig-ab-Datum**
(bis zum Stichtag bleibt die Vorversion gültig; Übersicht zeigt „v3 gilt ab …").
**Bewusst KEINE „veraltet"-Markierung** (Entscheidung MP 25.09.2026): Neue
Verträge entstehen nur aus der gültigen Version; unterschriebene Verträge mit
älterem Vorlagen-Stand bleiben gültig — Nachträge nur, wenn rechtlich nötig,
im Einzelfall. Die Auswertung „wer hat welchen Stand" bleibt reine Info. Bestehende Versionen gelten ab
ihrem Freigabedatum; Bestandsvorlagen stehen auf Lizenzvertrag — „NDA
Standard" o. Ä. nach dem Update einmal unter Vorlagen auf die richtige Art
stellen.

## Nr. 18 · Aktualität/Reihenfolge der Vertragsdokumente erkennen (2026-09-10, MP)

Beim Einlesen der Verträge (Bestandsscanner) soll erkennbar werden, welcher
Stand der aktuellste ist — z. B. bei 3 Bestellungen die zeitliche Reihenfolge
abbilden.

Umsetzungsidee:
- Je Dokument ein **Dokumentdatum** speichern. Quellen in dieser Reihenfolge:
  Datum im Dateinamen (z. B. „… 2026-09-08.docx"), sonst Datei-Änderungsdatum
  aus dem Dateisystem; -1/-2-Suffixe als Nachtrags-/Kündigungsreihenfolge.
- Bestandteile in Vertragsakte danach sortieren (neuestes oben), Bestellungen
  nummerieren („Bestellung 3 · 08.09.2026"), aktuellsten Stand im
  Vertragskopf und in der Vertragsliste ausweisen.
- Nachrüstung für den Bestand: Scanner-Lauf ergänzt das Datum idempotent an
  bereits importierten Dokumenten.

Status: ERLEDIGT (25.09.2026) — Dokumentdatum + Herkunft (Dateiname /
Dateidatum / Erzeugt / Manuell) und Folgenummer (-1/-2) am Bestandteil;
Erkennung von 2026-09-08, 08.09.2026, 8.9.26, 20260908 im Dateinamen, sonst
Datei-Änderungsdatum („ca."). Vertragsakte gruppiert nach Dokumentart, darin
neuestes oben, Nummerierung „Bestellung 3", „jüngstes Dokument"-Hinweis;
Stand im Akten-Kopf, in Vertragsliste und Kundenakte. Korrektur je Bestandteil
(„Korrigieren": Art, Titel, Datum; manuell gesetztes Datum hat Vorrang).
Nachrüstung: `vertragsscan` ergänzt fehlende Daten idempotent, zusätzlich
Knopf „Dokumentdaten nachtragen" unter Administration → Verzeichnisse
(läuft direkt auf dem Server, kein Import-Tool nötig).

## Nr. 19 · Dokumentart deutlicher kennzeichnen (2026-09-10, MP)

Bestellung, Wartungsvertrag, NDA usw. sollen auf einen Blick unterscheidbar
sein (Vertragsakte, Verträge-Karte in der Kundenakte, Vertragsliste).

Umsetzungsidee (im Ruhig-Konzept, Farbe bleibt dem Status vorbehalten):
kompakte Typ-Kürzel-Pills je Bestandteil (WV · BE · NDA · AVV · KÜ · NT …)
mit vollem Namen als Tooltip, in der Vertragsakte optional Gruppierung nach
Dokumentart, in Listen das Kürzel vor dem Titel.

Status: ERLEDIGT (25.09.2026) — Kürzel WV · LV · AVV · NDA · SV · NT · BE ·
IP · KÜ · SO als monochrome Pills (Tooltip = voller Name, Anzahl als „×5") in
Vertragsliste und Verträge-Karte der Kundenakte; Vertragsakte gruppiert nach
Dokumentart mit Kürzel je Bestandteil; Vorlagen-Auswahl zeigt die Art mit.
Formular „+ Bestandteil" bietet jetzt alle Dokumentarten an.

## Nr. 20 · Vertrauliche Verträge (Sondervereinbarungen, Partnerverträge) (2026-09-28, MP)

Es gibt Verträge, die nicht alle lesen dürfen — Sondervereinbarungen mit
Kunden und Verträge mit Partnern; sie liegen in einem eigenen Ordner.
Entscheidungen (MP): eigene Rolle; alle anderen sehen, **dass** es den
Vertrag gibt, plus manuell gepflegte Kurzinfos (Art, z. B. Provisionssatz),
damit sie nicht sofort nachfragen müssen.

Stufe 1 (umgesetzt 28.09.2026): Rolle **„Vertrauliche Verträge"** (bewusst
nicht in Admin enthalten); Bestandteil-Kennzeichen **Vertraulich** + für alle
sichtbare **Kurzinfo** (max. 300 Zeichen). Ohne Rolle liefert die API nur
Art, Datum und Kurzinfo — Titel, Beschreibung, Dokumente und Pfade verlassen
den Server nicht; Dateiabruf, Ändern und Registrieren → 403. Vertrauliche
Dokumente nur aus der neuen Einstellung **VertraulicheAblage.Verzeichnis**
(Administration → Verzeichnisse), normale Bestandteile dürfen keine Dateien
von dort registrieren. Verlaufseinträge nennen keine vertraulichen Titel oder
Dateinamen; Admin-Löschung listet vertrauliche Pfade nicht auf. Vertraulichkeit
lässt sich nicht aufheben, sobald Dokumente registriert sind. Der
`vertragsscan` liest den vertraulichen Ordner nicht.

Stufe 2 (umgesetzt 28.09.2026): **Partnerverträge + Partner-Akte.** Ein Vertrag
gehört genau einem Kunden ODER einem Partner (DB-Check-Constraint). Neuer
Bereich **Partner** (Übersicht mit Modell, Provision, Anzahl Kunden/Verträge)
und Partner-Akte (Stammdaten aus Odoo, nur lesend; Partnerverträge mit
Kürzeln, Stand und Kurzinfos; zugeordnete Kunden „vermittelt" / „Rechnung an
Partner"), aufgeteilt in **Kunden mit aktivem Abo** und **ohne aktives Abo**
(Interessenten u. a. — Ehemalige/Zu klären mit Kategorie-Hinweis, gleiche Regel wie
die Kundenliste; 08.10.2026). „+ Partnervertrag" legt die Vertragsakte an; neue Bestandteile
sind dort als vertraulich vorbelegt. Vertragsliste: Chip „Partnerverträge",
Suche findet Partnernamen, Kurzinfos stehen unter dem Vertrag (auch in der
Kundenakte). **Partner ist auch Kunde** (nutzt die Software selbst): manuelle
Verknüpfung Partner → Kunden-Datensatz in der Partner-Akte (mit Namensvorschlag,
höchstens ein Partner je Kunde); Partnerverträge bleiben am Partner, eigene
Nutzungsverträge/Abos am Kunden, beide Akten verlinken aufeinander. Offen: eigener Verlauf für Partner (Partnerverträge schreiben
noch keinen Verlaufseintrag), Partner-Stammdaten pflegen statt nur Odoo.

Wichtig für den Betrieb: NTFS-Rechte auf dem vertraulichen Ordner zusätzlich
einschränken (App-Pool-Konto + berechtigte Personen) — die App schützt nur den
Zugriff über die Kundenzentrale.

Status: Stufe 1 + 2 umgesetzt · Partner-Verlauf offen

## Nr. 21 · „Vertrag erstellen" und Vorlagenbereich überarbeiten (2026-10-08, MP, Screenshot)

Die Zeile „Vorlage · Angebot · Aus Vorlage erstellen" in der Kundenakte ist
gequetscht und zeigt nicht, was passiert (leeres Angebotsfeld bei Kunden ohne
Angebot). Im Vorlagenbereich fehlt der Überblick über alle Dateien im
Verzeichnis: was ist zugeordnet, aktiv, ersetzt …

Entscheidungen (MP): Fenster statt eigener Seite; nur aktuell gültige
Vorlagen anzeigen, mit „gültig seit".

Umgesetzt (08.10.2026):
- **Fenster „Vertrag aus Vorlage"** (Knopf „+ Vertrag aus Vorlage"): Vorlage
  (Kürzel, Version, gültig seit) → Angebot (mit Positionen, „ohne") und
  Ansprechpartner für Anrede/Name → **Prüfen** mit den tatsächlich
  eingesetzten Werten, Warnungen und unbekannten Platzhaltern (Server-
  Vorschau, gleiche Logik wie die Erzeugung) → Ergebnis mit „Zur Vertragsakte".
- **Dateitabelle im Vorlagenbereich** (Compliance): jede Datei mit Status
  neu / geändert seit Freigabe / aktiv / gilt künftig / ersetzt /
  zurückgezogen (Zuordnung per SHA-256 und gemerkter Quelldatei), Spalte
  Platzhalter (unbekannte `{{…}}` vor der Freigabe — Punkt 3 aus Nr. 17),
  Aktion „Freigeben …" bzw. „Neue Version …" im Fenster. Prüfung der
  eingefrorenen Kopien („Freigegeben": vorhanden + unverändert).
  Inhaltsgleiche Datei wird nicht erneut freigegeben.
- Vorlagen-Übersicht als Tabelle mit aufklappbaren Details (Versionen, wer
  hat welchen Stand, Dokumentart/Rollen, Zurückziehen).

Offen: **Probe-PDF** mit Testkunde vor der Freigabe.

Status: umgesetzt · Probe-PDF offen

## Nr. 22 · Wiki — Start mit Sanktionslisten (2026-10-14, MP)

Die Mitarbeiter sollen in der Kundenzentrale nachlesen können, wie bei uns was
funktioniert und wie wir was bezeichnen. Erstes Thema: Sanktionslisten —
gruppiert nach Land/Region (DE, EU, US …), je Liste: offizielle Bezeichnung,
wie sie bei uns und bei Kunden/Wettbewerbern heißt, Beschreibung,
Download-Link, Import (automatisch über den Sanction Admin oder von Hand),
wer überwacht + Vertretung, Prozess.

Umgesetzt (14.10.2026): Bereich **Wiki** (Navigation) mit Themenübersicht;
Katalog **Sanktionslisten** gruppiert nach Region, Suche über Name, Kürzel,
interne und andere Bezeichnungen, Filter (Standardumfang, Sanction Admin, von
Hand, Import offen, ohne Verantwortliche), aufklappbare Details, Pflege im
Fenster (Compliance/Admin; lesen alle). Verantwortlich/Vertretung als Auswahl
aus den Benutzern. Startbestand: 28 Listen aus der Sanktionslisten-Übersicht
von sanktionslisten.de (Stand 16.12.2025, P:\01_Vertrieb\…) — Fakten und
Links übernommen, Beschreibungen als eigene Kurzfassung. Offen für MP:
interne Bezeichnungen, Import, Verantwortliche/Vertretung und Prozesse je
Liste pflegen.

**Standardprozess je Importart** (14.10.2026, MP: ja): eigener Abschnitt
„Standardprozesse"; Listen ohne eigenen Prozess zeigen den Standard ihrer
Importart (mit Hinweis), ein eigener Prozess hat Vorrang und wird als
Abweichung gekennzeichnet.

**Pflege-Rechte** (14.10.2026, MP: Variante 2 mit Themen-Zuschnitt): neue Rolle
**Wiki-Redaktion**; je Redakteur werden die bearbeitbaren Wiki-Themen einzeln
freigegeben (Benutzer & Rollen, Feld `WikiThemen`), damit z. B. eine Person nur
Sanktionslisten pflegt und eine andere später Entwicklung oder Buchhaltung.
Admin und Compliance dürfen alle Themen. Neue Themen werden in
`Domain/Wiki/WikiThema.cs` registriert und tauchen dann automatisch in der
Themen-Auswahl auf. Serverseitig geprüft (`Dienste/WikiBerechtigung`).

Status: umgesetzt · Pflege der Inhalte offen

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
