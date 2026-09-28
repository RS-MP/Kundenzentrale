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
   wählen (zeigt immer die aktuell gültige Version mit Dokumentart, z. B.
   „NDA · NDA Standard (v2)") → **„Aus Vorlage erstellen"**. Angeboten werden
   nur Vorlagen, die für die eigene Rolle freigegeben sind.
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
welchen Versionsstand bekommen hat**. Platzhalter: `{{Firmenname}}`,
`{{Kundennummer}}`, `{{Strasse}}`, `{{Plz}}`, `{{Ort}}`, `{{PlzOrt}}`,
`{{Briefanrede}}`, `{{Ansprechpartner}}`, `{{Email}}`, `{{UStIdNr}}`,
`{{AboNummer}}`, `{{Datum}}`. Nicht ersetzbare Platzhalter meldet das System
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

## Wer darf was

Alle Mitarbeiter können mit Kunden und Verträgen arbeiten. Der Bereich
**Administration** (Einstellungen, Server, Odoo-Import, Benutzer & Rollen,
Verträge löschen) ist Admins vorbehalten — Rollen vergibt ein Admin unter
Administration → Benutzer & Rollen. Vertrauliche Verträge sieht vollständig nur,
wer die Rolle „Vertrauliche Verträge" hat.

## Bei Problemen

Fehlermeldung oder etwas sieht falsch aus? Kurz an MP mit Kundennummer und
was ihr gemacht habt — wir sammeln alles und arbeiten es ab.
