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
| 2 | Kunden-Timeline + E-Mail-Anbindung + Aufgabenverwaltung |
| 3 | Angebote, Produkte, CRM/Leads, Tickets (gleicher Unterbau wie Aufgaben) |
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
- Status: offen, in Arbeit, Rückfrage, zurückgestellt, erledigt.
- **Verlaufseinträge** an der Aufgabe („heute telefonisch nicht erreicht“),
  Kommentare/Rückfragen als Feedback-Schleife; Statuswechsel werden protokolliert.
- **Wiedervorlage** und **„Zurückstellen bis“**: am Stichtag taucht die Aufgabe
  automatisch wieder als offen auf.
- Bei Kundenbezug erscheinen Einträge auch in der Kunden-Timeline.
- Tickets (Phase 3) nutzen denselben Unterbau.

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
- Nummernkreise zentral; Audit-Log; Rechte/Rollen; DSGVO-Löschkonzept.
- Dokumente unveränderlich versionieren; Briefanrede nie speichern, immer
  berechnen.
