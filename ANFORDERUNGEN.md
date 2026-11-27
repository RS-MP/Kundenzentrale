# Kundenzentrale (Arbeitstitel) — Anforderungen & Konzept

**rausoft GmbH · Stand: 07.08.2026 · Ablösung von Odoo bis Ende 2027**

## Ziel & Rahmen

Odoo wird nur zu einem Bruchteil genutzt und soll bis Ende 2027 durch eine
Eigenentwicklung ersetzt werden. Umgebung: lokale Windows Server + SQL Server.
Die Buchhaltung bleibt vollständig bei DATEV / Steuerberater — das System
macht Fakturierung, keine Buchhaltung.

**Architektur (Entscheidung 08/2026): .NET Server-Applikation mit REST API
als Backend, Frontend entkoppelt.** Die gesamte Fachlogik (Kunden, Verträge,
Angebote, Fakturierung, Aufgaben/Tickets, E-Mail-Warteschlange) liegt hinter
der API; das Frontend (geplant: Blazor) ist dadurch austauschbar bzw.
erweiterbar. Betrieb lokal auf eigenen Windows Servern (On-Premises, keine
Cloud-Abhängigkeit). Vorteile im Zuschnitt: CTI-Telefonsoftware und
Service-Portal können dieselbe API nutzen (z. B. Anruf-Ereignisse in die
Timeline, Rückrufbitte → Aufgaben-Pool); Hintergrunddienste (E-Mail-Versand,
Erinnerungen, Wiedervorlagen) laufen im Server-Prozess. Security by default:
API nur intern erreichbar, HTTPS, tokenbasierte Authentifizierung,
Berechtigungen serverseitig durchgesetzt (nie nur im Frontend).

**PDF-Erzeugung: Telerik Reporting ist bereits lizenziert.** Damit werden die
dynamischen Angebots-PDFs aus dem Produktkatalog gerendert (Positionen aus der
Kalkulation, Briefanrede aus den Kontaktdaten); dieselbe Engine bietet sich für
Rechnungen und Mahnungen an. Für ZUGFeRD relevant: Telerik Reporting kann
PDF/A-3 erzeugen — das Rechnungs-XML wird separat erzeugt und eingebettet.

