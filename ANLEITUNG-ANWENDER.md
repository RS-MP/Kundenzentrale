# Kundenzentrale — Kurzanleitung für Anwender

Stand 25.09.2026 · Aufruf: **http://rs-w2019-01/Kundenzentrale** — die
Anmeldung läuft automatisch über euer Windows-Konto, es gibt kein Passwort.

> **Testphase:** Wir probieren das System gerade gemeinsam aus. Klickt euch
> ruhig durch und testet alles — zum Produktivstart wird einmal sauber
> zurückgesetzt. Kundendaten kommen aus Odoo und werden dort weiter gepflegt.

## Kunden finden

- **Kunden** in der Seitenleiste → Suche nach Firma oder Kundennummer.
- Die Chips oben filtern nach Lebenszyklus: **Kunden** (laufendes Abo oder
  aktiver Vertrag) · Interessenten · Zu klären · Einmalbestellung ·
  Gekündigt/Ehemalig — dahinter die Sichten Alle · Mit Partner · Archiv.
- Die Spalte **Server** zeigt sofort die Betriebs-Ampel: Grün = alles gut,
  Gelb = Connector-Problem, Rot = Server nicht erreichbar. Ruft ein Kunde an,
  sieht man ein Serverproblem ohne einen weiteren Klick.

## Die Kundenakte

- **Links der Verlauf** (alles, was passiert ist — Einträge mit Vertragsbezug
  sind anklickbar), **rechts die Fakten** in Aufklappern: Betrieb & Lizenzen,
  Verträge, Ansprechpartner, Stammdaten & Adressen.
- Oben die Faktenzeile: Kundennummer, Betriebsart, Server (mit Ampel),
  Merkmale (rot, z. B. Insolvenz), Partner, USt-IdNr.
- **Merkmale** vergebt ihr unter Stammdaten & Adressen per Auswahl-Chips —
  sie bleiben, bis sie bewusst geändert werden. Ändert Odoo den Kunden,
  erscheint „(prüfen)" als Erinnerung.
- **Ins Archiv** (kleiner Knopf oben rechts) räumt erledigte Kunden aus allen
  Listen; neue Geschäftsaktivität holt sie automatisch zurück.

## Vertrag aus Vorlage erstellen

1. Kundenakte öffnen → Aufklapper **Verträge** → **„+ Vertrag aus Vorlage"**.
   Im Fenster in drei Schritten:
   - **Vorlage** — nur aktuell gültige und für die eigene Rolle freigegebene
     Vorlagen, mit Kürzel, Version und „gültig seit".
   - **Angebot & Ansprechpartner** — das Angebot erscheint im Vertrag als
     „Angebot S0…" (vorbelegt: neuestes offenes Angebot aus Odoo, sonst das
     laufende Abo; „ohne Angebotsnummer" lässt eine Lücke `S0____`). Der
     Ansprechpartner bestimmt Anrede und Name (vorbelegt: Hauptansprechpartner).
   - **Prüfen** — zeigt genau die Werte, die eingesetzt werden, und warnt
     vorher, wenn z. B. die Anschrift fehlt oder ein Platzhalter unbekannt ist.

   Dann **„Vertrag erstellen"**.
2. Das System füllt die Kundendaten in die Vorlage und legt den fertigen
   Vertrag als **PDF** ab (Dateiname = Kundennummer, z. B. `4228.pdf`; die
   bearbeitbare Word-Quelle liegt gleichnamig daneben). Gleichzeitig entsteht
   eine **Vertragsakte im Status Entwurf** — mit Prüfsumme, Vorlagen-Version
   und Verlaufseintrag. Das PDF trägt Metadaten (rausoft, Vertragsnummer,
   Vorlagen-Stand) und kann digital signiert sein — nachträgliche Änderungen
   an der Datei fallen dann sofort auf.
3. PDF prüfen (Knopf **Ansehen**) und wie gewohnt per E-Mail verschicken;
   für Textanpassungen die gleichnamige `.docx` bearbeiten (Knopf **Ordner**).
   *(Versand direkt aus dem System folgt in einer späteren Ausbaustufe.)*
4. Kommt der unterschriebene Vertrag zurück: unterschriebene Version in die
   Ablage legen und in der Vertragsakte über **„+ Version hinzufügen"**
   registrieren, dann den Vertrag auf **Aktiv** stellen und die Laufzeit
   pflegen.

**Für neue Vorlagen:** Word-Datei mit Platzhaltern in das Vorlagen-Verzeichnis
legen (`…\Kunden Archiv\0_Vorlagen`) — das ist zunächst nur ein **Entwurf**.
Benutzbar wird sie erst nach der **Freigabe** im Bereich **Vorlagen**
(Rolle Compliance): Dort bekommt sie eine Versionsnummer (v1, v2 …), die
Datei wird eingefroren, und je Vorlage ist auswertbar, **welcher Kunde
welchen Versionsstand bekommen hat**.

