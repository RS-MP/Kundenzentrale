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

1. Kundenakte öffnen → Aufklapper **Verträge** → Vorlage im Auswahlfeld
   wählen (zeigt immer die aktuell freigegebene Version, z. B.
   „NDA Standard (v2)") → **„Aus Vorlage erstellen"**.
2. Das System füllt die Kundendaten in die Word-Vorlage ein, legt die Datei
   im Ablageverzeichnis ab (Dateiname = Kundennummer, z. B. `4228.docx`,
   bei mehreren `4228-1.docx` …) und legt eine neue **Vertragsakte im Status
   Entwurf** an — mit Prüfsumme und Verlaufseintrag.
3. Die fertige Datei bei Bedarf in Word öffnen (Knopf **Ordner** oder **Pfad**
   am Dokument, siehe unten), prüfen und wie gewohnt verschicken.
   *(PDF-Ausgabe und Versand direkt aus dem System folgen in einer späteren
   Ausbaustufe.)*
4. Kommt der unterschriebene Vertrag zurück: unterschriebene Version in die
   Ablage legen und in der Vertragsakte über **„+ Version hinzufügen"**
   registrieren, dann den Vertrag auf **Aktiv** stellen und die Laufzeit
   pflegen.

**Für neue Vorlagen:** Word-Datei mit Platzhaltern in das Vorlagen-Verzeichnis
legen (`…\Kunden Archiv\0_Vorlagen`) — das ist zunächst nur ein **Entwurf**.
Benutzbar wird sie erst nach der **Freigabe** im Bereich **Vorlagen**
(Rolle Compliance): Dort bekommt sie eine Versionsnummer (v1, v2 …), die
Datei wird eingefroren, und je Vorlage ist auswertbar, **welcher Kunde
welchen Versionsstand bekommen hat**. Platzhalter: `{{Firmenname}}`,
`{{Kundennummer}}`, `{{Strasse}}`, `{{Plz}}`, `{{Ort}}`, `{{PlzOrt}}`,
`{{Briefanrede}}`, `{{Ansprechpartner}}`, `{{Email}}`, `{{UStIdNr}}`,
`{{AboNummer}}`, `{{Datum}}`. Nicht ersetzbare Platzhalter meldet das System
nach dem Erzeugen als „offen". Details: Anleitung „Vertragsvorlagen erstellen".

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

## Wer darf was

Alle Mitarbeiter können mit Kunden und Verträgen arbeiten. Der Bereich
**Administration** (Einstellungen, Server, Odoo-Import, Benutzer & Rollen,
Verträge löschen) ist Admins vorbehalten — Rollen vergibt ein Admin unter
Administration → Benutzer & Rollen.

## Bei Problemen

Fehlermeldung oder etwas sieht falsch aus? Kurz an MP mit Kundennummer und
was ihr gemacht habt — wir sammeln alles und arbeiten es ab.