**Klickbares Mockup:** `mockup/index.html`
(veröffentlicht als Claude-Artefakt: https://claude.ai/code/artifact/6039a0c4-e0a5-4bfb-a960-d63b5553d7ec)

## Ausbauphasen

| Phase | Inhalt |
|---|---|
| 1 | Vertragsverwaltung + Kontaktverwaltung/Kundenstamm (Odoo-Import, Odoo-ID als Referenz) |
| 2 | Kunden-Timeline + E-Mail-Anbindung + Aufgabenverwaltung + Vorgänge (Bestellung → Teilaufgaben, manuelle Anlage) |
| 3 | Angebote, Produkte, CRM/Leads, Tickets (gleicher Unterbau wie Aufgaben); Vorgang automatisch aus gewonnenem Angebot |
| 4 | Abos, wiederkehrende Rechnungen, ZUGFeRD, Offene Posten, Auswertungen — Parallelbetrieb mit Odoo, dann Abschaltung |

Basisdienst (phasenübergreifend): E-Mail-Warteschlange.

## Kernidee: die Kundenakte

Eine Akte pro Kunde, **Verlauf links (breite Spalte), Fakten rechts** —
Layout-Entscheidung nach Ansicht beider Varianten.

- **Timeline über alle Kanäle** (der Hauptschmerz in Odoo): E-Mail, Anruf,
  Ticket, Angebot, Vertrag, Rechnung in einem Strom, filterbar.
- Jeder Eintrag zeigt das **Gesprächspaar**: welcher rausoft-Mitarbeiter mit
  welchem Kunden-Ansprechpartner (z. B. „D. Stauch (Vertrieb) ↔ Frau Berghoff (GF)“),
  bei Systemvorgängen Richtung/Empfänger („System → Einkauf“).
- Kopf der Akte zeigt sofort: Kundengruppe, **Betriebsart (Shared Cloud /
  Private Cloud / OnPrem)**, **Vertriebsweg** (Direktkunde oder farbiges Band
  „Endkunde via Partner · Rechnung an Partner“).
- Karte „Betrieb & Lizenzen“: Tier + Stammdaten-Volumen, gebuchte Module,
  Benutzerzahl — **abgeleitet aus den aktiven Abo-Positionen**, nicht manuell
  gepflegt.

## Kontaktverwaltung

- Kunden anlegen (Nummernkreis automatisch), Ansprechpartner pflegen.
- **Anrede + Titel + Vorname + Nachname als getrennte Felder** (Odoo-Ärgernis).
  Briefanrede wird immer daraus berechnet: „Sehr geehrte Frau X“ /
  „Sehr geehrter Herr Y“ / ohne Anrede „Sehr geehrte Damen und Herren“.
  Gilt überall: E-Mails, Mahnungen, Angebote, Serienbriefe.
- Rollen je Ansprechpartner (Vertrag, Technik/Tickets, Rechnungsempfang).
- Mehrere Adressen, inkl. abweichender Rechnungsanschrift.
- **CTI zur eigenen Telefonsoftware:** Klick auf Nummer wählt; nach Auflegen
  kommen Dauer/Richtung zurück, Gesprächsnotiz wird ergänzt, Eintrag landet in
  der Timeline.

### Achtung-Hinweise am Kunden (MP 26.11.2026)

> „Achtung, der Kunde muss immer mit Herr Dr. angesprochen werden“ ·
> „Die Abrechnung darf erst nach der Schulung erfolgen“ — so etwas muss sofort
> sichtbar sein. Bis dahin gab es den deutlichen Hinweis nur am Vorgang.

- **Hinweis für alle** (`KundenHinweis`): kurzer Text (höchstens 500 Zeichen,
  höchstens 5 je Kunde), den jeder aktive Mitarbeiter setzen und entfernen
  kann. Knopf **„⚠ Hinweis für alle“** oben in der Kundenakte — kein
  Untermenü.
- **Überall sichtbar, wo am Kunden gearbeitet wird** — dasselbe Band wie am
  Vorgang (`KundenHinweise`-Komponente): oben in der Kundenakte, im
  Mail-Fenster, am Vorgang und am Vertrag („Zum Kunden: …“), in „Meine
  Aufgaben“ an jeder Aufgabe des Kunden und im Outlook-Aufgabenbereich.
- **Nachvollziehbar:** Setzen und Entfernen stehen mit Text und Mitarbeiter im
  Verlauf der Akte.
- **Abgrenzung:** Eine *Vereinbarung* ist eine Zusage an den Kunden und wird
  erledigt; ein *Merkmal* ist ein festes Kennzeichen aus der Stammliste
  (Insolvenz, Einmalkauf); der *Hinweis zum Vorgang* gilt nur für diese eine
  Bestellung. Der Hinweis am Kunden gilt für alles, bis ihn jemand entfernt.
- **Grundsatz für die Bedienung** (MP 26.11.2026): Funktionen nicht hinter
  Textverweisen in Fenstern verstecken („wie in Odoo, das nervt“). Was jeder
  wissen muss, steht ungefragt oben.
- **„Meine Einstellungen“** (MP 26.11.2026, Seite `/meine-einstellungen`,
  Menüpunkt unten in der Navigation): der eine Ort für alles, was nur den
  angemeldeten Mitarbeiter betrifft — heute der eigene Benutzer (Name, Adresse,
  Bereich, Rollen; nur zur Ansicht, Pflege durch den Admin), die Darstellung
  hell/dunkel und die E-Mail-Signatur. **Neue persönliche Einstellungen kommen
  dorthin**, nicht in das Fenster der Funktion, die sie braucht.

#### Durchgang „aus Anwendersicht“ (26.11.2026)

Alle Bereiche auf versteckte und verschachtelte Funktionen durchgesehen (Menü,
Knöpfe, Textverweise, zugeklappte Abschnitte). MP folgt der vorgeschlagenen
Reihenfolge. **Erledigt im ersten Block:**

- **Menü in Gruppen:** oben die tägliche Arbeit (Meine Aufgaben, Kunden,
  Anfragen, Vorgänge, Angebote, Verträge, Partner), dann „Nachschlagen“
  (Vorlagen, Wiki), „Finanzen“ (Zahlungen, Margenrechner — nur für
  Berechtigte), „Technik & Verwaltung“ (Server, Administration), unten „Meine
  Einstellungen“.
- **„Anfragen“ ist ein eigener Menüpunkt** (vorher nur ein Knopf auf der
  Kundenliste).
- **Start mit „Meine Aufgaben“** statt mit der Kundenliste.
- **Vorlagen mit zwei Reitern:** Vertragsvorlagen · E-Mail-Textbausteine
  (`/vorlagen`, `/vorlagen/textbausteine`; die alte Adresse `/textbausteine`
  leitet um). Menüpunkt und Seitentitel heißen beide „Vorlagen“.
- **Aktionsleiste oben in der Kundenakte:** ✉ E-Mail · + Notiz / Telefonat ·
  + Aufgabe · + Vorgang · + Vertrag aus Vorlage · ⚠ Hinweis für alle. Die
  Formulare öffnen sich an ihrem Platz, die Seite springt hin.
- **Vorgang:** „⚠ Hinweis zum Vorgang“, „Odoo-Auftrag eintragen“ und „+ Schritt
  von Hand hinzufügen“ sind Knöpfe statt Textverweise.

**Erledigt im zweiten Block (27.11.2026):**

- **Vorbereitete Mails prüft die Akte von selbst**, sobald die Seite bei
  Microsoft angemeldet ist — beim Laden und beim Zurückkommen in den Reiter
  (etwa nach dem Senden in Outlook). Ohne Anmeldung steht im Verlauf eine
  deutliche Zeile mit dem Knopf „Jetzt prüfen“ (ein Anmeldefenster darf der
  Browser nur nach einem Klick öffnen). Der Textverweis „Entwürfe prüfen“
  ist entfallen.
- **Merkmale, Server und Individualentwicklung** haben in der Akte einen
  eigenen Abschnitt „Merkmale, Server & Besonderheiten“; der Knopf „✎
  Merkmale & Server“ im Kopf der Akte klappt ihn auf und springt hin. Vorher
  lagen sie am Ende von „Stammdaten & Adressen“.
- **Server mit zwei Reitern:** Server · SSL-Zertifikate (`/server`,
  `/server/zertifikate`; die alte Adresse `/zertifikate` leitet um).
- **Vorgangstypen:** Der Knopf auf der Seite Vorgänge heißt „Vorgangstypen
  einrichten“ und erscheint nur noch für die, die sie pflegen dürfen (Admin,
  Bereichsleiter). Der Platz in der Administration kommt mit deren Umbau.

**Erledigt im dritten Block (27.11.2026):**

- **Angebote und Zahlungen:** Alle Aktionen je Zeile sind Knöpfe (klein und
  zweitrangig neben der Hauptaktion). Bewusst **kein Menü „…“**: Das hätte
  wieder etwas versteckt.
- **Benennungen:** Menüpunkt und Seitentitel sind gleich („Angebote“,
  „Vorlagen“, „Server“). Je Handlung ein Wort: **Löschen** = endgültig weg
  (Aufgabe, Anfrage, hier angelegter Textbaustein); **Entfernen** = aus der
  Liste nehmen, wiederherstellbar (Textbaustein aus Odoo, Hinweis, Schritt);
  **Ausblenden** = hier nicht mehr zeigen, wieder einblendbar (Angebote,
  Zahlungseingänge — dort vorher „ignorieren“).
- **Administration als Übersicht mit Kacheln:** je Thema eine eigene Seite
  (`/admin/benutzer`, `/admin/server`, `/admin/odoo`, `/admin/mail`,
  `/admin/angebote-zahlungen`, `/admin/verzeichnisse`), dazu Kacheln für
  Outlook & Kundenmails und Vorgangstypen. Der Stand steht auf der Kachel
  („Kundenmails: Probebetrieb“, „SMTP eingetragen“, „3 Server“).

**Noch offen:** Im Odoo-Teil der Administration stehen Verbindung und „Daten
übernehmen“ weiter auf einer Seite untereinander — trennen, wenn es stört.

### Individualentwicklung & Preis-Sonderabsprachen (ergänzt 09/2026)

- **Individualentwicklung:** Einige Kunden haben eine Individualentwicklung
  erhalten — Kennzeichen am Kunden mit Beschreibung (was wurde entwickelt),
  in der Kundenakte sofort sichtbar.
- **Preis-Sonderabsprachen, zeitlich befristet:** z. B. 50 % Rabatt in den
  ersten 2 Jahren oder fester Abzugsbetrag in den ersten 2 Jahren.
  Hinterlegbar am Kunden/Vertrag (Gültig-von/-bis, Typ Prozent oder Betrag),
  auswertbar analog Sondervereinbarungen; fließt in Phase 4 in Abo-Abrechnung
  und MRR-Berechnung ein.

## Partner & Reseller

- **CSB:** Tipp-Provision 15 % — Partner stellt rausoft die Provision in
  Rechnung; Endkunde bleibt Vertrags-/Rechnungspartner von rausoft.
- **FineSolutions (Schweiz):** Vertriebspartner — rausoft stellt die Rechnung an
  FineSolutions, die **Endkunden werden trotzdem als Kunden geführt**
  (Shared-Cloud-Einrichtung, Support, Timeline).
- Datenmodell: Kunde und Rechnungsempfänger sind getrennte Beziehungen;
  Partner ist eigener Kontakttyp mit Provisionsmodell.
- Auswertungen: aufgelaufene Provisionen je Partner, MRR über Partner.

## Vertragsverwaltung

- Typischer Abschluss: **Lizenzvertrag inkl. AVV**, optional NDA, optional
  Sondervereinbarungen.
- Bestandteile **typisiert** speichern → Auswertung „Wer hat eine
  Sondervereinbarung?“ (explizite Anforderung), Filter mit NDA / über Partner.
- Versionen unveränderlich (neue Version statt Überschreiben), Nachträge,
  Anhänge. Dateien bleiben im bestehenden Ablageverzeichnis; System speichert
  Pfad + SHA-256-Prüfsumme.
- Laufzeiten, Kündigungsfristen, automatische Erinnerungen.

### Verträge umbenennen und zusammenfassen (Entscheidungen 24.11.2026, MP)

Anlass: Bei manchen Kunden stehen viele Verträge einzeln untereinander, die als
Anhang zusammengehören — bisher musste jeder geöffnet werden.

- **Bezeichnung:** Jeder Vertrag bekommt eine frei änderbare Bezeichnung
  („Wartungsvertrag ID.prove“), die überall neben der Nummer steht. Neue Verträge aus
  einer Vorlage tragen den Vorlagennamen als Vorbelegung. Reine Handpflege — Bestandsscan
  und Odoo-Import überschreiben sie nicht. Einzelne Dokumente einer Akte lassen sich wie
  bisher umbenennen (Titel des Bestandteils).
- **Gruppieren statt verschmelzen:** Ein Vertrag wird einem Hauptvertrag desselben Kunden
  (bzw. Partners) als Anhang zugeordnet und kann jederzeit wieder gelöst werden. Jeder
  Vertrag behält Nummer und Dokumente — die Vertragsnummer steht im erzeugten PDF, und
  unterschriebene Dokumente bleiben unverändert. Genau eine Ebene: kein Anhang am Anhang.
- **Anhang folgt dem Hauptvertrag:** Status, Laufzeit und Kündigung gelten vom
  Hauptvertrag und werden nur dort gepflegt. Anhänge zählen nicht extra bei „Laufzeit
  nachzupflegen“, „Ende in 90 Tagen“ und der Kundenkategorie.
- **Ohne Öffnen sichtbar:** Vertragsliste, Kundenakte und Partnerakte zeigen den
  Hauptvertrag mit eingerückten Anhängen und die Dokumente jeder Akte mit Art, Titel
  und Datum (bis fünf direkt, mehr auf einen Klick).
- **Rechte:** wie das übrige Vertragsbearbeiten. Besteht ein Vertrag nur aus
  vertraulichen Bestandteilen, brauchen Umbenennen und Zuordnen die Rolle
  „Vertrauliche Verträge“. Die Bezeichnung selbst sehen alle (wie die Kurzinfo).
- **Dokumente innerhalb einer Akte gruppieren (ergänzt 24.11.2026, MP: „Ja, bauen“):**
  Ein Dokument lässt sich einem anderen derselben Akte als **Anlage** zuordnen (z. B.
  das Preisblatt zu einem bestimmten Nachtrag) — im Formular „Umbenennen · Zuordnen“
  über „Anlage zu“, dort auch wieder lösbar. Anlagen stehen in Vertragsansicht, Listen
  und Kundenakte eingerückt unter ihrem Hauptdokument. Gleiche Regeln wie bei Verträgen:
  nicht an sich selbst, nur innerhalb derselben Akte, genau eine Ebene. Dokumentart,
  Datum und Dateien bleiben unverändert. Ist eines der beiden Dokumente vertraulich,
  braucht es die Rolle „Vertrauliche Verträge“. Der Bestandsscan ordnet nichts von
  selbst zu und ändert keine Zuordnung.

## Aufgabenverwaltung (Muster: rausoft-Service-Portal)

- Zuweisung an **Person oder Bereich** (Vertrieb, Support, Buchhaltung …).
  Bereichs-Aufgaben liegen im Pool; ein freier Mitarbeiter **übernimmt** oder
  weist gezielt zu (Beispiel: Rückrufbitte aus dem Portal → Pool Vertrieb).
- Status: offen, in Arbeit, Rückfrage, zurückgestellt, **wartet auf Kunden**
  (ergänzt 28.10.2026, mit Wiedervorlage-Datum), erledigt.
- **Verlaufseinträge** an der Aufgabe („heute telefonisch nicht erreicht“),
  Kommentare/Rückfragen als Feedback-Schleife; Statuswechsel werden protokolliert.
- **Wiedervorlage** und **„Zurückstellen bis“**: am Stichtag taucht die Aufgabe
  automatisch wieder als offen auf.
- Bei Kundenbezug erscheinen Einträge auch in der Kunden-Timeline.
- Tickets (Phase 3) nutzen denselben Unterbau.

## Vorgänge: Bestellungen und ihre Teilaufgaben (Konzept 28.10.2026)

> **Ausgangslage:** Aus einer Bestellung entstehen Teilaufgaben für mehrere
> Bereiche — Kunde in der Shared Cloud anlegen, Vertrag zusenden, Rechnung
> stellen, Schulungstermin vereinbaren. In Odoo wird dafür je Bestellung ein
> Projekt angelegt (automatisch, sobald der CRM-Eintrag „gewonnen" ist). Das
> ist unübersichtlich: leerer Container, Aufgaben von Hand getippt, keine
> feste Reihenfolge, keine Sicht über alle Bestellungen hinweg.

### Kernidee: Vorgang mit Vorlage statt leerem Projekt

- Ein **Vorgang** ist ein typisierter Container am Kunden, der seine
  Teilaufgaben aus einer **Vorlage** bekommt. Vorgangstypen (Entscheidung
  28.10.2026): **Neue Bestellung** (Neukunde), **Upgrade/Upsell**,
  **Stundenauftrag** (Projekt ohne Software), **Verkauf ohne
  Zwischenschritte** (nur Rechnung stellen); später z. B.
  „Kündigung/Offboarding". Die Liste ist Vorlagenpflege, kein Code.
- Eine Vorlage ist eine Liste von **Schritten**: Titel, zuständiger Bereich,
  Reihenfolge, Frist relativ zum Start oder zum Vorgänger-Schritt.
- **Schritte werden zu normalen Aufgaben** im Unterbau der Aufgabenverwaltung
  (Bereichs-Pool, übernehmen, Verlauf, Wiedervorlage). Zusätzliche Felder:
  Vorgang, Reihenfolge, „wartet auf Schritt X".
- **Abhängigkeiten sind optional je Schritt:** Ein Schritt kann auf einen
  Vorgänger warten; bis dahin ist er im Vorgang sichtbar (grau), liegt aber in
  keinem Pool. Schritte ohne Vorgänger starten sofort parallel. Die
  Reihenfolge ist Sache der Vorlage, nicht fest verdrahtet — **die Rechnung
  kann sofort oder parallel zum Vertrag** gestellt werden (Entscheidung
  28.10.2026), eine Abhängigkeit „erst nach Vertrag" ist nur eine Option.
- **Bedingte Schritte:** Vorlagen können Schritte an Inhalte knüpfen
  (Schulungstermin nur, wenn Schulung bestellt). Mit dem Angebotsmodul
  (Phase 3) steuern das die Angebotspositionen automatisch, vorher Häkchen
  beim Anlegen.
- **„Wartet auf Kunden" ist ein eigener Aufgabenstatus** (Entscheidung
  28.10.2026), den der Bearbeiter bewusst setzt, mit Wiedervorlage-Datum —
  z. B. Vertrag liegt beim Kunden. Keine rote Ampel, aber am Stichtag wieder
  offen. Ergänzt die Status-Liste der Aufgabenverwaltung.
- **Vorgangsstatus wird berechnet, nicht gepflegt:** Neu · Läuft · Wartet auf
  Kunden (alle noch offenen Schritte warten auf den Kunden, keiner
  überfällig — sonst „Läuft", weil intern noch jemand dran ist) · Hängt
  (mindestens ein Schritt überfällig) · Abgeschlossen (alle Schritte
  erledigt oder übersprungen).

### Auslöser und Weitergabe

- **Manuell anlegen** ist der Normalweg, solange Odoo läuft (siehe Übergang
  unten) und bleibt danach für Bestellungen ohne Angebot: Kunde wählen,
  Vorlage wählen, bedingte Schritte ankreuzen, optional Odoo-Auftragsnummer
  (S…) eintragen. Schon in Phase 2.
- **Angebot gewonnen → Vorgang automatisch** (wie heute CRM → Projekt), nur
  gleich mit allen Schritten gefüllt. Kommt erst mit dem eigenen
  Angebotsmodul (Phase 3), also nach dem Übergang.
- **Automatisch abhaken, wo das System es weiß:** Rechnung erzeugt → Schritt
  „Rechnung stellen" erledigt; Vertragsstatus „unterschrieben" → Schritt
  erledigt. Externes (Shared Cloud) bleibt ein manueller Haken, optional mit
  Pflichtfeld (z. B. Mandanten-ID), damit die Information in der Akte landet.
- **Weitergabe:** Ist ein Vorgänger-Schritt erledigt, erscheint der nächste im
  Pool des folgenden Bereichs. Ob zusätzlich benachrichtigt wird, ist **je
  Schritt in der Vorlage auswählbar** (keine / Mail / Teams; Entscheidung
  28.10.2026 — welcher Weg sich bewährt, ist noch offen). Kein Pingpong über
  Chatter nötig.

### Übergang: Parallelbetrieb mit Odoo (Entscheidung 28.10.2026)

Der Übergang dauert mindestens noch ein Jahr. In dieser Zeit bleibt Odoo
führend für **Angebot, Auftragsbestätigung und Rechnungsversand**; die
Kundenzentrale übernimmt nur die Abwicklung der Teilaufgaben.

- **Vorgang manuell anlegen**, sobald in Odoo der Auftrag bestätigt ist:
  Kunde, Vorlage, bedingte Schritte, **Odoo-Auftragsnummer (S…)**. Über die
  Nummer bekommt der Vorgang denselben Strang-Schlüssel wie der importierte
  Odoo-Verlauf („Verkauf S01194") — Mails, Aktivitäten und Schritte liegen in
  der Akte in einem Strang.
- **Kein Odoo-Projekt mehr** für neue Bestellungen ab dem Umstellungstag.
  Bereits laufende Odoo-Projekte werden dort zu Ende geführt, nicht
  importiert (bei Bedarf einzeln als Vorgang nachgelegt).
- **Schritt „Rechnung stellen" heißt im Übergang „Rechnung in Odoo
  stellen"** und wird vom System abgehakt, wenn der wiederholbare
  Odoo-Import das Ereignis „Rechnung gestellt" zum Auftrag findet
  (account.move am sale.order, Importer kennt das bereits). Vertragsschritte
  hakt das eigene Vertragsmodul ab.
- **Ausbaustufe (beschlossen 28.10.2026), wenn der Import regelmäßig
  läuft:** Liste „Bestätigte Odoo-Aufträge ohne Vorgang" auf dem Dashboard,
  Anlage per Klick mit Vorlagenwahl. Bewusst Vorschlag statt stiller
  Automatik. **Filter: nur Aufträge, zu denen noch keine Rechnung existiert**
  — Abo-Verlängerungen schreibt Odoo automatisch die Rechnung, die sollen
  nicht in der Liste auftauchen. Zusätzlich „ausblenden" je Auftrag für
  Fälle, die keinen Vorgang brauchen. Der Importer liest sale.order mit
  Status und die Rechnungen (account.move) zum Auftrag schon heute.
- Nach der Odoo-Abschaltung ersetzt das eigene Angebotsmodul (Phase 3) den
  manuellen Weg: Angebot gewonnen → Vorgang automatisch.

### Sichtbarkeit und Dashboard

Klickbares Mockup: `mockup/vorgaenge.html` (Übersicht mit Odoo-Vorschlagsliste,
Vorgang anlegen, Vorgangsansicht, Vorlagen-Editor; Stand 28.10.2026).

- **Vorgangsansicht:** eine Zeile pro Schritt mit Bereich, Zuständigem,
  Status, Frist und letztem Verlaufseintrag — „Wo steht die Bestellung X?"
  in zwei Sekunden.
- **Dashboard „Laufende Vorgänge":** Tabelle, keine Kanban-Wand. Spalten:
  Kunde, Typ, gestartet, Fortschritt (3 von 5), nächster offener Schritt mit
  Bereich, Ampel (überfällig / wartet auf Kunden). Filter nach Bereich und
  „nur wo ich dran bin"; hängende Vorgänge zuerst.
- **Meine Aufgaben / mein Bereich** bleibt die zweite Ansicht; Vorgangsschritte
  sind dort gewöhnliche Aufgaben mit Link auf den Vorgang.
- **In der Kundenakte:** Der Vorgang ist ein Strang im Verlauf (wie „Verkauf
  S01194"). „Was lief zuletzt?" zeigt die offenen Schritte, erledigte Schritte
  wandern als Einträge in den Verlauf.

### Vorgang im Einzelfall anpassen (Ergänzung 28.10.2026)

Die Vorlage ist nur der Startpunkt. Beim Anlegen (und Vorschlag: auch später
am laufenden Vorgang) kann der Bearbeiter den einzelnen Vorgang anpassen,
ohne die Vorlage zu ändern:

- **Schritt entfernen:** Der Schritt entfällt nur in diesem Vorgang (mit
  Vermerk „von Hand entfernt" im Verlauf). Wartet ein anderer Schritt auf ihn,
  startet dieser sofort bzw. hängt sich an den davorliegenden Schritt.
- **Manuellen Schritt hinzufügen:** Titel, Bereich, Frist, optional Person,
  optional „wartet auf Schritt X". Manuelle Schritte sind im Vorgang als
  solche erkennbar und verhalten sich sonst wie Vorlagenschritte (Pool,
  Status, Wiedervorlage).
- **Hinweis zum Vorgang** (z. B. „ACHTUNG: alten Vertragsstand versenden"):
  Freitext, der **überall sichtbar** ist, wo am Vorgang gearbeitet wird — im
  Kopf der Vorgangsansicht, als Markierung in der Übersicht und bei jedem
  Schritt in „Meine Aufgaben"/Bereichs-Pool, damit ihn auch der Kollege aus
  der Technik oder Buchhaltung sieht. Änderungen am Hinweis landen im Verlauf.
  Passt zur Regel, dass alte unterschriebene Vertragsstände gültig bleiben.

### Vorlagenpflege und Abgrenzung

- **Vorgangstypen sind pflegbar** (Ergänzung 28.10.2026): neue anlegen,
  umbenennen, Schritte ändern, eine bestehende Vorlage als Ausgangspunkt
  kopieren. Nicht mehr benötigte Typen werden **deaktiviert statt gelöscht**
  — laufende und abgeschlossene Vorgänge verweisen weiter darauf, für neue
  Vorgänge stehen sie nicht mehr zur Auswahl.
- Vorlagen werden in der Oberfläche gepflegt, nicht im Code — durch **Admin
  und Bereichsleiter** (je Bereich eine definierte Person; Entscheidung
  28.10.2026, Ergänzung des Rollenmodells: Bereich bekommt ein Feld
  „Leiter"). Laufende Vorgänge behalten ihre Schritte, auch wenn die Vorlage
  später geändert wird (analog zur Regel bei Vertragsvorlagen: kein
  „veraltet").
- Tickets = Ungeplantes von außen; Vorgänge = Geplantes nach Vorlage. Beide
  nutzen dieselbe Aufgabe.
- Einordnung: Aufgaben Phase 2, Vorgänge mit manueller Anlage ebenfalls
  Phase 2, Automatik aus gewonnenem Angebot Phase 3.

### Datenmodell-Skizze

- `Vorgang`: Kunde, Vorlage, Titel, berechneter Status, optionale Verweise
  auf Angebot und Vertrag, **Odoo-Auftragsnummer** (Übergang), Start,
  Abschluss, Strang-Schlüssel, **Hinweis** (Freitext, optional).
- `VorgangsVorlage` + `VorlagenSchritt`: Titel, Bereich, Reihenfolge, Frist in
  Tagen, „wartet auf" (Schritt, optional), Bedingung, Benachrichtigung
  (keine / Mail / Teams); Vorlage zusätzlich **Aktiv** (Deaktivieren statt
  Löschen).
- `Aufgabe` zusätzlich: Vorgang, Reihenfolge, „wartet auf Aufgabe",
  **Herkunft** (Vorlage / manuell hinzugefügt), Vermerk „von Hand entfernt";
  neuer Status „Wartet auf Kunden".
- `Bereich` zusätzlich: Leiter (Mitarbeiter) für die Vorlagenpflege.

### Umsetzungsstand (28.10.2026)

Erste Ausbaustufe in der App (Navigation „Vorgänge"):

- **Fertig:** Übersicht mit Kennzahlen, Bereichsfilter, „Nur wo ich dran bin",
  Abgeschlossene; Vorgang anlegen mit Kundensuche, Typwahl, Bedingungen,
  Odoo-Auftragsnummer (gleicher Strang wie der Odoo-Verlauf, doppelte laufende
  Nummer wird abgewiesen), Hinweis, Schritte entfernen/zurückholen und von Hand
  ergänzen; Vorgangsansicht mit Übernehmen, Zuweisen/zurück in den Pool,
  Erledigt (mit Pflichtfeld), Wartet auf Kunden mit Wiedervorlage, Notiz,
  Entfernen, Wieder öffnen, Schritt hinzufügen, Hinweis bearbeiten; Verlauf je
  Vorgang (landet auch in der Kundenakte); Vorgangstypen pflegen (anlegen,
  Schritte bearbeiten mit Stand, kopieren, deaktivieren, vier Standardtypen per
  Knopf); Bereichsleiter in der Administration; Kundenakte zeigt aktive Schritte
  mit Link zum Vorgang und „+ Vorgang".
- **Dazu (28.10.2026, vor dem Testbetrieb):** Seite **„Meine Aufgaben"** (bei mir +
  unbesetzt im Pool meines Bereichs, umschaltbar auf den ganzen Bereich oder einen
  anderen Bereich; Gruppen überfällig/heute/7 Tage/später/wartet auf Kunden;
  Vorgangshinweis bei jedem Schritt; Übernehmen und Erledigt direkt in der Liste)
  mit Abzeichen in der Navigation (bis heute fällig). **Abgleich mit Odoo**
  (stündlich, nach jedem Chatter-Import und per Knopf in der Übersicht; nur
  lesend): Vorgänge, die vor dem Import des Odoo-Verlaufs angelegt wurden, werden
  nachträglich an dessen Strang gehängt; „Rechnung in Odoo stellen" hakt sich ab,
  sobald Odoo eine gebuchte Ausgangsrechnung mit der Auftragsnummer in der Herkunft
  (invoice_origin) hat — exakt über die Nummer, nicht über den Strang, damit
  Abo-Rechnungen derselben Verkaufschance nicht fälschlich abhaken.
- **Auftragsauswahl (28.10.2026, Wunsch aus dem ersten Test):** Beim Anlegen und im
  Vorgang („eintragen"/„ändern") werden die bestätigten Odoo-Aufträge des Kunden
  zur Auswahl angezeigt (live aus Odoo über den Firmenpartner und seine Kontakte,
  neueste 20, mit Datum, Abo-Kennzeichen und gebuchten Produkten, ohne Preise);
  ist Odoo nicht erreichbar, aus importierten Abos und dem Odoo-Verlauf der Akte.
  Nummern mit laufendem Vorgang sind gesperrt und verlinkt; freie Eingabe bleibt.
  Nachträglich eingetragene Nummern ziehen Titel und Verlaufsstrang mit.
- **Noch offen:** Benachrichtigung bei Weitergabe (Einstellung wird gespeichert,
  Versand fehlt); Abhaken bei „Vertrag unterschrieben" (das Vertragsmodul kennt
  diesen Status noch nicht); Ausbaustufe „Bestätigte Odoo-Aufträge ohne Vorgang".
- **Gesammelt im Testbetrieb** (Punkte 1–3 umgesetzt am 29.10.2026, siehe
  „Umsetzung" je Punkt):
  1. *Nachrichten aus dem Vorgang in der Kundenakte sichtbar* (MP, 28.10.2026):
     Was im Vorgang eingegeben wird (Hinweis zum Vorgang, Notizen zu Schritten),
     soll auch in der Akte bei „Offene Aufgaben" stehen, damit es jeder sofort
     sieht. Heute landet es nur im Verlauf (Strang des Vorgangs); die offenen
     Aufgaben zeigen nur Titel, Frist und Zuständigkeit. Vorschlag: Hinweis des
     Vorgangs als ⚠-Zeile über dessen Schritten, letzte Notiz je Schritt
     gekürzt darunter (Klick führt in den Vorgang).
     **Umsetzung:** Am ersten offenen Schritt je Vorgang stehen der Hinweis (⚠)
     und die letzte Nachricht aus dem Vorgang (Notiz, Notiz beim Erledigen oder
     beim „Wartet auf Kunden") mit Verfasser und Zeitpunkt, je mit Link in den
     Vorgang. Einmal je Vorgang statt je Schritt, weil Notizen am Vorgang hängen.
  2. *Wiki-Einträge übersichtlicher* (MP, 28.10.2026; Beispiel Sanktionsliste
     „MEUL"): Der Steckbrief oben ist gut, darunter erschlägt der Abschnitt
     „Bei uns → Prozess". Befund: (a) Der Standardprozess der Importart
     („automatisch (Sanction Admin)") wird bei *jeder* Liste dieser Art in voller
     Länge eingeblendet — derselbe lange Text dutzendfach, mit DEB-Beispiel auf
     der MEUL-Seite. (b) Alle Wiki-Texte sind reiner Text ohne Formatierung:
     keine Überschriften, Listen, Hervorhebungen; JSON-Konfiguration steht als
     Fließtext. (c) Der Text läuft über die volle Breite unter einem kleinen
     Label. Vorschlag: Standardprozess bei der Liste nur als Verweis/aufklappbar
     („Standardprozess automatisch (Sanction Admin) ▸"), voll nur einmal oben;
     bei der Liste nur die Abweichungen bzw. listeneigenen Werte (z. B. URL,
     Dateiformat) als kleine Tabelle; einfache Formatierung für Wiki-Texte
     (Markdown: Überschriften, Aufzählungen, **fett**, Codeblöcke für
     Konfiguration) mit Vorschau beim Bearbeiten; lange Texte auf lesbare
     Zeilenbreite begrenzen. Prüfen, ob das auch für die übrigen Wiki-Seiten gilt.
     **Umsetzung:** Standardprozess bei der Liste nur noch als Zeile plus
     „Standardprozess anzeigen" (aufklappbar), voll einmal oben. Einfache
     Formatierung (`WikiFormat`: Überschriften, Aufzählungen, fett, Code,
     Codeblöcke, Tabellen, Links) für Prozesse, Beschreibung/Relevanz der
     Sanktionslisten und den Prozess der Preisliste, mit Kurzhilfe und Vorschau
     beim Bearbeiten; ungeformte JSON-Konfiguration wird automatisch Codeblock;
     Zeilenbreite begrenzt. Text wird vollständig HTML-kodiert (kein eigenes HTML).
     Offen: listeneigene Werte (URL, Format) als eigene Felder statt Freitext.
  3. *Kundenakte „Was lief zuletzt?" besser* (MP, 28.10.2026): Befund: (a) Die
     Kurzfassung mischt Zählungen — „4 Kontakte" sind Gespräche, „2 bei rausoft ·
     3 beim Kunden" sind Personen; liest sich wie „4 Ansprechpartner". (b) Die
     Wer-mit-wem-Tabelle braucht viel Platz für wenig Aussage (meist „1×", viele
     „–"), die Kopfzeile „rausoft ↓ · Kunde →" ist kryptisch, dass Zellen
     filtern, sieht man nicht. (c) Das Wichtigste fehlt: *worum* es zuletzt ging
     (Betreff). (d) Nullwerte („0 offene Vereinbarungen") und die offenen
     Aufgaben, die direkt darunter ohnehin stehen, sind Rauschen. Vorschlag:
     Kopfzeile „4 Gespräche mit 3 Personen beim Kunden · letztes vor 35 Tagen";
     darunter die letzten 2–3 Gespräche je eine Zeile (wann · wer ↔ wer · Art ·
     Betreff, Klick öffnet den Eintrag); statt der Matrix eine kurze Liste je
     Ansprechpartner beim Kunden („Herr Braun — zuletzt vor 35 Tagen mit Daniel
     Stauch, 1×"), Klick filtert den Verlauf; Matrix höchstens als Umschalter
     bei vielen Beteiligten; Nullwerte weglassen. Layout-Entscheidung „Verlauf
     links, Fakten rechts" bleibt unberührt.
     **Umsetzung:** wie vorgeschlagen — „N Gespräche mit M Personen beim Kunden
     · letztes vor …", Überfälliges und offene Vereinbarungen nur wenn > 0;
     „Zuletzt" mit den letzten 3 Gesprächen (Klick öffnet den Eintrag im
     Verlauf); „Ansprechpartner" je Person mit letztem Kontakt und Gesprächspartnern
     bei uns (Klick filtert den Verlauf auf die Person); Tabelle „Wer mit wem"
     nur noch per Umschalter, wenn auf beiden Seiten mehrere Personen beteiligt sind.

### Entscheidungen 28.10.2026 (MP)

1. **Vorgangstypen:** Neue Bestellung, Upgrade/Upsell, Stundenauftrag
   (Projekt ohne Software), Verkauf ohne Zwischenschritte (nur Rechnung).
2. **Reihenfolge nicht fest:** Rechnung kann sofort oder parallel gestellt
   werden; Abhängigkeiten sind je Schritt optional.
3. **„Wartet auf Kunden"** wird eigener Aufgabenstatus.
4. **Vorlagenpflege:** Admin und Bereichsleiter (definierte Person je Bereich).
5. **Benachrichtigung bei Weitergabe:** je Schritt auswählbar — welcher Weg
   gebraucht wird, zeigt die Praxis.
6. **Übergang (mind. 1 Jahr):** Angebot, Bestätigung und Rechnung bleiben in
   Odoo; Vorgänge werden in der Kundenzentrale manuell angelegt (mit
   Odoo-Auftragsnummer), kein Odoo-Projekt mehr für neue Bestellungen.
7. **Einzelfall anpassen:** Beim Anlegen Schritte entfernen, manuelle
   Schritte hinzufügen und einen gut sichtbaren Hinweis zum Vorgang erfassen
   (z. B. „ACHTUNG: alten Vertragsstand versenden").
8. **Vorgangstypen pflegbar:** neue anlegen, ändern, kopieren; deaktivieren
   statt löschen.

### Offene Angebote und Aufträge (Entscheidung 26.11.2026, MP)

> Auslöser: In Odoo werden Angebote gestellt und teils auch bestellt, aber
> niemand fasst nach und niemand startet das Projekt.

Seite **Angebote** (`/angebote`, jeder aktive Mitarbeiter, ohne Preise), live
aus Odoo, nur lesend (`OffeneVerkaeufe`, `VerkaufsUeberwachung`):

- **Bestätigte Aufträge, die niemand gestartet hat** — die am 28.10.2026
  beschlossene Liste: in Odoo bestätigt, noch keine Rechnung (Abo-Verlängerungen
  fallen so heraus), kein Vorgang mit dieser Auftragsnummer **und kein Projekt
  in Odoo** (siehe nächster Punkt). „Vorgang anlegen“ springt mit Kunde und
  Auftragsnummer vorbelegt in die Anlage. Bewusst Vorschlag statt stiller
  Automatik.
- **Aufträge, die als Projekt in Odoo laufen** (MP 26.11.2026): Solange der
  Umstellungstag nicht erreicht ist, legt Odoo das Projekt mit seinen Aufgaben
  selbst an, sobald die Verkaufschance als gewonnen bestätigt wird. Die Seite
  zeigt zu jedem bestätigten Auftrag ohne Vorgang den Stand dieses Projekts:
  Name, Leitung, Phase, erledigte Aufgaben, letzte Bewegung — was am längsten
  stillsteht, oben. **Nur Anzeige, keine Kopie:** Es entsteht kein Vorgang, und
  die Entscheidung vom 28.10.2026 bleibt (laufende Odoo-Projekte werden dort zu
  Ende geführt, nicht importiert). Ein Auftrag, dessen Projekte in Odoo
  archiviert oder als erledigt gemeldet sind, erscheint nicht mehr.
  Zuordnung (`AuftragsProjekte`), sicherste Spur zuerst: Odoo verknüpft Projekt
  und Auftrag selbst (Auftrag oder Auftragsposition) → das Projekt hängt an der
  Verkaufschance des Auftrags → **vermutet** über Kunde und Zeitpunkt (Projekt
  desselben Kunden, frühestens 3 Tage vor dem Auftrag bzw. vor dem Gewinn der
  Verkaufschance; jedes Projekt nur an einem Auftrag). Vermutete Zuordnungen
  sind als solche gekennzeichnet. Welche Verknüpfungsfelder es gibt, fragt der
  Leser bei Odoo ab — keine festen Feldnamen einer Odoo-Einrichtung, keine
  festen Nummern. Sind die Projekte nicht lesbar (Rechte des Odoo-Benutzers),
  steht ein Hinweis auf der Seite und die Liste verhält sich wie zuvor.
  **Offen:** gegen das echte Odoo prüfen, über welche Spur die Zuordnung dort
  tatsächlich läuft; Aufgabe oder Hinweis bei Stillstand (Frist als
  Einstellung) erst, wenn die Anzeige sich bewährt hat.
- **Angebote ohne Rückmeldung** — in Odoo gesendet, weder bestätigt noch
  storniert; mit Alter, Gültigkeit, Verkäufer.
- **Nachfass-Aufgabe:** nach 30 Tagen ohne Reaktion (einstellbar) beim Verkäufer des Angebots
  (Zuordnung Odoo-Login = E-Mail des Mitarbeiters; sonst steht der Name an der
  Aufgabe). Erledigt und Angebot weiter offen → nach weiteren 30 Tagen die nächste.
  Solange eine offen ist, entsteht keine zweite. Wird das Angebot in Odoo
  bestätigt oder storniert, erledigt sich die Aufgabe von selbst (Eintrag in
  der Akte). Regeln: `NachfassRegeln`; stündlicher Takt und Knopf.
- **Ausblenden** je Angebot/Auftrag mit Grund (braucht keinen Vorgang, Kunde
  entscheidet später); ausgeblendete bekommen keine Aufgaben und lassen sich
  wieder einblenden (`VerkaufsMerker` — gespeichert wird nur die Odoo-Nummer).
- **Wochenübersicht per Mail** an die eingetragenen Empfänger (nur erlaubte
  Domänen wie bei den Zertifikats-Mails).
- Einstellungen `Verkauf.*` unter Administration → „Offene Angebote und
  Kontoauszüge“: Frist, Wiederholung, Rückblick (180 Tage), Empfänger, Wochentag.
- Das Angebotsdatum ist Odoos `date_order`; ein eigenes „gesendet am“ führt
  Odoo nicht. Mit dem eigenen Angebotsmodul (Phase 3) entfällt die Odoo-Abfrage,
  die Regeln bleiben.

## Wiki

„Wie bei uns was funktioniert und wie wir es nennen." Themen stehen in
`Domain/Wiki/WikiThema.cs` (Sanktionslisten, Preisliste, Kommunikationsleitlinien,
FAQ Support); Entstehung und Einzelheiten der ersten Themen in `FEEDBACK.md`
Nr. 22. Gemeinsame Bausteine: Neuerungen mit Lese-/Bestätigungsprotokoll,
Meldungen an die Zuständigen, Editor (`WikiEditor`/`WikiFormat`).

### Kommunikationsleitlinien (Entscheidungen 10.11.2026, MP)

Sprachregeln für die Kommunikation mit Kunden: welche Wörter wir verwenden,
was wir besser vermeiden, welche Aussagen nie getroffen werden dürfen und
welche Hinweise in bestimmten Situationen fallen müssen — für die Mitarbeiter
in Vertrieb und Support und als Vorgabe für die KI.

1. **Ein gemeinsamer Regelbestand.** Mitarbeiter und KI bekommen dieselben
   Regeln; jede Regel trägt Zielgruppen-Kennzeichen (Vertrieb, Support, KI).
   Keine getrennten Listen, die auseinanderlaufen.
2. **Freigabe ausschließlich durch Compliance und Admin** — für alle Arten.
   Die Wiki-Redaktion gibt hier nicht frei. Vorschlagen (als Entwurf) darf
   jeder Mitarbeiter.
3. **Das KI-System ist Claude.** Stufe 1: Die Regeln gehen als kopierbarer
   Textblock in den Systemprompt bzw. die Projektanweisungen (Export, nur
   Admin/Compliance). Automatischer Abruf mit Maschinenzugang folgt später.
4. **Lesebestätigung ist Pflicht** für verbotene Aussagen und Pflichthinweise:
   Mit der Freigabe müssen die Mitarbeiter der betroffenen Rollen „Gelesen und
   verstanden" bestätigen; Admin/Compliance sehen je Regel, wer fehlt.

Umsetzung (10.11.2026): Entität `Sprachregel` mit den Arten Verwenden,
Vermeiden, Verboten, Pflichthinweis, Grundsatz; festes Themen-Raster
(`SprachregelKatalog`), optional Produkt und Kanäle; Rechte in
`SprachregelRegeln`. **Freigegebene Regeln werden nie überschrieben:** Ändern
heißt „Neu fassen" (neuer Entwurf mit Verweis auf den Vorgänger); mit dessen
Freigabe wird die alte Regel zurückgezogen. Eine geltende Regel lässt sich
nicht löschen, nur zurückziehen. Freigabe/Rücknahme erzeugen in derselben
Transaktion eine Wiki-Neuerung; dafür kennen Neuerungen jetzt **Zielrollen**
(leer = alle Mitarbeiter wie bisher) — betroffen sind Rolle Vertrieb bei
Zielgruppe Vertrieb und Rolle Support bei Zielgruppe Support, ist nur KI
gewählt, entsteht keine Neuerung. **Export** (`SprachregelExport`,
`GET api/wiki/leitlinien/export?zielgruppe=ki|vertrieb|support&format=prompt|json`):
nur freigegebene Regeln mit erreichtem „gültig ab"; interne Begründungen
bleiben im Haus; der Text ist byte-stabil (feste Sortierung, keine
Erzeugungszeit), damit er als Prompt-Cache-Präfix gültig bleibt, solange sich
keine Regel ändert. Kundennamen und Preise gehören nicht in Leitlinien.

**Bewusst noch nicht gebaut:** Textprüfer für Mail-Entwürfe (dafür gibt es je
Regel `Varianten`), Druckansicht, automatischer Abruf mit API-Key (der
JSON-Export ist dafür vorbereitet).

## Rechnungen & Offene Posten

- Versand nur per E-Mail als **ZUGFeRD**; Postversand = drucken + manuell.
- **Automatische Kopie an DATEV Unternehmen Online** bei jedem Versand.
- Original unveränderlich, fortlaufender Nummernkreis, Korrektur nur per
  Storno/Gutschrift.
- **Bank-CSV-Import** halbautomatisch: Vorschlag über Rechnungsnummer + Betrag,
  Zuordnung per Klick; Sonderfälle (Sammelüberweisung) manuell → erzeugt Aufgabe.
- **Mahnung auf Knopfdruck** als druckfertiges PDF (Versand meist manuell per
  Post); Offene-Posten-Liste reicht als Übersicht.

### Kontoauszüge im Übergang mit Odoo (Entscheidung 26.11.2026, MP)

> Bisher bucht MP jede Zahlung von Hand in Odoo. Neu: Auszug in einen Ordner
> kopieren, die Kundenzentrale bucht die Eingänge und meldet, was aussteht.

Ersetzt für die Übergangszeit den oben skizzierten „halbautomatischen“ Weg:
eindeutige Eingänge werden **ohne Klick** gebucht, alles andere per Klick.

- **Format: CSV-MT940** (Export des Online-Bankings; Spalten werden an der
  Überschrift erkannt, CSV-CAMT geht deshalb auch). Echtes MT940 und CAMT-XML
  werden mit Klartext abgelehnt (`KontoauszugLeser`).
- **Eingangsordner** (`Zahlungen.EingangVerzeichnis`): alle zwei Minuten wird
  nachgesehen; eingelesene Dateien wandern nach `verarbeitet`, unlesbare nach
  `fehler`. Der Ordner gehört per NTFS-Rechten nur der Buchhaltung und dem
  Dienstkonto.
- **Datensparsam:** Gespeichert werden nur gebuchte **Eingänge** (`Bankumsatz`).
  Abgänge (Gehälter, Miete) und vorgemerkte Umsätze werden gezählt und
  verworfen; die Kontoverbindung des Zahlers wird nicht gespeichert.
- **Nie doppelt:** Jede Buchung hat einen Fingerabdruck; dieselbe Datei oder
  sich überlappende Auszüge erzeugen nichts Zweites.
- **Automatisch gebucht wird nur der eindeutige Fall** (`ZahlungsZuordnung`):
  genau eine Rechnungsnummer im Verwendungszweck (auch mit Leerzeichen oder
  Umbruch), offener Betrag exakt, Euro. Teilzahlung, Skonto, Überzahlung,
  Sammelüberweisung, fehlende Nummer → **Klärungsliste** mit Vorschlag.
- **Zahlung in Odoo** über den Assistenten „Zahlung registrieren“ an der
  Rechnung (`ZahlungsBucher`) — nur über das Schreibziel
  (`OdooSchreibRegeln`: Aus/Test/Produktiv). Die Rechnung wird in der
  Zieldatenbank am Namen gesucht, der offene Betrag vor dem Schreiben geprüft,
  danach gegengeprüft. Das Bankjournal wird am Namen bestimmt
  (`Zahlungen.OdooJournal`; leer = das einzige Bankjournal) — keine festen
  Odoo-Nummern.
- **Unklare Buchung:** Vor dem Aufruf wird der Eingang als „Buchung unklar“
  gespeichert. Bricht der Aufruf ab, bucht kein Lauf ein zweites Mal; ein
  Mensch sieht in Odoo nach und entscheidet („steht in Odoo“ / „nicht gebucht“).
- **Wichtig:** Den Auszug **nicht zusätzlich in Odoo importieren** — sonst
  stehen die Eingänge dort doppelt.
- **Seite Zahlungen** (`/zahlungen`, Rolle Buchhaltung oder Admin):
  Klärungsliste, Zugeordnetes, Gebuchtes, offene Rechnungen live aus Odoo
  (überfällige zuerst).
- **Meldungen per Mail:** nach jedem eingelesenen Auszug (gebucht, zur
  Klärung) und wöchentlich die überfälligen Rechnungen (`Zahlungen.Empfaenger`).
- **Offen:** Test gegen die Odoo-Testdatenbank (der Assistent ist lokal nur
  gegen ein nachgestelltes Odoo gelaufen); eigener, eng berechtigter
  Schreib-Benutzer vor „Produktiv“. Mit der eigenen Fakturierung (Phase 4)
  bucht derselbe Ablauf gegen die eigenen Rechnungen.

## E-Mail (Basisdienst)

### Ausgang

- Alle Mails laufen über eine **Warteschlange**; Hintergrunddienst drosselt
  automatisch auf die **IONOS-Limits (pro Minute/Stunde/Tag, konfigurierbar)**.
- Wiederholversuche mit Wartezeit-Staffelung; nach letztem Fehlversuch →
  Aufgabe. Übersicht der versendeten Mails; jede Mail auch in der Timeline.
- Die Warteschlange gilt für **automatische** Mails (Erinnerungen, später
  Rechnungen/Mahnungen). Persönliche Mails an Kunden laufen über den
  folgenden Weg.

### E-Mail aus der Akte: Entwurf in Outlook (Entscheidung 10.11.2026, MP)

Mitarbeiter verschicken Kundenmails weiter aus ihrem eigenen Postfach — die
Kundenzentrale **bereitet die Mail fertig vor**, der Mitarbeiter drückt in
Outlook nur noch auf Senden. Kein eigener Sendekanal für persönliche
Korrespondenz.

- **Ablauf:** In der Akte (Kunde, Vertrag, Vorgang) „E-Mail vorbereiten" →
  Empfänger aus den Ansprechpartnern, Betreff, Text (aus Textbaustein) und
  **Anhänge** (z. B. das erzeugte Vertrags-PDF, Dateien aus der Ablage) →
  die Kundenzentrale legt über **Microsoft Graph einen Entwurf im Postfach
  des Mitarbeiters** an. Der Entwurf öffnet sich in Outlook im Web und liegt
  zugleich im Entwürfe-Ordner des Desktop-Outlook. Der Mitarbeiter kann noch
  ändern und sendet selbst — Absender, Signatur, gesendete Elemente und
  Antworten bleiben in seinem Postfach.
- **Ablage des tatsächlich Gesendeten:** Die Kundenzentrale merkt sich den
  Entwurf (Graph-Id) und übernimmt nach dem Versand die **wirklich gesendete
  Fassung** (Text inkl. letzter Änderungen, Empfänger, Anhänge, Message-ID)
  in die Timeline der Akte, Quelle „Outlook", im passenden Strang. Bis dahin
  steht dort nur „E-Mail vorbereitet"; ein nie gesendeter Entwurf erscheint
  nicht als gesendet. Die gespeicherte Message-ID lässt Antworten des Kunden
  später über den Outlook-Knopf im selben Gesprächsfaden landen.
- **Voraussetzung:** App-Registrierung in Entra ID mit delegiertem Zugriff
  auf das **eigene** Postfach (Mail.ReadWrite) — dieselbe Registrierung wie
  für den Outlook-Knopf, daher zusammen umsetzen. Die Kundenzentrale bleibt
  bei Windows-Auth; das Graph-Token holt das Frontend per Single Sign-on.
- **Datenschutz-Leitplanke:** Die Kundenzentrale fasst im Postfach nur die
  von ihr selbst angelegten Entwürfe und deren gesendete Fassung an — kein
  Lesen oder Abgleichen anderer Mails. Graph-Tokens werden nicht gespeichert.
- **Textbausteine:** freigegebene Formulierungen aus den
  Kommunikationsleitlinien als Grundlage; Pflichthinweise werden eingefügt.
- **Testsystem:** legt Entwürfe nur mit der Umleitungsadresse als Empfänger an.
- **Nicht gewählt:** Entwurfsdatei (`.eml`) zum Öffnen — die Kundenzentrale
  erführe nicht, ob und was gesendet wurde, und das neue Outlook öffnet solche
  Dateien nicht verlässlich als Entwurf. Eigener Versand durch die
  Kundenzentrale — Mail läge nicht im Postfach des Mitarbeiters, eigener
  Editor nötig.
- **Offen:** Sammelpostfächer (z. B. Vertrieb/Support als Absender),
  Zeitpunkt der Übernahme (sofort nach dem Senden per Abfrage oder
  Benachrichtigung von Graph).
- Zeitpunkt: zusammen mit dem Outlook-Knopf, nach dem Produktivstart.

#### Ausbau 26.11.2026: „E-Mail“ in der Kundenakte (MP)

Der Durchstich lief auf dem Server (Add-in mit Windows-Anmeldung, Entwurf und
Erkennen des Sendens). Daraus entschieden und gebaut:

- **Zwei Wege aus demselben Fenster** (MP: den Entwurf erst öffnen zu müssen,
  ist ein Schritt zu viel): **„Senden“** schickt die Mail sofort aus dem
  Postfach des Mitarbeiters (Graph, delegiert `Mail.Send` — nur angefordert,
  wenn jemand auf „Senden“ klickt; mit Rückfrage „Jetzt an … senden?“).
  **„In Outlook öffnen“** legt den Entwurf an und öffnet ihn in Outlook im Web
  zum Ändern. Das ersetzt „nicht gewählt: eigener Versand“ von oben nur zum
  Teil: Gesendet wird weiterhin aus dem Postfach des Mitarbeiters, die Mail
  liegt in seinen gesendeten Elementen.
- **Fenster** (`EmailFenster`, Knopf „✉ E-Mail“ am Verlauf): Ansprechpartner,
  weitere Empfänger, Cc, Textbaustein, Betreff, eigener Text über dem Baustein,
  Signatur, Anhänge des Bausteins zum Abwählen. Die **Vorschau kommt vom
  Server** (`api/mail/zusammenstellen`, `MailRegeln`) — was dort steht, geht
  ins Postfach.
- **Platzhalter:** gefüllt werden Kunde (Firma, Ansprechpartner, Adresse,
  Kundennummer) und Mitarbeiter (Name, Adresse, Signatur). Alles andere
  (Beleg, Angebot, Rechnung …) bleibt offen, wird benannt und **sperrt das
  direkte Senden** — solche Mails gehen über „In Outlook öffnen“.
- **Signatur je Mitarbeiter** (`Mitarbeiter.MailSignaturHtml`, Seite
  `/signatur`): über Graph angelegte Mails bekommen von Outlook keine (im Test
  bestätigt). Jeder hinterlegt seine einmal — aus Odoo geholt
  (`res.users.signature`, über die Anmeldung = E-Mail-Adresse) oder aus Outlook
  kopiert und eingefügt. Outlooks eigene Signaturen sind über Graph nicht
  lesbar. Bilder nur per Internetadresse.
- **Verlauf:** Erst wenn die Mail im Postfach liegt, entsteht `Ausgangsmail`
  mit Eintrag im Verlauf — „E-Mail vorbereitet: …“ beim Entwurf, die Mail
  selbst beim Senden. „Stand prüfen“ (im Fenster) bzw. „n Entwürfe prüfen“ (am
  Verlauf) holt die wirklich gesendete Fassung; ein gelöschter Entwurf
  verschwindet aus dem Verlauf. Die Prüfung läuft im Browser des Mitarbeiters
  (das Token liegt nur dort), also nicht von selbst im Hintergrund.
- **Testsystem:** jede Mail geht an die Umleitungsadresse (`MailUmleitung`),
  Cc entfällt; ohne Umleitung wird nichts angelegt. Der Server lehnt es ab,
  dort eine Mail an eine andere Adresse festzuhalten.
- **Probebetrieb auf dem Produktivsystem** (MP 26.11.2026 — getestet wird
  dort, und die Tester sollen Add-in und App-Registrierung später nicht
  umstellen müssen): Schalter **„Kundenmails freigegeben“**
  (`Outlook.KundenmailsFreigegeben`, Administration → Outlook-Anbindung, nur
  Admin, Freigeben mit Rückfrage). **Standard ist gesperrt**; nur ein
  ausdrückliches „Ja“ gibt frei. Gesperrt geht jede Mail aus dem Fenster — bei
  „Senden“ wie bei „In Outlook öffnen“ — an den Mitarbeiter selbst, Cc
  entfällt, und das Fenster sagt das oben deutlich. Der Server hält gesperrt
  keine Mail an eine andere Adresse fest. Grenze: Wer den geöffneten Entwurf
  in Outlook selbst auf den Kunden umadressiert, sendet an den Kunden — das
  ist dann eine Mail aus Outlook, nicht aus der Kundenzentrale.
- **Versandweg vorgesehen (MP 26.11.2026):** Das **Ticketsystem** soll später
  über einen **eigenen SMTP-Kanal unter @idprove.de** senden. Deshalb ist der
  Weg ein Merkmal der Mail (`MailKanal`: Postfach, Smtp), und das
  Zusammenstellen (Baustein, Platzhalter, Empfänger, Anhänge, Eintrag im
  Verlauf) hängt nicht am Postfach. Der SMTP-Kanal selbst ist nicht gebaut.
- **Noch offen:** „E-Mail“ aus Vertrag und Vorgang (mit deren Platzhaltern und
  dem Vertrags-PDF als Anhang), weitere Anhänge aus der Ablage, freies
  Formatieren des eigenen Texts; Add-in „Übernehmen“ für eingehende Mails.
  **Nicht gegen Microsoft geprüft** (lokal nicht möglich): Senden, Anhänge
  (auch über 3 MB) und ob der Verweis den Entwurf in Outlook im Web im
  Schreibfenster öffnet.

#### Verträge versenden und Unterschrift als Freigabe (MP 27.11.2026)

> Ein Kunde bestellt z. B. ID.prove Professional und die World-Check-Daten:
> Dann gehen **zwei Verträge** raus, und der **unterschriebene Vertrag über
> die World-Check-Daten muss zwingend vorliegen** — sonst dürfen die Daten in
> der SaaS-Umgebung nicht freigeschaltet werden.

- **Senden gleich nach dem Erzeugen:** Im Fenster „Vertrag aus Vorlage“ steht
  nach dem Erstellen **„✉ Per E-Mail senden“**. Für Bestellungen mit mehreren
  Verträgen daneben **„+ Weiteren Vertrag erstellen“** — das Fenster sammelt
  die erzeugten Verträge und übergibt sie zusammen („2 Verträge per E-Mail
  senden“).
- **Verträge anhängen im Mail-Fenster:** Abschnitt „Verträge anhängen“ mit den
  Vertrags-PDFs des Kunden (je Bestandteil die neueste Fassung, mit Stand:
  Entwurf / unterschrieben am …). Die übergebenen Verträge sind angehakt,
  weitere lassen sich dazunehmen. Vertrauliche Bestandteile nur mit der Rolle
  dafür; ausgeliefert wird die Datei weiter nur über den Vertrags-Endpunkt.
- **„Unterschrieben liegt vor“** (`VertragsUnterschrift`): am selben
  Bestandteil ist „Unterzeichnet am“ eingetragen **und** eine Datei abgelegt,
  die nicht der aus der Vorlage erzeugte Entwurf ist. Ein Datum allein reicht
  nicht, eine Datei allein auch nicht. Bestellung, Installationsprotokoll und
  Kündigung zählen nicht als Vertragstext.
- **Sperre im Vorgang:** Ein Schritt mit „Abhaken: Vertrag unterschrieben“
  bekommt den **Vertrag zugeordnet** (Auswahl am Schritt) und zeigt, was noch
  fehlt. Er lässt sich **nicht von Hand abhaken** — die Kundenzentrale
  erledigt ihn, sobald die unterschriebene Fassung vorliegt (sofort nach der
  Änderung am Vertrag, beim Öffnen des Vorgangs, sonst stündlich;
  `VertragsUnterschriftDienst`). Alles, was auf ihn wartet — etwa
  „World-Check-Daten freischalten“ — bleibt bis dahin gesperrt. Entfernen
  kann einen solchen Schritt nur ein Admin (sonst wäre die Sperre mit einem
  Klick aufgehoben); das steht im Verlauf.
- **Einrichtung** in den Vorgangstypen: je Vertrag, der unterschrieben sein
  muss, ein Schritt „Unterschriebenen … erfassen“ mit „Abhaken: Vertrag
  unterschrieben“; der Schritt der Freischaltung „wartet auf“ ihn. Für
  Bestellungen ohne World-Check über eine Bedingung („World-Check bestellt“).
- **Noch offen:** Der Schritt „Vertrag an Kunden senden“ hakt sich beim
  Versand noch nicht selbst ab; die Zuordnung Vertrag ↔ Schritt geschieht von
  Hand; „E-Mail“ direkt aus Vertragsakte und Vorgang.

### Outlook-Durchstich (26.11.2026 — vorgezogen, MP: schneller produktiv)

Vor dem Ausbau beider Outlook-Wege klärt ein Durchstich die technischen Fragen
(Seite Administration → Outlook-Anbindung, `/admin/outlook`; Schritte in
DEPLOYMENT.md). Er speichert nichts in der Akte.

- **Entwurf im eigenen Postfach:** Der Browser des Mitarbeiters meldet sich
  selbst bei Microsoft an (öffentlicher Client, Autorisierungscode mit PKCE,
  Anmeldefenster; `outlook-entwurf.js`) und legt über Graph den Entwurf an. Die
  Kundenzentrale bleibt bei Windows-Auth, der Server sieht kein Token, das Token
  liegt nur im Arbeitsspeicher der Seite. Der Entwurf bekommt eine
  unveränderliche Kennung — darüber findet „Stand prüfen“ die gesendete Fassung
  samt Message-ID wieder. Berechtigung: nur delegiert `Mail.ReadWrite`.
- **Add-in „In Kundenakte“:** Manifest je Instanz (`OutlookManifest`,
  Berechtigung `ReadItem`), Aufgabenbereich unter `outlook-addin/`. Er liest die
  geöffnete Mail über Office.js und fragt `api/outlook/zuordnung`: exakte
  Adresse eines Ansprechpartners, sonst Domäne (nie Freemail, nie eigene
  Adressen), mehrere Kunden = Mensch entscheidet (`MailZuordnung`).
- **Zu klären durch den Test auf dem Server:** Geht die Windows-Anmeldung im
  Aufgabenbereich mit — in klassischem, neuem Outlook und Outlook im Web? Wird
  das interne Zertifikat dort akzeptiert? Kommt das Anmeldefenster nur einmal?
- **Bekannte Lücke:** Über Graph angelegte Entwürfe enthalten **keine
  Signatur** — Outlook fügt sie nur in selbst begonnene Mails ein. Lösung beim
  Ausbau: Signatur je Mitarbeiter in der Kundenzentrale pflegen und in den
  Entwurf setzen.
- **Danach (Ausbau):** „E-Mail vorbereiten“ in Akte, Vertrag und Vorgang mit
  gefüllten Platzhaltern und Anhängen, gesendete Fassung in den Verlauf;
  „Übernehmen“ im Add-in mit Speichern samt Anhängen, Liste „Zuzuordnen“,
  Gesprächsfaden über Message-ID.

### Textbausteine aus den Odoo-Mailvorlagen (Entscheidung 26.11.2026, MP)

Alle Mailvorlagen aus Odoo (`mail.template`, deutsche Fassung) werden als
**Textbausteine** übernommen — Betreff und formatierter Text (HTML), Seite
`/textbausteine` (Verweis unter „Vorlagen“). Sie sind der Vorrat für
„E-Mail vorbereiten“; bis dahin lässt sich der Text formatiert in die
Zwischenablage kopieren und in Outlook einfügen.

- **Übernahme auf Knopfdruck** (Admin, mit Probelauf), wiederholbar über die
  Odoo-Nummer der Vorlage. Hier bearbeitete Bausteine überschreibt sie nicht.
- **Entfernen** (MP 26.11.2026 — nicht jede Odoo-Vorlage wird gebraucht): Ein
  Admin entfernt einzelne Bausteine. Ein aus Odoo übernommener bleibt dabei als
  Merker stehen (`EntferntAm`), unsichtbar für alle und ohne seine Dateien —
  sonst legte die nächste Übernahme ihn wieder an. Unter „Entfernt“ lässt er
  sich wiederherstellen. Hier angelegte Bausteine werden wirklich gelöscht.
- **Anhänge** (MP 26.11.2026, Beispiel „Infomappe 3.0 Erstkontakt“): Die
  Dateien, die Odoo fest an eine Vorlage hängt (`attachment_ids`), kommen mit
  und stehen am Baustein zum Herunterladen. Abgleich bei jeder Übernahme,
  unabhängig vom Text (`AnhangAbgleich`): neu oder geändert → laden, in Odoo
  abgehängt → entfernen. Ablage in der Datenbank (`TextbausteinAnhaenge`),
  nicht in der Dokumentablage — wenige, kleine Dateien ohne Kundenbezug, und
  das Testsystem braucht kein eigenes Verzeichnis. Nicht übernommen werden
  Dateien über 15 MB, bloße Links und ausführbare oder aktive Dateiarten
  (Programme, Skripte, HTML, Office mit Makros) — mit Grund in der Meldung.
  Auslieferung immer als Download mit neutralem Inhaltstyp. Fehlt dem Namen
  die Endung, kommt sie aus dem Inhaltstyp. „Text kopieren“ nimmt Dateien
  nicht mit; beim späteren „E-Mail vorbereiten“ (Graph-Entwurf) sollen sie
  automatisch am Entwurf hängen.
- **Platzhalter:** Odoos `<t t-out="object.partner_id.name"/>`,
  `{{ object.name }}` und `${…}` werden zu `{{Kunde.Name}}`,
  `{{Beleg.Nummer}}` usw. (`OdooMailvorlage`). Unbekannte bleiben als
  `{{Odoo: …}}` stehen und erscheinen unter „Bitte nacharbeiten“.
- **Bedingungen und Schleifen** (`t-if`, `t-foreach`) kann ein Textbaustein
  nicht: Der Inhalt bleibt stehen, die Bedingung wird als Hinweis genannt.
- **Nicht übernommen:** Odoos Mailrahmen (Kopf, Fuß, Logo) und Berichte, die
  Odoo beim Senden frisch erzeugt (z. B. das Angebot als PDF) — die Signatur
  kommt aus dem Postfach des Mitarbeiters.
- **Sicherheit:** Fremdes HTML wird beim Speichern gesäubert (keine Skripte,
  Formulare, eingebetteten Inhalte, Ereignis-Attribute) und nur in einem
  abgeschotteten Rahmen angezeigt (`iframe sandbox`).

### Erfassung aus Outlook (BCC-Postfach)

> **Entscheidung 23.10.2026 (MP):** Die Übernahme startet **direkt mit einem
> eigenen Outlook-Knopf** (Outlook-Add-in „In Kundenakte übernehmen") statt mit
> Schnellschritt/Weiterleitung. **Voraussetzung: Umstellung der Kundenzentrale
> auf HTTPS** (Add-ins laufen nur über HTTPS; ohnehin geplant). Anlass: Kunden
> schreiben oft direkt an den Ansprechpartner statt auf die Odoo-Nachricht zu
> antworten — diese Mails sollen mit einem Klick in die Akte.
> Zuordnung wie unten, ergänzt um: (0) **Gesprächsfaden** über
> In-Reply-To/References gegen gespeicherte Message-IDs (auch der aus Odoo
> übernommenen Mails → landet im richtigen Verkaufsstrang), Nummern im
> Betreff/Text (Kundennr., V…, S…, RE/…), Freemail-Domains zählen nicht für
> die Domain-Zuordnung. Darstellung in der Akte wie die Odoo-Mails (Quelle
> „Outlook"), Anhänge in die Dokumentablage, Zugangsdaten maskiert.
> Das BCC-/Weiterleitungs-Postfach bleibt als möglicher Zusatzweg beschrieben.
> **Postfächer: Exchange Online / Microsoft 365, alle Mitarbeiter über M365**
> (geklärt 23.10.2026). Folgen für die Umsetzung:
> - Verteilung des Add-ins zentral über das M365 Admin Center („Integrierte
>   Apps") — kein Installieren je Arbeitsplatz; läuft in klassischem und
>   neuem Outlook sowie Outlook im Web.
> - Das Add-in liest die markierte Mail komplett über Office.js (inkl.
>   Kopfzeilen und Anhängen) und schickt sie an die API — kein Postfachzugriff
>   der Kundenzentrale nötig.
> - Anmeldung des Add-ins an der API per Entra-ID-Token (Single Sign-on);
>   dafür App-Registrierung in Entra ID. Die Kundenzentrale selbst bleibt bei
>   Windows-Auth, nur der Add-in-Endpunkt akzeptiert zusätzlich Entra-Tokens.
> - Zu prüfen beim E-Mail-Ausgang: Versand über Exchange Online statt der unten
>   genannten IONOS-Limits.
> Zeitpunkt: später, nach dem Odoo-Chatter-Import.

Mitarbeiter schreiben weiter in Outlook — wichtige Mails sollen trotzdem für
Kollegen in der Kundenakte lesbar sein:

- **Dediziertes Postfach** (z. B. `akte@rausoft.de`), das der Hintergrunddienst
  überwacht. Ausgehende Mail: Mitarbeiter nimmt das Postfach als **BCC** auf.
  Eingehende Mail: einfach dorthin **weiterleiten**.
- **Automatische Zuordnung** über die beteiligten Adressen, in dieser
  Reihenfolge: (1) exakte E-Mail-Adresse eines Ansprechpartners,
  (2) Domain des Kunden (z. B. `@berghoff-logistik.de`), (3) sonst
  **unzugeordnet**. rausoft-eigene Adressen werden ignoriert; bei ausgehenden
  Mails zählen die Empfänger, bei weitergeleiteten der ursprüngliche Absender.
- **Unzugeordnete Mails** erzeugen eine **Aufgabe für den absendenden
  Mitarbeiter** (am Absender erkannt; unbekannter Absender → Pool). Der
  Zuordnungsdialog zeigt eine Kundenauswahl mit Suche; optional wird die
  fremde Adresse dabei **als Ansprechpartner beim Kunden ergänzt** — beim
  nächsten Mal greift die Zuordnung dann automatisch (lernend). Auch „neuen
  Kunden/Lead anlegen“ ist aus dem Dialog möglich.
- Mehrdeutige Treffer (Adressen von zwei Kunden in einer Mail) → ebenfalls
  manuelle Zuordnung statt Ratespiel.
- **Duplikaterkennung über Message-ID** (Mail an Kunde + BCC kommt nur einmal
  in die Timeline); Anhänge werden mitgespeichert.
- Datenschutz-Leitplanke: erfasst wird **nur, was bewusst per BCC oder
  Weiterleitung** ans Postfach geht — kein Komplett-Sync von Mitarbeiter-
  Postfächern.

## Auswertungen

- **MRR/ARR**: berechnet aus Abo-Positionen mit Gültig-von/Gültig-bis
  (rückwirkender Verlauf, Upgrades, Churn — nicht nur Momentaufnahme).
- MRR nach Produkt, Sondervereinbarungs-Liste, Partner-Provisionen,
  offene Posten. Geplant: Umsatz je Kunde, Kündigungsquote,
  Fälligkeitsvorschau der Jahresrechnungen.

## Produktkatalog (Phase 3)

Interne Preisliste (VERTRAULICH, Stand 06/2026) wird Produktkatalog des
Angebotsmoduls: Tier-Staffeln nach Stammdaten-Volumen (STARTER / PROFESSIONAL /
ENTERPRISE), Add-ons, Sanktionslisten-Pakete, Benutzer-Staffel,
WorldCheck-Pakete. Angebote **berechnen** Preise aus dem Volumen
(Aufrundungsregel auf die nächsthöhere Stufe) statt Freitext. Regeln:
WorldCheck nur ab PROFESSIONAL; Full File / Bank & Versicherung → Prüffilter
(LSEG-Direktvertrag).

## Datenmodell-Leitplanken

- Kunde ≠ Ansprechpartner ≠ Adresse ≠ Rechnungsempfänger (Partner möglich).
- Kette Angebot → Vertrag → Abo → Rechnung von Anfang an verknüpft.
- Aktivitäten-Tabelle generisch (Typ, Kunde, rausoft-Mitarbeiter,
  Kunden-Ansprechpartner, Zeitpunkt, Verweis) = Timeline.
- Zuweisung zweistufig: Bereich immer, Person optional (leer = Pool).
- Vorgänge (Bestellung → Teilaufgaben) nutzen die Aufgabe als Baustein;
  Schritte aus Vorlagen, Abhängigkeiten zwischen Schritten, Status berechnet.
- Nummernkreise zentral; Audit-Log; Rechte/Rollen; DSGVO-Löschkonzept.
- Dokumente unveränderlich versionieren; Briefanrede nie speichern, immer
  berechnen.