Die Tabelle **„Dateien im Vorlagen-Verzeichnis"** zeigt jede Word-Datei mit
Status: **neu** (keiner Vorlage zugeordnet → „Freigeben …"), **geändert seit
Freigabe** (→ „Neue Version …"), **aktiv** (= gültige Version), **ersetzt**
(= ältere Version), **gilt künftig** oder **Vorlage zurückgezogen**. Die
Spalte **Platzhalter** warnt schon vor der Freigabe vor unbekannten
`{{…}}`. Darunter prüft die Seite, ob die eingefrorenen Kopien im Unterordner
„Freigegeben" noch vorhanden und unverändert sind. Eine unveränderte Datei
lässt sich nicht ein zweites Mal freigeben. Platzhalter: `{{Firmenname}}`,
`{{Kundennummer}}`, `{{Strasse}}`, `{{Plz}}`, `{{Ort}}`, `{{PlzOrt}}`,
`{{Briefanrede}}`, `{{Ansprechpartner}}`, `{{Email}}`, `{{UStIdNr}}`,
`{{AboNummer}}` (= gewähltes Angebot), `{{Datum}}`. Nicht ersetzbare Platzhalter meldet das System
nach dem Erzeugen als „offen". Details: Anleitung „Vertragsvorlagen erstellen".

Beim Freigeben legt Compliance außerdem fest: **Dokumentart** (NDA,
Lizenzvertrag … — so wird der erzeugte Bestandteil eingeordnet), **Gültig ab**
(leer = sofort; bis zum Stichtag bleibt die Vorversion wählbar) und je
Vorlage, **welche Rollen sie verwenden dürfen** (keine Auswahl = alle;
Compliance und Admin immer).

**Alte Vorlage oder alter Vertrag?** Neue Verträge entstehen immer nur aus der
**aktuell gültigen** Vorlagen-Version — ältere Versionen sind nicht mehr
wählbar. Unterschriebene Verträge im Archiv dürfen dagegen einen älteren Stand
haben und **bleiben gültig**; die Liste „Wer hat welchen Stand bekommen?" ist
reine Information, es wird nichts automatisch zum Nachversand markiert.
Nachträge bei rechtlich notwendigen Änderungen entscheidet Compliance im
Einzelfall.

## Dokumentart und Reihenfolge erkennen

- Jede Dokumentart hat ein **Kürzel** (WV Wartungsvertrag · LV Lizenzvertrag ·
  AVV · NDA · SV Sondervereinbarung · NT Nachtrag · BE Bestellung ·
  IP Installationsprotokoll · KÜ Kündigung · SO Sonstiges). In Vertragsliste
  und Kundenakte stehen die Kürzel vor dem Vertrag, z. B. `WV` `NT ×2`
  `BE ×5` — Maus darüber zeigt den vollen Namen.
- In der **Vertragsakte** sind die Bestandteile nach Dokumentart gruppiert,
  innerhalb jeder Gruppe steht das **neueste oben**; mehrere gleiche werden
  nummeriert („Bestellung 3"). Der Akten-Kopf und die Listen zeigen den
  **Stand** = das jüngste Dokument, z. B. „Stand: Bestellung 3 vom 08.09.2026".
- Das Datum stammt aus „Unterzeichnet am", sonst aus dem **Dateinamen**
  (z. B. `… 2026-09-08.pdf`, `… 08.09.2026.pdf`), sonst aus dem
  Datei-Änderungsdatum — dann steht „ca." davor. Bei gleichem Datum
  entscheidet die Endung `-1`, `-2` … Falsch erkannt? Am Bestandteil auf
  **Korrigieren** klicken und Datum oder Dokumentart anpassen.

## Dokumente ansehen und im Explorer öffnen

An jedem Vertragsdokument gibt es drei Knöpfe:

- **Ansehen** — Vorschau direkt im Browser.
- **Ordner** — öffnet den Ablage-Ordner im Windows-Explorer (Original-Datei,
  kein Download mit kryptischem Namen).
- **Pfad** — kopiert den vollständigen Dateipfad; in die Explorer-Adresszeile
  einfügen öffnet die Datei direkt.

## Verträge im Blick behalten

Bereich **Verträge**: Kennzahlen oben (gesamt, Laufzeit nachzupflegen, Ende
in 90 Tagen, gekündigt) — die Arbeitsfilter **Nachzupflegen** und **Ende in
90 Tagen** sind die tägliche Arbeitsliste. Beim Klären gilt: Kündigung
gefunden → Vertragsstatus auf **Gekündigt** setzen.

## Vertrauliche Verträge

Sondervereinbarungen oder Partnerverträge, die nicht alle lesen dürfen, werden
am Bestandteil als **Vertraulich** markiert (🔒). Dann gilt:

- **Alle** sehen die Dokumentart, das Datum und die **Kurzinfo** (z. B.
  „Sonderpreis 10 % auf Wartung bis 2027" oder „Provision 15 %") — so ist das
  Nötigste bekannt, ohne nachfragen zu müssen. Die Kurzinfo also bitte immer
  pflegen.
- Titel, Beschreibung und Dokumente sehen nur Mitarbeiter mit der Rolle
  **„Vertrauliche Verträge"** (vergibt ein Admin; Admins haben sie nicht
  automatisch).
- Vertrauliche Dokumente liegen im eigenen vertraulichen Ordner und werden
  relativ zu diesem registriert.

**Partnerverträge:** Im Bereich **Partner** hat jeder Partner eine Akte mit
seinen Kunden und Partnerverträgen. „+ Partnervertrag" legt einen neuen an;
Bestandteile sind dort automatisch als vertraulich vorbelegt — die Kurzinfo
(z. B. „Tipp-Provision 15 % auf Erstlizenz, 24 Monate") sehen alle. In der
Vertragsliste zeigt der Chip **Partnerverträge** nur diese.
Nutzt ein Partner die Software auch selbst, verknüpft man ihn in der
Partner-Akte unter **„Ist auch Kunde"** mit seinem Kunden-Datensatz — seine
eigenen Nutzungsverträge stehen dann in der Kundenakte, die
Partnerverträge in der Partner-Akte; beide verlinken aufeinander.
Nutzt ein Partner die Software auch selbst, wird er in der Partner-Akte unter
**„Ist auch Kunde"** mit seinem Kunden-Datensatz verknüpft (Vorschlag per Name
oder Suche). Seine eigenen Nutzungsverträge stehen dann in der Kundenakte, die
Partnerverträge in der Partner-Akte — beide verlinken aufeinander.

## Wiki

Bereich **Wiki** in der Navigation: hier steht, wie bei uns was funktioniert
und wie wir es nennen. Erstes Thema **Sanktionslisten**: alle Listen nach
Land bzw. Region, je Liste offizieller Name, unsere Bezeichnung und andere
gebräuchliche Namen (Suche findet alle), Beschreibung und Relevanz, Download-Link, wie sie
importiert wird (Sanction Admin oder von Hand), wer sie überwacht und wer
vertritt, und der Prozess bei Aktualisierungen — ohne eigenen Prozess gilt der
**Standardprozess** der Importart (Abschnitt oben). Lesen dürfen alle.

**Wer darf das Wiki pflegen?** Admin und Compliance alle Themen. Alle anderen
brauchen die Rolle **Wiki-Redaktion** plus eine Freigabe je Thema: Ein Admin
hakt unter Benutzer & Rollen bei der Person an, welche Wiki-Themen sie
bearbeiten darf (z. B. nur „Sanktionslisten"). So kann eine Person die
Sanktionslisten pflegen und eine andere später Entwicklung oder Buchhaltung.
Ohne angehaktes Thema kann ein Redakteur nichts bearbeiten. Wer darf, sieht
in der aufgeklappten Liste den Knopf „Bearbeiten".

**Preisliste (Bereich Vertrieb):** zeigt die aktuelle ID.prove-SaaS-Preisliste
mit Stufen, Volumenpreisen, Add-ons, Sanktionslisten-Paketen, Benutzer-Paketen
und den Begriffen intern ↔ extern (z. B. „Mengenstaffel" = „Benutzer-Pakete").
Die PDFs liegen im Preislisten-Ordner auf dem P-Laufwerk („Ordner öffnen").
**Interne Teile** — WorldCheck-Preise, Volumen-Pool, Vertriebshinweise, alles mit
der Marke „INTERN" — sehen nur Vertrieb, Admin und Compliance; alle anderen
sehen die Kundenfassung. **Neue Version:** Das Preislisten-Projekt legt je
Version die beiden PDFs und eine JSON-Datei auf P ab; ein Wiki-Redakteur des
Themas Preisliste wählt die JSON unter „Neue Version importieren" aus. Nur
neuere Versionsnummern werden angenommen; ältere Versionen bleiben als
Historie einsehbar.

## Wer darf was

Alle Mitarbeiter können mit Kunden und Verträgen arbeiten. Der Bereich
**Administration** (Einstellungen, Server, Odoo-Import, Benutzer & Rollen,
Verträge löschen) ist Admins vorbehalten — Rollen vergibt ein Admin unter
Administration → Benutzer & Rollen. Vertrauliche Verträge sieht vollständig nur,
wer die Rolle „Vertrauliche Verträge" hat. Das Wiki pflegen Admin, Compliance
und Wiki-Redakteure (nur ihre freigegebenen Themen).

## Bei Problemen

Fehlermeldung oder etwas sieht falsch aus? Kurz an MP mit Kundennummer und
was ihr gemacht habt — wir sammeln alles und arbeiten es ab.
