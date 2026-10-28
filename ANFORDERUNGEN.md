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

## Rechnungen & Offene Posten

- Versand nur per E-Mail als **ZUGFeRD**; Postversand = drucken + manuell.
- **Automatische Kopie an DATEV Unternehmen Online** bei jedem Versand.
- Original unveränderlich, fortlaufender Nummernkreis, Korrektur nur per
  Storno/Gutschrift.
- **Bank-CSV-Import** halbautomatisch: Vorschlag über Rechnungsnummer + Betrag,
  Zuordnung per Klick; Sonderfälle (Sammelüberweisung) manuell → erzeugt Aufgabe.
- **Mahnung auf Knopfdruck** als druckfertiges PDF (Versand meist manuell per
  Post); Offene-Posten-Liste reicht als Übersicht.

## E-Mail (Basisdienst)

### Ausgang

- Alle Mails laufen über eine **Warteschlange**; Hintergrunddienst drosselt
  automatisch auf die **IONOS-Limits (pro Minute/Stunde/Tag, konfigurierbar)**.
- Wiederholversuche mit Wartezeit-Staffelung; nach letztem Fehlversuch →
  Aufgabe. Übersicht der versendeten Mails; jede Mail auch in der Timeline.

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
