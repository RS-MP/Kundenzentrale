# Deployment auf RS-W2019-01

Stand 10.09.2026 · Es gibt **zwei Wege** — Weg A ist der beschlossene
Sofort-Weg (wie beim Urlaubsplaner), Weg B (Entra ID) bleibt als spätere
Option dokumentiert.

> **Ist-Stand: seit 10.09.2026 im Testbetrieb über Weg A; seit 25.09.2026
> als IIS-Unteranwendung** unter der Default Web Site (wie der Urlaubsplaner):
> **`http://rs-w2019-01/Kundenzentrale`** — Ordner `C:\Kundenzentrale`,
> App-Pool „Kundenzentrale", Windows-Auth an/Anonym aus auf der Anwendung.
> Intranet-Zone per GPO erledigt; nur intern erreichbar (kein HTTPS —
> bei Bedarf internes Zertifikat + https-Binding an der Default Web Site).
> Update-Ritual: **vorher** `dotnet list Rausoft.Erp.slnx package --vulnerable --include-transitive`
> (muss für alle Projekte „keine anfälligen Pakete" melden, sonst erst Pakete heben) →
> App-Pool beenden → `C:\Kundenzentrale\wwwroot\_framework` leeren (der Ordner
> ist **versteckt**, daher `Remove-Item -Recurse -Force …\wwwroot\_framework`;
> sonst sammeln sich alte Assembly-Stände) → Inhalt von `publish` (frisch per
> `rm -rf publish; dotnet publish src/Rausoft.Erp.Api -c Release -o publish`,
> weil auch `publish -o` nie aufräumt) drüberkopieren
> (`appsettings.Production.json` liegen lassen) → App-Pool starten → Browser
> Strg+F5. Kontrolle: `(Get-ChildItem …\wwwroot\_framework -Force -Filter
> "Rausoft.Erp.Web*.wasm").Count` muss 1 sein.

## Nachzuziehende Schritte (Testbetrieb → Server)

- [ ] **Outlook-Durchstich (26.11.2026):** Publish wie üblich, Migration `OutlookEinstellungen`
  (nur zwei Einstellungen). Zuerst im **Testsystem** (`…/Kundenzentrale-Test`). Der Durchstich
  speichert nichts; er soll zeigen, ob beide Wege in eurem M365 und eurem Outlook funktionieren.
  1. **App-Registrierung** im Entra Admin Center (entra.microsoft.com → Identität → Anwendungen →
     App-Registrierungen → Neue Registrierung):
     - Name `Kundenzentrale Outlook`, Kontotyp „Nur Konten in diesem Organisationsverzeichnis“.
     - Umleitungs-URI: Plattform **Single-Page-Anwendung (SPA)**, Adresse von der Seite
       Administration → Outlook-Anbindung kopieren
       (`https://rs-w2019-01:8443/Kundenzentrale-Test/outlook-anmeldung.html`). Für das
       Produktivsystem später eine zweite Umleitungs-URI derselben Plattform ergänzen
       (`https://rs-w2019-01:8443/Kundenzentrale/outlook-anmeldung.html`).
     - API-Berechtigungen → Microsoft Graph → **Delegierte** Berechtigung `Mail.ReadWrite`
       hinzufügen → „Administratorzustimmung erteilen“. Kein geheimer Clientschlüssel, kein
       Zertifikat, keine Anwendungsberechtigung — die App darf nur im Namen des angemeldeten
       Mitarbeiters auf dessen eigenes Postfach.
     - Von der Übersicht **Anwendungs-ID (Client)** und **Verzeichnis-ID (Mandant)** kopieren.
  2. Kundenzentrale → Administration → **Outlook-Anbindung**: beide Kennungen eintragen.
  3. **Entwurf prüfen:** auf derselben Seite „Entwurf anlegen“. Erwartet: Anmeldefenster von
     Microsoft (schließt sich bei bestehender Anmeldung von selbst), dann „Entwurf angelegt“. Den
     Entwurf in Outlook senden, danach „Stand prüfen“ → „gesendet … wiedergefunden“.
     Dabei notieren: Erscheint das Anmeldefenster jedes Mal oder nur einmal? Steht die
     **Signatur** im Entwurf? (Erwartung: nein — Outlook setzt sie nur in selbst begonnene Mails.)
  4. **Add-in prüfen:** „Manifest herunterladen“, in Outlook `https://aka.ms/olksideload` öffnen →
     Meine Add-Ins → Benutzerdefiniertes Add-In hinzufügen → Aus Datei. Eine Kundenmail öffnen →
     „In Kundenakte“. Der Aufgabenbereich meldet im Klartext, ob Outlook die Mail lesen kann und
     ob die Kundenzentrale antwortet. **In jedem benutzten Outlook wiederholen** (klassisch, neu,
     im Web) und das Ergebnis je Outlook notieren — vor allem, ob die Windows-Anmeldung mitgeht
     (Meldung „Anmeldung nicht angenommen“) und ob das Zertifikat der Kundenzentrale akzeptiert wird.
  5. Ergebnis an Claude melden; danach entscheidet sich, ob das Add-in bei der Windows-Anmeldung
     bleiben kann oder ein Entra-Token braucht.

- [ ] **Offene Angebote, Textbausteine, Kontoauszüge (26.11.2026):** Publish wie üblich, die
  Migration `VerkaufTextbausteineZahlungen` läuft beim Start (drei neue Tabellen, Einstellungen
  `Verkauf.*` und `Zahlungen.*`). Danach unter Administration → „Offene Angebote und Kontoauszüge“:
  1. **Empfänger** der beiden Übersichten eintragen (`Verkauf.BerichtEmpfaenger`,
     `Zahlungen.Empfaenger`) und mit „Übersicht … jetzt senden“ prüfen.
  2. **Eingangsordner** für Kontoauszüge anlegen und eintragen. NTFS-Rechte: nur Buchhaltung
     (Ändern) und das Konto des App-Pools (Ändern — es verschiebt die Dateien nach `verarbeitet`).
     Der Ordner enthält vollständige Auszüge, also auch Gehaltszahlungen.
  3. **Rolle Buchhaltung** an die Personen vergeben, die die Seite „Zahlungen“ sehen sollen.
  4. **Mailvorlagen:** Vorlagen → E-Mail-Textbausteine → „Probelauf Odoo“, dann „Aus Odoo
     übernehmen“. Liest der Odoo-Benutzer der Anbindung `mail.template` nicht, meldet die
     Übernahme das im Klartext.
  5. **Buchen in Odoo zuerst gegen die Testdatenbank:** Administration → Odoo-Testdatenbank,
     Schreibziel „Test“. Einen Auszug mit einer bekannten offenen Rechnung einlesen und in Odoo
     prüfen: Zahlung am richtigen Journal, Rechnung „bezahlt“ bzw. „in Zahlung“. Gibt es mehrere
     Bankjournale, den Namen unter `Zahlungen.OdooJournal` eintragen. Erst danach „Produktiv“.
     In der Testdatenbank Gebuchtes lässt sich auf der Seite „Zahlungen“ zurück in die Klärung legen.
  6. Ab dann den Kontoauszug **nicht mehr in Odoo importieren** (sonst doppelte Eingänge).

- [ ] **Margenrechner: Einkaufsstaffel oberhalb von 5 Mio. Cases berichtigen (25.11.2026):**
  Kein Publish, keine Migration — nur Datenpflege auf dem Server. Die HTML-Vorlage rechnet
  oberhalb von 5 Mio. Cases mit linearen 100.000er Zwischenstufen; laut Staffeltabelle des
  Anbieters gelten dort **zwei feste Stufen** (bis 10 Mio., bis 20 Mio.). Die 88 Stufen bis
  5 Mio. stimmen überein (Stufe für Stufe verglichen). Folge des Fehlers: zwischen 5 und
  20 Mio. Cases war der Einkauf zu niedrig angesetzt, knapp über 5 Mio. etwa um die Hälfte.
  **So berichtigen (einfachster Weg):** Margenrechner → „Daten pflegen“ → die berichtigte
  Vorlage `Margenrechner_Full-File_Sanctions-Only_2.html` einlesen (liegt neben dem Original
  im Ordner `04_World Check` der neuen Preisliste auf `V:`) → prüfen → **Speichern**. Sie
  bringt die 90 Stufen, den einen Anker bei 20 Mio. und den neuen Hinweistext mit. Die Kopie
  ist geprüft: Ihr Skript läuft, die Kundenzentrale liest sie, und beide rechnen in 49
  Beständen (davon 16 über 5 Mio. Cases) gleich.
  **Oder von Hand** in „Daten pflegen“:
  1. Feld **Einkaufsstaffel**: unten zwei Zeilen anhängen, mit den Gebühren aus der
     Staffeltabelle des Anbieters (Full File und Sanctions Only sind dort gleich):
     `10.000.000; <Gebühr bis 10 Mio.>; <Gebühr bis 10 Mio.>` und
     `20.000.000; <Gebühr bis 20 Mio.>; <Gebühr bis 20 Mio.>`.
  2. Feld **Lineare Staffel oberhalb der Tabelle** (Anker): die drei Zeilen durch **eine**
     ersetzen: `20.000.000; <Gebühr bis 20 Mio.>` — der erste Anker muss am Ende der Tabelle
     liegen. Schrittweite bleibt 100.000.
  3. Feld **Hinweis zur Regel**: den Satz zur „linearen Staffel ab 5 Mio. Cases“ ersetzen,
     z. B. „Staffeltabelle des Anbieters mit festen Stufen bis 20 Mio. Cases. Maßgeblich ist
     der Vertragstext.“ → **Speichern**.
  Kontrolle: Unter „So rechnet die Seite“ steht danach „90 Stufen von 130.000 bis 20 Mio.
  Cases“; die Kurve springt bei 5 Mio. Cases sichtbar nach unten. Oberhalb von 20 Mio. Cases
  (die Tabelle endet dort) rechnet der Rechner mit dem Preis je Case der 20-Mio.-Stufe
  weiter (Entscheidung 25.11.2026). **Achtung:** Das Original `…_1.html` auf `V:` enthält die
  lineare Regel weiterhin (bewusst unverändert gelassen) — nur noch `…_2.html` einlesen.

- [ ] **Publish aktualisieren (24.11.2026, erledigte Aufgaben in der Akte ausgeblendet):**
  Keine Migration, nur `publish`. In der Kundenakte zeigt „Offene Aufgaben“ nur noch offene
  Aufgaben — die durchgestrichenen erledigten zwischen den offenen verwirrten, erst recht
  nachdem der Import viele Odoo-Aufgaben auf einmal geschlossen hat. Am Ende der Liste
  steht „n in den letzten 7 Tagen erledigt — anzeigen“; damit lassen sich die kürzlich
  erledigten einblenden, etwa um ein versehentliches Abhaken zurückzunehmen. Ältere
  erledigte Aufgaben stehen wie bisher nur im Verlauf.
  **Mit demselben Publish — Anfragen, Zeile „Odoo“:** Bei Anfragen, die aus Odoo
  übernommen oder mit einer Verkaufschance verknüpft sind, stand dort „noch nicht
  angelegt“ (die Zeile kannte nur das Anlegen aus der Kundenzentrale). Jetzt steht
  „In Odoo vorhanden · Verkaufschance Nr. … · in Odoo öffnen ↗“, und der Knopf „In Odoo
  anlegen …“ entfällt dort (die API lehnte das Anlegen ohnehin ab). Der Link führt in die
  Produktiv-Datenbank laut Administration → Odoo-Anbindung. Von Hand erfasste Anfragen
  ohne Verkaufschance zeigen weiter „noch nicht angelegt“.

- [ ] **Publish aktualisieren (24.11.2026, erledigte Odoo-Aktivitäten als offene Aufgaben):**
  Keine Migration. Anlass: Bei SKD stand „Online Presentation“ vom 31.07. als überfällige
  Aufgabe „aus Odoo“, in Odoo war sie nicht mehr geplant. Ursache: Der Verlaufs-Import las
  geplante Aktivitäten **einschließlich archivierter** — Odoo (Version 19.3) löscht eine
  erledigte Aktivität nicht, sondern archiviert sie. Jetzt liest der Import Aktivitäten
  ohne archivierte; alles andere (Kontakte, Verkaufschancen, Nachrichten) weiterhin mit.
  **Betrifft beide Pakete:** `publish` (Chatter-Import aus der Administration) **und**
  `publish-import` (`C:\Kundenzentrale-Import`, falls der Import dort läuft) — beide neu
  kopieren.
  **Wirkung beim nächsten Lauf:** Alle offenen Aufgaben „aus Odoo“, deren Aktivität in Odoo
  erledigt (archiviert) oder gelöscht ist, werden auf **erledigt** gesetzt — das können
  beim ersten Mal viele sein. In der Kundenzentrale selbst erledigte oder dort angelegte
  Aufgaben bleiben unberührt. **Vorher ansehen:** Administration → Odoo-Anbindung →
  „Chatter → Kunden-Timeline“ → **Dry-Run** (oder `Rausoft.Erp.OdooImport.exe
  chatter-import --dry-run`): Die Zeile „Aufgaben aus Odoo (würden): … in Odoo
  erledigt/entfernt“ nennt die Zahl. Mit Kunde SKD zuerst probieren (`--kunde <KundenNr>`).
  **Nicht gegen Odoo geprüft:** Der lokale API-Key wird von Odoo abgelehnt; die Ursache ist
  aus dem Importcode und der Odoo-Version hergeleitet. Bleibt die SKD-Aufgabe nach einem
  echten Lauf offen, ist die Aktivität in Odoo noch aktiv — sie hängt am Verkaufsauftrag
  S01191, nicht am Kontakt.

- [ ] **Publish aktualisieren (24.11.2026, Margenrechner World-Check):** Migration
  `Margenrechner` (neue Tabelle `MargenrechnerStaende`) läuft beim Start von selbst.
  Wirkung: neue Seite **„Margenrechner“** (Full File und Sanctions Only, Einkauf nach
  Variante B) mit denselben Eingaben, Formeln und Ergebnissen wie die
  HTML-Kalkulationsvorlage; gerechnet wird auf dem Server.
  **Zugang nur mit der neuen Rolle „Margenrechner“** — sie steckt in keiner anderen Rolle,
  auch nicht in Admin. Ohne die Rolle gibt es weder den Menüpunkt noch Daten (die API
  antwortet mit 403).
  **Nach dem Update auf dem Server:**
  1. Administration → „Benutzer & Rollen“: bei Marcus und bei sich selbst die Rolle
     **Margenrechner** anklicken und speichern. Seite neu laden — der Menüpunkt
     „Margenrechner“ erscheint unter „Wiki“.
  2. Margenrechner öffnen. Beim ersten Mal sind keine Daten da: unter „Daten des
     Margenrechners“ die HTML-Vorlage wählen (`V:\5_Preise\ID.prove\Neue Preisliste\
     04_World Check\Margenrechner_Full-File_Sanctions-Only_2.html`, die am 25.11.2026
     berichtigte Fassung — nicht mehr `…_1.html`) — die Seite liest
     Baskets, Verkaufspreise, Staffeltabelle und lineare Regel aus und zeigt sie zur
     Kontrolle. Mit **„Speichern“** gilt der Stand.
  3. Gegenprobe: Beispielbestand, „pro Monat“ — der **Einkaufspreis** muss mit der Vorlage
     übereinstimmen. Verkaufspreis, Gewinn und Marge weichen dort ab, wo die Wiki-Preisliste
     andere (aktuell gültige) Verkaufspreise hat als die Vorlage; das ist gewollt. Mit
     gleichen Verkaufspreisen stimmt die Anzeige Feld für Feld (lokal gegen das Skript der
     Vorlage geprüft).
  **Verkaufspreise (Entscheidung 24.11.2026):** Es gilt die **Wiki-Preisliste** — hat sie
  für Basket und Bereich einen World-Check-Preis, rechnet der Margenrechner damit, und eine
  neu importierte Version gilt sofort. Der in der Datenpflege eingetragene Verkaufspreis
  (aus der Vorlage, teils noch zu niedrig) gilt nur für Baskets, die im Wiki keinen Preis
  haben. Unter der Bestandstabelle steht, welche das sind; die Datenpflege zeigt je Basket
  Wiki-Preis und eingetragenen Preis nebeneinander. Stand v22: Im Wiki fehlen für
  Sanctions Only die Baskets 15.000, 20.000, 30.000 und 40.000, für Full File 15.000,
  25.000, 30.000 und 40.000 — dort rechnet der Rechner bis zur nächsten Preisliste mit den
  eingetragenen Preisen.
  **Verkaufte Lizenzen als Bestand (24.11.2026):** Beim Öffnen lädt der Rechner die
  verkauften World-Check-Lizenzen aus den **laufenden Abos** (Odoo-Import) als Bestand;
  Knopf „Verkaufte Lizenzen“ holt sie jederzeit wieder, „Beispielbestand“ und „Leeren“
  bleiben. Bereich und Basket kommen aus dem Produktnamen der Abo-Position, z. B.
  „ID.prove SaaS World-Check Sanctions Only (5.000)“ → Sanctions Only, Basket 5.000; die
  Menge der Position zählt als Stückzahl. Unter der Tabelle steht die Liste dahinter
  (Kunde, Abo, Produkt, Zuordnung). **Bitte auf dem Server einmal durchsehen** — lokal gibt
  es keine Abos, geprüft ist nur mit erfundenen Testkunden: Stehen dort alle erwarteten
  Kunden (z. B. SKD)? Positionen mit „nicht zugeordnet“ nennen den Grund (Basket fehlt in
  der Datenpflege oder Produktname ohne Bereich/Basket). Fehlt ein Kunde ganz, heißt sein
  Produkt in Odoo anders als erwartet — den Produktnamen dann bitte an Claude geben.
  Die **Einkaufsstaffel** wird in der Datenpflege **von Hand gepflegt** (eine Zeile je
  Stufe, aus Excel einfügbar) und steht nicht im Wiki; jeder gespeicherte Stand ist eine
  neue Zeile, das Audit-Log hält fest, wer wann gespeichert hat, aber keine Zahlen.
  **Zu wissen:** Die Daten liegen verschlüsselt in der Datenbank (Data Protection, derselbe
  Schlüsselbund wie die Geheimnisse). Das **Testsystem** hat einen eigenen Schlüsselbund und
  kann einen Stand aus einer Produktiv-Kopie nicht lesen — dort die Vorlage neu einlesen;
  dasselbe gilt nach einem Serverumzug. Im Programm, in den Tests und im Repository stehen
  keine Preise. Ein Admin kann sich die Rolle selbst geben (wie bei „Vertrauliche
  Verträge“) — das steht dann im Audit-Log.

- [ ] **Publish aktualisieren (24.11.2026, Anfrage in Odoo anlegen — Stufe 2):** Migration
  `AnfrageOdooAnlage` (neue Tabelle `AnfrageOdooAnlagen`, Einstellung
  `Anfragen.OdooVorbelegung`) läuft beim Start von selbst; sie liegt hinter
  `AnfragenOdooUebersprungen` und kommt mit demselben Publish. Wirkung: Unter Kunden →
  „Anfragen“ hat jede gespeicherte Anfrage den Knopf **„In Odoo anlegen …“**. Er liest
  zuerst nur (Vorschau): vorhandene Firma und Kontakt per Dublettensuche, offene
  Verkaufschancen, dazu Phasen, Stichwörter, Benutzer, Aktivitätsarten, Quelle und Medium
  aus Odoo. Geschrieben wird erst nach „Jetzt anlegen“: Firma (oder vorhandene), Kontakt
  (Name = Nachname, Vorname, Anrede nach Bestätigung), Hauptansprechpartner (nur wenn die
  Firma keinen hat, sonst per Häkchen), Verkaufschance (neu oder Notiz an einer offenen)
  und Aktivität. Nicht übertragen: Klick-Kennungen, IP-Adresse, Browser-Kennung. Im
  Programm stehen keine Odoo-Nummern — alles kommt über den API-Key und wird am Namen
  vorbelegt; die letzte Wahl (Zuweisung, Aktivitätsart, Phase, Quelle, Medium) merkt sich
  die Kundenzentrale. Rechte: Vertrieb und Admin.
  **Rundlauf-Test gegen die Odoo-Testdatenbank:**
  1. Administration → „Odoo-Testdatenbank“: Stand prüfen (nicht abgelaufen), „Nur prüfen“.
  2. Dort „Schreiben nach Odoo geht an“ auf **Odoo-Testdatenbank** stellen. Solange
     „Aus“ gilt, ist der Knopf gesperrt und sagt warum.
  3. Kunden → „Anfragen“: Mailtext einfügen → „Auslesen“ → speichern → Anfrage in der
     Liste öffnen → „In Odoo anlegen …“ → Vorschau prüfen. Beim ersten Mal Zuweisung
     (Daniel) und ggf. Art der Aktivität wählen → „Jetzt in der Testdatenbank anlegen“.
  4. In der Odoo-Testdatenbank ansehen: Firma, Ansprechpartner (Vorname, Anrede),
     Hauptansprechpartner, Verkaufschance (Phase, Stichwörter, Quelle/Medium, Felder unter
     „weitere Informationen“ ohne gclid/gbraid/wbraid/msclkid), Aktivität.
  5. Gegenprobe: dieselbe Anfrage lässt sich nicht noch einmal anlegen; eine zweite
     Anfrage derselben Firma schlägt die vorhandene Firma und die offene Verkaufschance vor.
  Schlägt ein Schritt fehl, nennt die Seite den Schritt mit festem Text und was in Odoo
  schon steht; „Anlegen fortsetzen …“ legt nichts doppelt an. Im Server-Log stehen nur
  Schritt und Fehlerart (z. B. `odoo.exceptions.AccessError`), nie Odoos Antwort. Den
  Fehlertext bitte an Claude geben — lokal ließ sich nur gegen ein nachgestelltes Odoo
  prüfen, das echte kann an Feldtypen oder Rechten anders reagieren.
  Nummern aus der Testdatenbank bleiben in `AnfrageOdooAnlagen` (je Datenbank) und kommen
  nie in `Anfragen.OdooLeadId`; nach Ablauf der Testdatenbank oder beim Wechsel auf
  „Produktiv“ lässt sich eine Anfrage dort neu anlegen. Test-Anfragen danach unter
  „Anfragen“ löschen.
  **Vor „Produktiv“ klären:** (a) Das Schreiben in die Produktiv-Datenbank nutzt den
  API-Key der Odoo-Anbindung — der dortige Benutzer ist bisher zum Lesen gedacht. Entweder
  ihm Schreibrechte nur auf Kontakte, CRM und Aktivitäten geben oder einen eigenen
  Benutzer dafür anlegen (dann braucht die Kundenzentrale einen zweiten Produktiv-Zugang —
  an Claude geben). (b) Mit Daniel abstimmen, was vom n8n-Workflow abweicht: neue Firmen
  werden zusätzlich ausdrücklich als Firma markiert (`is_company` — sonst nur, wenn die
  Anreicherung eine USt-ID liefert; meldet Odoo das Feld als nur berechnet, wird es nicht
  mitgeschickt), die Verkaufschance wird als „Verkaufschance“ angelegt (nicht als Lead),
  ohne Firmenname wird keine Firma erfunden.
  **Anreicherung der Firma (MP 24.11.2026):** Wie im Workflow lässt die Kundenzentrale
  eine neue Firma von Odoo über die E-Mail-Domäne anreichern (`enrich_by_domain`: Adresse,
  USt-ID, Telefon, allgemeine E-Mail, Branche) — in der Vorschau als Häkchen „Firmendaten
  von Odoo ergänzen lassen“, vorbelegt an, nie bei Freemailern oder vorhandener Firma.
  Der Firmenname aus dem Formular bleibt; was Odoo eingetragen hat, steht als Prüfhinweis
  an Verkaufschance und Aktivität („nicht die Konzernmutter?“). Findet Odoo nichts oder
  schlägt der Aufruf fehl, wird die Firma trotzdem angelegt, mit Hinweis. Beim Test in der
  Testdatenbank darauf achten, ob die Anreicherung dort Daten liefert (je nach
  Odoo-Vertrag verbraucht sie Guthaben).
  **Mit demselben Publish (24.11.2026, Liste filtern):** Unter „Anfragen“ sind die Chips
  mit der Zählung je Kanal anklickbar. Ein Klick auf den Kanal zeigt nur dessen Anfragen,
  ein Klick auf eine Zahl darin (Kunden, gewonnen, verloren, offen) nur diese — z. B.
  „Google organisch“ → „gewonnen“. Der Chip „Alle“ filtert kanalübergreifend. Ein zweiter
  Klick oder „Filter aufheben“ zeigt wieder alles. Keine Migration.

- [ ] **Publish aktualisieren (24.11.2026, Dokumente in einer Akte gruppieren):**
  Migration `VertragsbestandteilAnlagen` (eine neue Spalte an `Vertragsbestandteile`)
  läuft beim Start von selbst, ändert keine vorhandenen Daten. Wirkung: In der
  Vertragsansicht heißt der Knopf am Dokument „Umbenennen · Zuordnen“; im Formular gibt
  es „Anlage zu“ — damit hängt ein Dokument unter einem anderen derselben Akte (z. B.
  Preisblatt unter dem Nachtrag) und steht in Vertragsansicht, Vertragsliste und
  Kundenakte eingerückt darunter. „— eigenständiges Dokument —“ löst die Zuordnung wieder.

- [ ] **Publish aktualisieren (24.11.2026, Verträge umbenennen und zusammenfassen):**
  Migration `VertragBezeichnungUndAnhaenge` (zwei neue Spalten an `Vertraege`) läuft beim
  Start von selbst; sie trägt bei Verträgen aus einer Vorlage einmalig den Vorlagennamen
  als Bezeichnung ein. Wirkung: In der Vertragsansicht oben „Benennen/Umbenennen“; rechts
  die Karte „Hauptvertrag & Anhänge“ zum Zuordnen und Lösen. Vertragsliste, Kundenakte und
  Partnerakte zeigen Bezeichnung, eingerückte Anhänge und die Dokumente je Akte. Anhänge
  übernehmen Status und Laufzeit des Hauptvertrags und zählen in den Kennzahlen und der
  Kundenkategorie nicht mehr extra. Kein erneuter Import nötig; Bestandsscan und
  Odoo-Import lassen Bezeichnung und Zuordnung unberührt.

- [ ] **Publish aktualisieren (24.11.2026, Website-Anfragen aus Odoo übernehmen):**
  Migration `AnfragenAusOdoo` (vier neue Spalten an `Anfragen`) läuft beim Start von
  selbst; sie liegt hinter `VertragBezeichnungUndAnhaenge`, beide kommen mit demselben
  Publish. Unter Administration → Odoo-Anbindung zuerst **„Anfragen übernehmen:
  Probelauf“** starten (liest Odoo, speichert nichts) und den Bericht an Claude geben —
  er nennt nur Zahlen, Formular-, Kanal- und Feldnamen sowie die Werte der Felder
  „Herkunftskanal“ und „Anliegen“. Danach **„Anfragen übernehmen“**: legt jede als
  Website-Anfrage erkennbare Verkaufschance (Formular oder Herkunft gefüllt) unter
  Kunden → „Anfragen“ an, mit lesbarer Herkunft, Kunde (über den Odoo-Kontakt) und Ausgang
  laut Odoo (offen/gewonnen/verloren, Verlustgrund); zugeordnete Anfragen stehen zum
  ursprünglichen Datum im Verlauf der Kundenakte. Klick-Kennungen (jetzt auch Microsoft,
  msclkid) kommen nur mit, wenn die Anfrage jünger als 90 Tage ist. Der Lauf ist
  wiederholbar: nichts wird doppelt angelegt, eine von Hand erfasste Anfrage mit derselben
  E-Mail-Adresse wird verknüpft statt verdoppelt, bei schon Übernommenem wird nur der
  Ausgang nachgezogen. Odoo wird nur gelesen. Konsole:
  `Rausoft.Erp.OdooImport.exe anfragen-import [--dry-run]`.
  **Nachgeschärft nach dem Probelauf (24.11.2026, Migration `AnfragenOdooUebersprungen`,
  nur eine Einstellung):** Eigene Tests über den Google Tag Assistant (Verweis von
  tagassistant.google.com bzw. `gtm_debug=` in der Adresse) werden ausgelassen. Eine unter
  „Anfragen“ gelöschte übernommene Anfrage kommt nicht wieder — ihre Verkaufschancen-Nummer
  steht dann in der Einstellung `Anfragen.OdooUebersprungen` (dort wieder herausnehmen, um
  sie doch zu übernehmen). Ein Verweis von der eigenen Website (idprove.com → idprove.de)
  zählt als „Direkt“, ein in Teams angeklickter Link als „Link aus Microsoft Teams“.

- [ ] **Publish aktualisieren (17.11.2026, Website-Anfragen):** Migration `Anfragen`
  (neue Tabelle, Einstellung `Anfragen.Kampagnen`) läuft beim Start von selbst. Wirkung:
  Unter Kunden → „Anfragen“ fügt ein Mitarbeiter (Vertrieb/Admin) den Text einer
  Website-Formular-Mail ein; die Kundenzentrale liest ihn aus, prüft Freemailer und
  Dubletten und speichert die Anfrage mit ihrer Herkunft (Kanal, Kampagne, Seiten) —
  nicht den Mailtext, nicht IP/Browser. Zugeordnete Anfragen stehen im Verlauf der
  Kundenakte. Die Klick-Kennungen (gclid, gbraid, wbraid) löscht ein täglicher Lauf nach
  90 Tagen. Nach Odoo wird nichts geschrieben. Optional in der Administration:
  `Anfragen.Kampagnen` mit „Nummer = Name; Nummer = Name“ füllen, dann zeigt die
  Herkunft den Kampagnennamen statt der Nummer.
- [ ] **Website-Anfragen in Odoo auswerten (18.11.2026):** keine Migration. Unter
  Administration → Odoo-Anbindung → „Website-Anfragen auswerten“ starten (nur lesen,
  Produktiv-Anbindung). Der Bericht zählt je Jahr, wie viele Verkaufschancen ein Formular
  und Herkunftsfelder tragen, und nennt fehlende bzw. unbekannte eigene Felder — er enthält
  keine Namen. Bericht an Claude geben: danach wird entschieden, ob und ab wann die alten
  Anfragen übernommen werden. Konsole: `Rausoft.Erp.OdooImport.exe anfragen-analyse`.
- [ ] **Odoo-Testdatenbank eintragen (17.11.2026):** Migration `OdooTestdatenbank` (nur
  Einstellungen) läuft beim Start von selbst. Unter Administration → „Odoo-Testdatenbank“
  Adresse, Datenbank, API-Benutzer, API-Key und „gültig bis“ der Odoo-Testdatenbank
  eintragen, „Speichern und prüfen“. Odoo stellt Testdatenbanken nur 14 Tage bereit —
  danach denselben Schritt mit der neuen wiederholen. Die Produktiv-Anbindung (Abschnitt
  „Odoo-Anbindung“) bleibt unberührt; Import und alles Lesen nutzen weiter sie.
  „Schreiben nach Odoo geht an“ steht auf **Aus** und wird erst umgestellt, wenn die
  erste schreibende Funktion (Anfrage in Odoo anlegen) gebaut ist: zuerst „Odoo-Testdatenbank“,
  später „Produktiv“. Im Testsystem der Kundenzentrale ist „Produktiv“ nicht wählbar.
- [ ] **Publish aktualisieren (11.11.2026, Chatter: Umbrüche wie im Browser):** keine
  Migration. Wirkung: Der Chatter-Import bricht Mailtexte so um, wie Odoo sie anzeigt —
  Zeilenumbrüche aus dem HTML-Quelltext zählen nicht mehr (Sätze zerfielen an jedem
  fetten Wort oder Link), leere Absätze bleiben als eine Leerzeile, Aufzählungen
  bekommen „•" bzw. Nummern, Tabellenzellen einen Abstand. Nach dem Update:
  **Chatter-Import erneut ausführen** — die Texte fast aller übernommenen Mails ändern
  sich dabei einmalig (Leerzeilen), der Lauf meldet entsprechend viele Aktualisierungen.
- [ ] **Publish aktualisieren (11.11.2026, Chatter: unsichtbare Füllzeichen):** keine
  Migration. Wirkung: Der Chatter-Import entfernt unsichtbare Zeichen aus Mailtexten
  (U+034F, weiche Trennzeichen, Null-Breite-Zeichen) — sie stammen aus dem Vorschautext
  von Mailvorlagen (z. B. der Odoo-Rechnungsmail) und ließen in zitierten Mails über
  hundert scheinbar leere Zeilen stehen. Nach dem Update: **Chatter-Import erneut
  ausführen** (Administration → Odoo); er gleicht die Texte bereits übernommener
  Nachrichten ab und korrigiert sie damit.

- [ ] **Publish aktualisieren (10.11.2026, Wiki Kommunikationsleitlinien):**
  Migration `WikiLeitlinien` läuft automatisch (neue Tabelle `Sprachregeln`;
  `WikiNeuerungen` bekommt die Spalten `Zielrollen` und `SprachregelId`;
  legt drei **Entwürfe** mit dem Präfix „Beispiel:" an — nichts ist vorab
  freigegeben). Wirkung: Wiki → **Kommunikationsleitlinien** (Bereich
  Vertrieb) mit den Bereichen „So sagen wir es", „Besser vermeiden",
  „Niemals", Pflichthinweise und Grundsätze; Filter nach Zielgruppe
  (Vertrieb/Support/KI) und Thema, Suche. Vorschlagen darf jeder (Entwurf);
  **freigeben, zurückziehen, fremde Entwürfe ändern/löschen nur Compliance
  und Admin** — nicht die Wiki-Redaktion. Freigegebene Regeln werden nicht
  überschrieben („Neu fassen" → neuer Entwurf, löst die alte Regel bei
  Freigabe ab). Freigabe von „Niemals"/Pflichthinweis verlangt eine
  **Lesebestätigung** von den Mitarbeitern mit Rolle Vertrieb bzw. Support
  (je nach Zielgruppe der Regel; Abzeichen am Wiki-Eintrag); Compliance/Admin
  sehen je Regel, wer bestätigt hat. „Als Vorgabe für Claude kopieren"
  (Compliance/Admin) liefert den Textblock für den Systemprompt; interne
  Begründungen sind ausgenommen. Neuerungen der anderen Themen verhalten sich
  wie bisher (ohne Zielrollen = alle Mitarbeiter). **Nach dem Update:** prüfen,
  dass die richtigen Personen die Rollen Vertrieb/Support/Compliance tragen
  (Administration → Benutzer & Rollen) — danach richtet sich, wer bestätigen
  muss und wer freigeben darf; die drei Beispiel-Entwürfe anpassen oder
  löschen. **Strg+F5 im Browser ist hier Pflicht:** mit dem alten `app.css`
  aus dem Browser-Cache erscheint die Seite ungestaltet (Regeln als graue
  Kästen, Überschriften als Fließtext). Ab diesem Stand liefert die API
  `app.css`, `wiki-editor.js` und die Startseite mit `Cache-Control: no-cache`
  aus — der Browser fragt dann bei jedem Laden nach, ob sich die Datei
  geändert hat; künftige Updates brauchen dafür kein Strg+F5 mehr.

- [ ] **Publish aktualisieren (30.10.2026, Aufgaben bearbeiten/löschen):**
  keine Migration, Odoo-Import unverändert. Wirkung: In der Akte und unter
  „Meine Aufgaben“ lassen sich in der Kundenzentrale angelegte Aufgaben
  bearbeiten — Titel, Art, Fälligkeit, Zuständig, Vorgang und neu eine
  Beschreibung — und löschen. Löschen darf, wer die Aufgabe angelegt hat,
  wer zuständig ist, oder ein Admin. Aus Odoo übernommene Aufgaben und
  Vorgangsschritte bekommen die Knöpfe nicht (Odoo-Aufgaben gleicht weiter
  der Import ab, Schritte pflegt der Vorgang). Strg+F5 im Browser.

- [ ] **Publish aktualisieren (30.10.2026, Wiki-Editor):** keine Migration.
  Wirkung: Alle Wiki-Textfelder (FAQ Symptom/Ursache/Lösung, Prozesse der
  Preisliste und Sanktionslisten, Standardprozesse, Beschreibung/Relevanz,
  Neuerungen) haben einen Editor wie in Word: Absatzformat (Überschriften,
  Code), fett, kursiv, Code, Aufzählung, Nummerierung, Link, Tabelle mit
  Zeilen-/Spaltenwerkzeugen, Rückgängig. Einfügen aus Word/Outlook übernimmt
  nur die Formatierung, die das Wiki kennt. Gespeichert wird weiter im
  bisherigen Textformat — vorhandene Texte bleiben unverändert. Neuerungen
  werden jetzt ebenfalls formatiert angezeigt. Neues Skript
  `wwwroot\js\wiki-editor.js` → nach dem Update **Browser-Cache leeren bzw.
  Strg+F5**, sonst fehlt der Editor.

- [ ] **Publish aktualisieren (29.10.2026 abends, Wiki FAQ):** Migration
  `WikiFaq` läuft automatisch (Tabelle FaqEintraege, legt den ersten Fall
  „2FA lässt sich nicht einrichten – alter ID.prove-Eintrag in der
  Authenticator-App" freigegeben an). Wirkung: Wiki → **FAQ Support**
  (Bereich Support): Supportfälle als Symptom → Ursache → Lösung, Volltextsuche
  (alle Begriffe müssen vorkommen), Filter nach Produkt/Kategorie/Status,
  Sortierung nach Änderung/Häufigkeit, „Kam wieder vor", „Lösung kopieren";
  in der Kundenakte beim Erfassen von Notiz/Telefonat das Häkchen „als
  FAQ-Fall übernehmen" (öffnet die FAQ-Erfassung vorbelegt). Rechte: lesen und
  anlegen alle; eigenen Eintrag bearbeiten der Ersteller; freigeben, veraltet
  setzen, löschen und fremde Einträge ändern Support, Admin, Compliance oder
  Wiki-Redaktion mit Thema „FAQ Support" (Administration → Benutzer & Rollen,
  neues Häkchen). Nach dem Update: Browser-Cache leeren / hart neu laden.

- [ ] **Publish aktualisieren (29.10.2026, SICHERHEIT.md Paket 1+2):** Migration
  `VorgangRowVersionUndVerweisIndex` läuft automatisch (RowVersion auf
  Vorgänge, Index auf Aktivitäten-Verweise). Wirkung: schreibende API-Aufrufe
  verlangen die Kopfzeile `X-Requested-With: Kundenzentrale` (setzt das
  Frontend selbst — **Browser-Cache leeren bzw. Seite hart neu laden**, sonst
  bekommt ein altes Frontend 403 bei jedem Speichern); Security-Header;
  Vertragsdokumente außer PDF kommen als Download; Datenbankkonflikte melden
  409 mit Text; Vorgangs-Abgleich läuft nur einmal gleichzeitig; Audit-Log
  ohne Takt-Rauschen; Odoo-/Portal-URL in den Einstellungen nur noch https.
  Vor dem Kopieren `wwwroot\_framework` auf dem Server leeren (alte
  Assembly-Stände sammeln sich dort an).

- [ ] **Publish aktualisieren (29.10.2026, SICHERHEIT.md N1 — Zertifikatsüberwachung
  gehärtet):** Migration `ZertifikatPruefregeln` läuft automatisch (zwei neue
  Einstellungen `Zertifikate.ErlaubtePorts` = „443, 8443" und
  `Zertifikate.EmpfaengerDomaenen` = leer → Domäne der Absenderadresse).
  Wirkung: „Prüfen" nur noch für Support/Admin; Hostname muss DNS-Name oder
  IP sein (kein localhost/Loopback/Link-Local); Port nur aus der Liste;
  Empfänger je Zertifikat nur in den erlaubten Domänen; Fehlertexte der
  Live-Prüfung sind feste Kategorien (Details nur im Log). Nach dem Update:
  1) Administration → E-Mail-Versand: `Mail.Absender` muss gesetzt sein, sonst
     gilt keine Domänen-Einschränkung (Hinweis in der Zertifikate-Seite).
  2) Zertifikate-Seite prüfen: Einträge mit anderem Port als 443/8443 zeigen
     nach dem nächsten Takt „Port … ist nicht zulässig" — dann entweder Port in
     `Zertifikate.ErlaubtePorts` ergänzen oder Eintrag korrigieren.
  3) Bestehende Empfänger außerhalb der eigenen Domäne werden beim Versand
     übersprungen (Mailfehler-Text am Zertifikat nennt es) — ggf. Domäne ergänzen.

- [ ] **Publish aktualisieren (23.10.2026 spät, Akte „Was lief zuletzt?"):**
  Migration `AkteUeberblick` läuft automatisch (Tabellen AktivitaetTeilnehmer,
  Vereinbarungen, Aufgaben). Die linke Spalte der Kundenakte ist neu
  (Komponente `AkteVerlauf`): Kurzfassung und **Wer mit wem** (rausoft-Person ×
  Ansprechpartner, Klick filtert den Verlauf), **Offene Aufgaben** (aus den
  geplanten Aktivitäten in Odoo und manuell, Person oder Bereichs-Pool),
  **Vereinbart & offen**, Verlauf einzeilig mit Zeitabschnitten. Manuell
  erfassen: Notiz, Telefonat, Termin mit mehreren Teilnehmern (+ Vereinbarungen),
  Vereinbarung, Aufgabe. Nach dem Update:
  1) Chatter-Import für 25757 erneut ausführen — setzt die Teilnehmer der
     vorhandenen Einträge (Grundlage für „Wer mit wem") und übernimmt offene
     geplante Aktivitäten als Aufgaben.
  2) Bei den Ansprechpartnern die **Position/Funktion** pflegen (Einkauf, IT …) —
     sie steht im Verlauf und in „Wer mit wem" neben dem Namen.
  3) Hinweis fürs Team: In der Kundenzentrale erledigte Odoo-Aufgaben bleiben in
     Odoo offen (Import liest nur); im Parallelbetrieb dort mit abhaken.
- [ ] **Publish aktualisieren (23.10.2026 abends, Stränge je Verkauf):**
  Migration `VerlaufStraenge` läuft automatisch (Aktivitaet.StrangSchluessel/
  StrangName). Ein Strang umfasst jetzt den ganzen Verkauf: Verkaufschance,
  Angebote/Aufträge (über `opportunity_id`) und Rechnungen daraus (über
  `invoice_origin`, auch Abo-Folgerechnungen) — z. B. „Verkauf S01194".
  Notizen an Firma/Kontakt bleiben eigene Stränge. Nach dem Update den
  Chatter-Import für 25757 erneut ausführen (setzt die Stränge an den
  bestehenden Einträgen; behebt auch „Verkaufschance Verkaufschance …").
- [ ] **Publish aktualisieren (23.10.2026 nachmittags, Verlauf Variante D):**
  Migration `VerlaufEmpfaenger` läuft automatisch (Aktivitaet.Empfaenger).
  Akte: chronologischer Verlauf mit Art und Person (inkl. **Bereich**) links,
  Empfänger „an …", aufeinanderfolgende Einträge eines Odoo-Vorgangs als
  einklappbarer Strang; beim Öffnen nur Notizen und E-Mails aktiv.
  Nach dem Update: 1) Administration → Benutzer & Rollen: **Bereich** bei den
  Mitarbeitern pflegen (Vertrieb, Technik, Buchhaltung …), sonst steht nur
  der Name. 2) Chatter-Import für 25757 erneut ausführen — er ergänzt die
  Empfänger an den bestehenden Einträgen.
- [ ] **Publish aktualisieren (23.10.2026, Chatter: alles importieren, umschaltbar):**
  Migration `ChatterAllesImportieren` läuft automatisch (Aktivitaet.Stufe/
  FilterGrund/OdooModell/OdooResId, Anhang ohne lokale Datei möglich).
  Der Chatter-Import übernimmt jetzt **alle** Nachrichten mit Stufe
  (wesentlich, Meilenstein, Systemmeldung, Feldänderung) und bewertet bereits
  übernommene bei jedem Lauf neu; Anhänge nur noch als Link auf Odoo (Häkchen
  „Anhänge zusätzlich herunterladen" = Archiv-Modus, erst vor der Abschaltung).
  Nach dem Update: Administration → Odoo-Anbindung → Kunde `25757` →
  **Import ausführen** (aktualisiert die 19 Einträge, legt den Rest an), dann
  in der Akte die Schalter Notizen / E-Mails / Meilensteine / Systemmeldungen /
  Feldänderungen, eine Vorgangsmarke (z. B. „Auftrag S01194") und „in Odoo
  öffnen" ausprobieren. Die bereits heruntergeladenen 8 Dateien bleiben lokal
  und werden weiter aus der Dokumentablage ausgeliefert.
- [ ] **Publish aktualisieren (22.10.2026, Odoo-Chatter → Kunden-Timeline):**
  Migration `ChatterImport` läuft beim Start automatisch (Aktivitaet.Quelle/
  OdooId/AutorName/Bezug, Tabelle Aktivitaetsanhaenge). Kundenakte: Schalter
  „Odoo-Verlauf", Anhänge im Verlauf. **Import-Tool (`publish-import`)
  ebenfalls neu kopieren** — neue Modi `chatter` (Analyse) und `chatter-import`,
  `zugangstest` prüft jetzt die Anmeldung bei Odoo und zeigt die Quelle je Wert.
  **Im Testbetrieb läuft der Chatter-Import manuell aus der Administration**
  (Odoo-Anbindung → „Chatter → Kunden-Timeline": Analyse / Dry-Run / Import,
  Bericht erscheint darunter); die nächtliche Automatik kommt erst zum
  Produktivstart. Nach dem Update:
  0) Administration → Odoo-Anbindung → **„Import jetzt ausführen"** einmal laufen
     lassen: Der Kontakt-Import zieht jetzt Kundennummern nach, die in Odoo erst
     nach dem ersten Import vergeben wurden (z. B. 25757 statt Odoo-ID 61917);
     erst danach findet die Kundensuche solche Kunden über ihre Nummer.
  1) Administration → Verzeichnisse → **Dokumentablage.Verzeichnis** setzen
     (dort landen die Anhänge unter `<KundenNr>\Odoo-Chatter\`; der App-Pool
     braucht Schreibrecht, da der Import im IIS-Prozess läuft).
     **Stolperfalle (22.10.2026): „Access to the path … is denied"** — der
     Import läuft als App-Pool-Konto, nicht als angemeldeter Benutzer. Weil die
     Freigabe auf demselben Server liegt, am einfachsten den **lokalen Pfad**
     eintragen (physischer Ordner der Freigabe, auf dem Server per
     `Get-SmbShare -Name "Kunden Archiv"` ablesen, z. B.
     `D:\Kunden Archiv\8_Ablage_Kundenzentrale`) und dem App-Pool-Konto NTFS-
     Änderungsrecht geben:
     ```powershell
     icacls "D:\Kunden Archiv\8_Ablage_Kundenzentrale" /grant "IIS APPPOOL\Kundenzentrale:(OI)(CI)M"
     ```
     (bei einem Domänen-Dienstkonto als App-Pool-Identität dieses Konto statt
     `IIS APPPOOL\Kundenzentrale`; bleibt es beim UNC-Pfad, braucht zusätzlich
     die Freigabe „Ändern" für dieses Konto). Anhänge werden über die API
     ausgeliefert, der Pfad muss für Arbeitsplätze nicht erreichbar sein.
     **Anhänge „Odoo Server Error … ValueError"** beim ersten Server-Import
     (22.10.2026): der Import versucht jetzt `datas` und ersatzweise `raw` und
     zeigt die volle Odoo-Meldung; ein erneuter Lauf lädt fehlende Anhänge zu
     bereits übernommenen Einträgen nach. Diagnose für einen einzelnen Anhang:
     `Rausoft.Erp.OdooImport.exe anhangtest <ir.attachment-Id>` (Id steht im Bericht).
  2) Administration → Odoo-Anbindung → Kunde `25757` eintragen → **Analyse**:
     Chronologie mit Filterentscheidung prüfen.
  3) **Dry-Run** für 25757, dann **Import ausführen**; Akte des Kunden ansehen
     (Schalter „Odoo-Verlauf", Anhänge).
  4) Feintuning der Regeln mit weiteren Kunden; erst danach Import ohne Kunde
     für alle (dauert, Anhänge werden geladen).
  5) Zum Produktivstart: Aktion 3 der nächtlichen Aufgabe (`chatter-import`
     nach `import` und `abos`).
- [ ] **Publish aktualisieren (15.10.2026, Sicherheit — SICHERHEIT.md V2/V3):**
  Pakete auf 10.0.13 (Schwachstellen-Scan sauber) und neue Grundberechtigung:
  jeder Zugriff braucht jetzt einen **aktiven** Mitarbeiter-Datensatz —
  „Deaktivieren" unter Benutzer & Rollen sperrt damit vollständig aus (vorher
  blieb Lesezugriff). Neue Domänenkonten werden weiterhin automatisch aktiv
  angelegt (Testphase). Manuelle Server-Schritte: **ASP.NET Core Hosting Bundle
  und .NET Desktop Runtime auf den aktuellen 10.0.x-Patch** heben (Download-
  Seite wie oben, danach `iisreset`). Danach prüfen: ein deaktivierter
  Testbenutzer bekommt überall 403.
- [ ] **Publish aktualisieren (21.10.2026, FEEDBACK Nr. 23 SSL-Zertifikate):**
  Seite Server → SSL-Zertifikate mit Ablauf-Erinnerung per E-Mail; Migration
  `Zertifikate` (Tabelle + Mail-/Zertifikats-Einstellungen) läuft automatisch.
  Nach dem Update: 1) Administration → „E-Mail-Versand und Zertifikats-
  Erinnerungen": SMTP-Server, Port, Benutzer, Absender eintragen, Passwort
  setzen, Testmail senden; Standardempfänger und Wiederholungstakt setzen.
  2) Unter Benutzer & Rollen den Zuständigen die Rolle **Support** geben
  (pflegen/bestätigen dürfen Support und Admin). 3) Alle Zertifikate mit
  Hostname und Ablaufdatum erfassen; „jetzt prüfen" zeigt, ob die Live-Prüfung
  vom Server aus durchkommt (ausgehend TCP 443 zu den Zielen nötig).
  **Ausbau 21.10. (Migration `ZertifikateAutomatik`):** 4) „Aus Serverliste
  anlegen" klicken — legt für jeden aktiven ID.prove-Server ein Zertifikat mit
  Live-Ablaufdatum an; danach übernimmt die Kundenzentrale Erneuerungen selbst
  (keine Bestätigung mehr nötig). 5) In der Administration die
  Eskalationsempfänger (DRINGEND ab 3 Tagen) und Abgelaufen-Empfänger setzen,
  Wochenbericht-Tag prüfen (Standard Montag), einmal „Wochenbericht jetzt
  senden" als Test.
- [ ] **HTTPS im Intranet** (SICHERHEIT.md V1, Code ist vorbereitet) — Schritte
  im Abschnitt „HTTPS im Intranet" unten; bis dahin bleibt `Hosting:HttpsErzwingen`
  auf `false` und der Testbetrieb läuft unverändert über http.

- [ ] **VOR dem nächsten Publish: .NET Desktop Runtime 10 installieren!**
  Die PDF-Erzeugung (Telerik) braucht WPF — ohne die Desktop Runtime startet
  die App nach dem Update nicht mehr (500.30):
  https://dotnet.microsoft.com/download/dotnet/10.0 → „.NET Desktop Runtime"
  (x64) installieren, dann normal Publish kopieren (App-Pool stoppen/starten).
- [ ] **Publish aktualisieren (25.09.2026 abends):** Vertragserzeugung liefert
  PDF (+ docx-Quelle daneben), Metadaten, optionale digitale Signatur.
- [ ] **PDF-Signatur produktiv schalten** (sobald gewünscht): Signatur-
  Zertifikat als PFX beschaffen — internes AD-CS-Zertifikat (Warnhinweis bei
  Externen) oder ein offizielles Dokumenten-Signaturzertifikat (extern gültig).
  PFX auf den Server legen (z. B. `C:\Kundenzentrale-Zertifikat\signatur.pfx`,
  NTFS nur Admins + App-Pool), dann Administration → Verzeichnisse:
  Pfad in „Signatur-Zertifikat" eintragen + „Signatur-PFX-Passwort" setzen.
  Leer lassen = PDFs ohne Signatur (funktioniert auch).

Laufende Liste aller Änderungen seit dem Go-Live vom 10.09.2026 — beim
nächsten Server-Update Publish kopieren und die Punkte abhaken:

- [ ] **Publish aktualisieren** (enthält: Benutzerverwaltung „Benutzer &
  Rollen" im Admin-Bereich, 10.09.2026). Migrationen laufen automatisch,
  manuelle Server-Schritte: keine.
- [ ] **Publish aktualisieren (25.09.2026):** „Ordner öffnen" + „Pfad kopieren"
  an den Vertragsdokumenten; **Vorlagen-Verwaltung mit Freigabe-Versionierung**
  (Seite „Vorlagen", Rolle Compliance). Nach dem Update auf dem Server:
  1) unter Administration → Benutzer & Rollen die Rolle **Compliance** an die
  zuständige Person vergeben, 2) unter „Vorlagen" die Referenzvorlage
  „Lizenzbedingungen SaaS + OnPrem.docx" als v1 freigeben — vorher ist in der
  Kundenakte keine Vorlage mehr wählbar (Auswahl zeigt nur Freigegebenes).
- [ ] **Publish aktualisieren (25.09.2026, FEEDBACK 17-Reste/18/19):**
  Dokumentart-Kürzel, Reihenfolge/Stand der Vertragsdokumente, Vorlagen mit
  Dokumentart, Rollen-Freigabe und Gültig-ab. Migration läuft automatisch.
  Nach dem Update: 1) Administration → Verzeichnisse → **„Dokumentdaten
  nachtragen"** einmal klicken (liest Datum aus Dateinamen/Dateidatum für den
  Bestand), 2) unter „Vorlagen" je Vorlage die **Dokumentart** prüfen
  (Bestand steht auf Lizenzvertrag) und ggf. Rollen einschränken.
  Import-Tool (`publish-import`) ebenfalls neu kopieren — `vertragsscan`
  setzt das Datum dann bei neuen Dateien gleich mit.
- [ ] **Publish aktualisieren (28.09.2026, FEEDBACK Nr. 20 Stufe 1):**
  vertrauliche Vertragsbestandteile. Migration läuft automatisch. Nach dem
  Update: 1) Administration → Verzeichnisse → **VertraulicheAblage.Verzeichnis**
  auf den Ordner der vertraulichen Verträge setzen (UNC), 2) unter Benutzer &
  Rollen die Rolle **„Vertrauliche Verträge"** an die berechtigten Personen
  vergeben, 3) **NTFS-Rechte** des Ordners prüfen: Lesen nur App-Pool-Konto
  „IIS APPPOOL\Kundenzentrale" (bzw. Computerkonto bei UNC) + berechtigte
  Personen — die App schützt nur den Zugriff über die Kundenzentrale.
- [ ] **Publish aktualisieren (14.10.2026, Wiki Preisliste):** Thema
  „Preisliste" mit JSON-Import. Migration legt die Einstellung
  `Preisliste.Verzeichnis` (= `\\192.168.0.203\Produktiv\01_Vertrieb\Preisliste`)
  an — unter Administration → Verzeichnisse prüfen, ggf. auf den Hostnamen
  umstellen. Danach: Wiki → Preisliste → „Neue Version importieren" mit
  `ID.prove_SaaS_Preisliste_v22.json` aus dem Ordner (Redakteur des Themas
  Preisliste, Compliance oder Admin). Der Server braucht keinen Zugriff auf P:,
  der Import läuft per Upload aus dem Browser.
- [ ] **Publish aktualisieren (14.10.2026, FEEDBACK Nr. 22 Wiki):** Bereich
  „Wiki" mit Sanktionslisten-Katalog (28 Listen werden von der Migration
  angelegt), Standardprozesse je Importart, Rolle **Wiki-Redaktion** mit
  Themen-Freigabe; ausführliche Texte (Beschreibung + Relevanz) für alle
  Listen kommen per Migration mit. Nach dem Update: unter Benutzer & Rollen die Redakteure
  festlegen und je Person die Wiki-Themen anhaken (Admin/Compliance dürfen
  ohnehin alles); dann interne Bezeichnungen, Import, Verantwortliche und
  Prozesse pflegen.
- [ ] **Publish aktualisieren (14.10.2026 abends, Wiki Preisliste):** Thema
  „Preisliste" (JSON-Import aus dem Preislisten-Projekt, Seite mit
  Sichtbarkeitsfilter, anordenbare Abschnitte). Migrationen laufen automatisch.
  Nach dem Update: 1) Administration → Verzeichnisse: `Preisliste.Verzeichnis`
  auf `P:\01_Vertrieb\Preisliste` (UNC-Pfad) setzen, 2) unter Benutzer &
  Rollen den Preislisten-Redakteuren das Wiki-Thema „Preisliste" anhaken,
  3) im Wiki → Preisliste die JSON v22 hochladen und Zuständigkeit/Prozess
  pflegen, 4) für v22 eine **Neuerung** erfassen („+ Neuerung erfassen", z. B.
  die zwei neuen Funktionen, Lesebestätigung nach Bedarf) und unter
  „Abschnitte anordnen" festlegen, welche Abschnitte ausgeklappt sind.
  Migration `WikiNeuerungenMeldungen` (Neuerungen, Lesungen, Meldungen) läuft
  automatisch.
- [ ] **Publish aktualisieren (28.09.2026, FEEDBACK Nr. 20 Stufe 2):** Bereich
  „Partner" mit Partner-Akte und Partnerverträgen. Migration läuft automatisch
  (Vertrag gehört Kunde ODER Partner). Manuelle Schritte: keine.
- [ ] **Edge-GPO für „Ordner öffnen"** (einmalig, AD-seitig): Damit der
  Ordner-Knopf den Explorer öffnet, in der GPO die Edge-Richtlinie
  **„IntranetFileLinksEnabled"** („Zulassen, dass Dateilinks der Intranetzone
  in Datei-Explorer geöffnet werden") aktivieren — braucht die Edge-ADMX-
  Vorlagen im Central Store. Ohne die Richtlinie funktioniert ersatzweise
  der „Pfad"-Knopf (kopieren + in Explorer-Adresszeile einfügen).
- [ ] **Odoo-Zugangsdaten eintragen + Import manuell starten** (24.09.2026):
  Administration → Odoo-Anbindung — URL/Datenbank/Benutzer speichern, API-Key
  setzen, dann „Import jetzt ausführen" (Kontakte + Abos, Status wird
  angezeigt). Nach dem App-Update außerdem den Portal-Push-Token neu eintragen
  (Schlüsselbund-Umzug nach ProgramData).
- [ ] *(optional, später)* **Odoo-Import als geplante Aufgabe** — erst nötig,
  wenn der Import automatisch nachts laufen soll; Anleitung im Abschnitt
  „Odoo-Import als geplante Aufgabe", Paket liegt in `publish-import`.
- [x] ~~Umzug auf /Kundenzentrale~~ — erledigt 25.09.2026 (Unteranwendung
  unter der Default Web Site, eigener App-Pool; alte 8081-Site + Firewall-Regel
  entfernt).
- [x] ~~GPO: Intranetzone~~ — erledigt 11.09.2026 (eigene GPO
  „Kundenzentrale Intranetzone", Benutzerkonfiguration, `http://rs-w2019-01` = 1).
  **Korrektur 29.10.2026:** Laut `gpresult /r /scope user` am Arbeitsplatz wirkt
  diese GPO nicht; die Zonenliste kommt aus der GPO **„Lokale Anwendungen"**
  (Benutzerkonfiguration, Registry `HKCU\Software\Policies\…\ZoneMapKey`).
  Änderungen an der Zonenliste dort vornehmen.
  Ursprüngliche Anleitung (einmalig, AD-seitig) — damit KEIN Benutzer je
  einen Anmeldedialog sieht: Benutzerkonfiguration → Administrative Vorlagen
  → Windows-Komponenten → Internet Explorer → Internetsystemsteuerung →
  Sicherheitsseite → „Liste der Site zu Zonenzuweisungen": `http://rs-w2019-01`
  = `1` (Lokales Intranet); später auch den finalen DNS-Namen eintragen.
  Gilt für Edge + Chrome; Firefox bräuchte `network.automatic-ntlm-auth.trusted-uris`.

## Weg A · Windows-Authentifizierung über die lokale AD (Sofort-Deployment)

Ein einziger IIS-Site: die API liefert das Blazor-Frontend gleich mit aus
(kein CORS, keine zweite Site). Anmeldung per Domänen-Konto — der Browser
meldet Intranet-Benutzer automatisch an (SSO), keine Passwörter/Tokens bei uns.

### A1 · Einmalig auf dem Server

1. **ASP.NET Core Hosting Bundle** (Version 10.x) installieren:
   https://dotnet.microsoft.com/download/dotnet/10.0 → „Hosting Bundle" →
   danach `iisreset`.
2. In IIS unter *Server → Windows-Authentifizierung*: Feature installiert?
   (Server-Manager → Rollen → Webserver → Sicherheit → Windows-Authentifizierung.)

### A2 · Veröffentlichen (vom Entwicklungsrechner, je Release)

```bash
dotnet publish src/Rausoft.Erp.Api -c Release -o \\RS-W2019-01\<Freigabe>\Kundenzentrale
```

(Zielordner z. B. `D:\Apps\Kundenzentrale` auf dem Server. Der Publish-Output
enthält Frontend + web.config; Migrationen wendet die App beim Start selbst an.)

### A3 · Konfiguration im Zielordner

`appsettings.Production.json` neben die appsettings.json legen (bleibt bei
späteren Publishes liegen, wenn man den Ordnerinhalt nicht löscht — sonst
vorher sichern):

```json
{
  "Authentifizierung": {
    "Modus": "Windows",
    "ErstAdminKonto": "RS\\mp"
  },
  "ConnectionStrings": {
    "Kundenzentrale": "Server=RS-W2019-01;Database=Kundenzentrale;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True"
  }
}
```

- `ErstAdminKonto` bekommt beim App-Start automatisch die Admin-Rolle.
- Alle anderen Domänen-Benutzer werden beim ersten Zugriff automatisch als
  Mitarbeiter **ohne Rollen** angelegt (Timeline-/Audit-Zuordnung); Rollen
  vergibt später ein Admin (Pflege-UI noch offen, übergangsweise SQL).

### A4 · IIS-Site anlegen

1. Site „Kundenzentrale": physischer Pfad = Zielordner, eigener App-Pool
   (.NET CLR: *No Managed Code*), Binding z. B. `http://kundenzentrale.rs.local`
   (DNS-Eintrag/HOSTS nicht vergessen; HTTPS mit internem Zertifikat gern später).
2. Site → **Authentifizierung**: *Windows-Authentifizierung = Aktiviert*,
   *Anonyme Authentifizierung = Deaktiviert*.
3. **App-Pool-Identität → Datenzugriff** (die App greift mit dieser Identität
   auf SQL und die Freigabe zu):
   - Standard `ApplicationPoolIdentity`: in SSMS Login `IIS APPPOOL\Kundenzentrale`
     anlegen → DB `Kundenzentrale`: `db_datareader` + `db_datawriter` + `db_ddladmin`
     (ddladmin, weil die App Migrationen selbst anwendet).
   - Zugriff auf `\\RS-W2019-01\Kunden Archiv`: bei ApplicationPoolIdentity
     zählt das **Computerkonto** — einfacher ist ein Domänen-Dienstkonto
     (z. B. `RS\svc-kundenzentrale`) als App-Pool-Identität: SQL-Rechte wie
     oben + Lese-/Schreibzugriff auf die Freigabe (Schreiben für die
     Vertragserzeugung nach `2_Wartungsverträge`).
4. Browser-Test von einem Arbeitsplatz: `http://kundenzentrale.rs.local` —
   Anmeldung läuft unsichtbar über das Domänen-Konto (Intranet-Zone).
   Edge/Chrome behandeln `*.rs.local` automatisch als Intranet; falls ein
   Anmeldedialog erscheint, die URL in den Internetoptionen zur Intranet-Zone
   hinzufügen (GPO oder lokal).

### A5 · Nacharbeiten nach dem ersten Start

- **Geheimnisse neu eintragen** (Service-Portal-Push-Token): DPAPI ist
  maschinengebunden — Administration → Server & Portal-Meldung → Token setzen.
- `ServicePortal.IntervallMinuten` auf 60 oder 120 stellen (Auto-Meldung).
- Test-Server (Test-01/02/03, localhost-Healthcheck) löschen oder auf die
  echten Abfragepunkte umstellen, sobald sie vorliegen.
- Backups einrichten (siehe unten).

## HTTPS im Intranet (Weg A, nachziehbar ohne Code-Änderung)

Ziel: `https://rs-w2019-01:8443/Kundenzentrale` mit einem Zertifikat, dem alle
Domänenrechner vertrauen — keine Warnung im Browser, Kerberos/NTLM und
Kundendaten laufen nicht mehr im Klartext. Reihenfolge, ca. 30–45 Minuten:

0. **Vorab (ergänzt 27.10.2026 für den Outlook-Knopf):**
   - Gibt es eine AD-Zertifizierungsstelle? Auf dem Server `certutil -ADCA`
     ausführen — listet sie auf, sonst Fehlermeldung → Variante „Ohne AD CS".
   - **Endgültigen Namen jetzt festlegen**, z. B. `kundenzentrale.<domäne>`:
     Das Manifest des Outlook-Add-ins enthält die Adresse fest; ein späterer
     Namenswechsel hieße neu verteilen. DNS-Manager → Forward-Lookupzone der
     Domäne → neuer **CNAME** `kundenzentrale` → `rs-w2019-01.<domäne>`.
     Den Namen gleich mit ins Zertifikat aufnehmen (Schritt 1).

1. **Zertifikat beschaffen** (eine der beiden Varianten):
   - *Mit AD-Zertifizierungsstelle (AD CS) im Haus:* Auf dem Server
     `certlm.msc` → Eigene Zertifikate → Alle Aufgaben → **Neues Zertifikat
     anfordern** → Vorlage „Webserver" → Antragstellername: Allgemeiner Name
     `rs-w2019-01`, zusätzlich DNS-Namen `rs-w2019-01` und
     `rs-w2019-01.<domäne>` und den festen Namen
     `kundenzentrale.<domäne>` → Registrieren. Domänenrechner vertrauen der
     AD CS automatisch (Stammzertifikat kommt per GPO).
   - *Ohne AD CS:* Selbstsigniertes Zertifikat auf dem Server erzeugen
     (`New-SelfSignedCertificate -DnsName kundenzentrale.<domäne>,rs-w2019-01,rs-w2019-01.<domäne>
     -CertStoreLocation cert:\LocalMachine\My -NotAfter (Get-Date).AddYears(3)`),
     es als `.cer` exportieren und per GPO (Computerkonfiguration → Richtlinien
     → Windows-Einstellungen → Sicherheitseinstellungen → Richtlinien für
     öffentliche Schlüssel → **Vertrauenswürdige Stammzertifizierungsstellen**)
     an alle Rechner verteilen. Funktioniert, ist aber Handarbeit bei Ablauf.
2. **IIS-Binding — auf rs-w2019-01 Port 8443, nicht 443!** Port 443 gehört
   dort der IIS-Site **iddproveOnline** (Bindung `192.168.0.203:443`, Zertifikat
   „IDPServer_RS-W2019-01", gültig bis 9999). Weil die Bindung an die feste IP
   geht, gewinnt sie gegen jede `*:443`-Bindung der Default Web Site — Folge:
   altes Zertifikat und „Seite nicht gefunden" (Diagnose 29.10.2026 mit
   `netsh http show sslcert`). **iddproveOnline nicht anfassen.**
   IIS-Manager → Default Web Site → Bindungen → https-Eintrag bearbeiten →
   Port `8443`, IP „Keine zugewiesen", Hostname leer, SSL-Zertifikat
   „Kundenzentrale HTTPS" (CN `kundenzentrale.rs.local`) → OK. Firewall:
   `New-NetFirewallRule -DisplayName "Kundenzentrale HTTPS 8443" -Direction Inbound -Protocol TCP -LocalPort 8443 -Action Allow -Profile Domain`.
   Die Unteranwendung `/Kundenzentrale` erbt das Binding.
3. **Test im Browser (Edge/Chrome, nicht Firefox):**
   `https://rs-w2019-01:8443/Kundenzentrale` muss ohne Warnung laden und die
   Windows-Anmeldung unsichtbar durchlaufen. Die Zoneneinträge in der GPO
   „Lokale Anwendungen" gelten ohne Portangabe für alle Ports; Kerberos nutzt
   den SPN ohne Port — beides bleibt unverändert.
   Kerberos unter dem neuen Namen (sonst fällt die Anmeldung still auf NTLM
   zurück, funktioniert aber): einmalig als Domänen-Admin
   `setspn -S HTTP/kundenzentrale.<domäne> RS-W2019-01` — bei
   ApplicationPoolIdentity gilt das Computerkonto; läuft der App-Pool unter
   einem Dienstkonto, dieses statt `RS-W2019-01` angeben.
4. **Umleitung einschalten:** in `appsettings.Production.json` ergänzen
   ```json
   "Hosting": { "HttpsErzwingen": true }
   ```
   → App-Pool neu starten. Ab jetzt leitet die App http auf https (Port 8443,
   ermittelt das ASP.NET-Core-Modul aus der IIS-Bindung) um; alle
   Links/Lesezeichen auf http funktionieren weiter.
   **HSTS bleibt aus** (`Hosting:Hsts` = false, Standard): HSTS gilt für den
   ganzen Rechnernamen und würde `http://rs-w2019-01/Urlaubsplaner` auf
   `https://rs-w2019-01` (Port 443 = iddproveOnline) zwingen → kaputt.
   Hat ein Browser HSTS schon einmal gelernt: `edge://net-internals/#hsts`
   → „Delete domain security policies" → `rs-w2019-01`.
5. **Import-Task:** nichts zu tun (spricht nur mit SQL Server und Odoo).
6. **Ablauf überwachen:** das neue Zertifikat in der Kundenzentrale unter
   Server → SSL-Zertifikate eintragen (Erinnerung vor Ablauf). AD-CS-
   Webserverzertifikate laufen meist nach 1–2 Jahren ab.
7. **Lesezeichen/Verknüpfungen** im Team auf
   `https://rs-w2019-01:8443/Kundenzentrale` umstellen.

Bis Schritt 4 läuft alles wie bisher über http — der Schalter macht die
Umstellung jederzeit rückgängig (`false` + App-Pool-Neustart).

## Weg B · Entra ID (spätere Option, z. B. für Zugriff von außen/M365-SSO)

<details>
<summary>Anleitung aufklappen (Stand 03.09.2026)</summary>

### B1 · Entra-ID-App-Registrierungen (Azure-Portal, einmalig)

Portal: https://entra.microsoft.com → Anwendungen → App-Registrierungen.
Es braucht **zwei** Registrierungen:

**API („Kundenzentrale API")**

1. Neue Registrierung → Name `Kundenzentrale API`, Konten: *nur dieses
   Organisationsverzeichnis*, ohne Redirect-URI → Registrieren.
2. **Übernehmen:** Anwendungs-ID (Client-ID) und Verzeichnis-ID (Tenant-ID)
   von der Übersichtsseite.
3. *Eine API verfügbar machen* → App-ID-URI übernehmen (Standard
   `api://<client-id>`) → *Bereich hinzufügen*:
   Name `Zugriff`, wer kann einwilligen: Administratoren und Benutzer,
   Anzeigename/Beschreibung z. B. „Zugriff auf die Kundenzentrale".

**Frontend („Kundenzentrale Web")**

1. Neue Registrierung → Name `Kundenzentrale Web`, Konten: *nur dieses
   Organisationsverzeichnis*, Redirect-URI: Typ **Single-Page-Anwendung (SPA)**,
   URI `https://kundenzentrale.rs.local/authentication/login-callback`
   (an die echte URL anpassen; für Tests zusätzlich
   `http://localhost:5100/authentication/login-callback`).
2. *API-Berechtigungen* → Berechtigung hinzufügen → *Eigene APIs* →
   `Kundenzentrale API` → Bereich `Zugriff` anhaken →
   **Administratorzustimmung erteilen** (Button).
3. Client-ID dieser Registrierung ebenfalls notieren.

**Werte eintragen**

- `appsettings.Production.json` → `Authentifizierung:Modus` auf `EntraId`,
  Sektion `EntraId`: TenantId + ClientId der **API**-Registrierung
  (keine Secrets nötig — die API validiert nur Tokens).
- Frontend: MSAL-Konfiguration (TenantId, ClientId **Web**, Scope
  `api://…/Zugriff`) — der MSAL-Einbau in Blazor ist noch offen und wird
  gemacht, sobald die IDs vorliegen.
- Mitarbeiter brauchen dann ihre Entra-Objekt-ID am Datensatz (Feld
  `EntraObjectId`); das Windows-Konto kann parallel bestehen bleiben.
- CTI/Service-Portal später: je eigene Registrierung mit Client Credentials.

</details>

## Odoo-Import als geplante Aufgabe auf dem Server (Variante B, beschlossen 24.09.2026)

Der Import (Kontakte, Abos, Kündigungsautomatik, Archiv-Rückholung,
Merkmal-Prüfhinweise) läuft nächtlich auf RS-W2019-01 statt manuell vom
Entwicklungsrechner. **Die Odoo-Zugangsdaten werden in der Kundenzentrale
gepflegt** (Administration → Odoo-Anbindung); der API-Key liegt verschlüsselt
in der DB — keine Ini-Dateien, keine Secrets in Umgebungsvariablen. Web-App
und Import teilen sich dafür den Data-Protection-Schlüsselbund unter
`C:\ProgramData\Rausoft\Kundenzentrale\Schluessel` (DPAPI, maschinengebunden).

1. **Zugangsdaten eintragen:** Kundenzentrale → Administration →
   **Odoo-Anbindung**: Odoo-URL, Datenbank, API-Benutzer speichern und den
   API-Key setzen (wird verschlüsselt, nie wieder angezeigt).
2. **Paket kopieren:** `dotnet publish src/Rausoft.Erp.OdooImport -c Release -o publish-import`
   (macht Claude) → Inhalt nach `C:\Kundenzentrale-Import` auf dem Server.
3. **Eine Umgebungsvariable** (kein Secret — nur der DB-Verweis mit Windows-Auth):
   ```powershell
   [Environment]::SetEnvironmentVariable("KUNDENZENTRALE_DB", "Server=RS-W2019-01;Database=Kundenzentrale;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True", "Machine")
   ```
4. **Schlüsselbund-Ordner absichern** (einmalig; die Web-App legt ihn beim
   ersten Start nach dem Update an):
   ```powershell
   icacls "C:\ProgramData\Rausoft\Kundenzentrale" /inheritance:r /grant "Administratoren:(OI)(CI)F" "IIS APPPOOL\Kundenzentrale:(OI)(CI)M" "<Aufgaben-Konto>:(OI)(CI)RX"
   ```
   (`<Aufgaben-Konto>` = das Konto der geplanten Aufgabe; bei deutschsprachigem
   Server ggf. „VORDEFINIERT\Administratoren".)
5. **Test:** `C:\Kundenzentrale-Import\Rausoft.Erp.OdooImport.exe zugangstest`
   unter dem Aufgaben-Konto — zeigt, ob URL/DB/Benutzer/Key auflösbar sind
   (der Key selbst wird nie ausgegeben).
6. **Aufgabenplanung** → „Aufgabe erstellen":
   - Name `Kundenzentrale Odoo-Import`; Konto mit SQL-Rechten auf die DB
     (db_datareader + db_datawriter, z. B. dasselbe Dienstkonto wie der
     App-Pool); „Unabhängig von der Benutzeranmeldung ausführen".
   - Trigger: täglich 03:00.
   - Aktion 1: Programm `cmd.exe`, Argumente
     `/c ""C:\Kundenzentrale-Import\Rausoft.Erp.OdooImport.exe" import >> "C:\Kundenzentrale-Import\import.log" 2>&1"`
   - Aktion 2: wie Aktion 1, nur mit `abos` statt `import`.
7. Einmal per „Ausführen" testen und `import.log` prüfen.
8. `vertragsscan` (neue Dateien im Kunden Archiv) bleibt bewusst manuell bzw.
   kann als eigene wöchentliche Aufgabe ergänzt werden — das Konto braucht
   dafür Lesezugriff auf die Freigabe.
9. `chatter` (Vorstufe zum Timeline-Import, **nur lesen**, manuell):
   - `Rausoft.Erp.OdooImport.exe chatter` — Gesamtüberblick über den
     Odoo-Chatter (`mail.message`): Anzahl je Modell, Nachrichtenart,
     Untertyp, Jahr und Autor.
   - `Rausoft.Erp.OdooImport.exe chatter --kunde <KundenNr|Odoo-ID|Name> --ausgabe C:\Temp\chatter-12345.txt`
     — Chronologie eines Beispielkunden (Firma, Personen/Adressen, CRM-Leads,
     Angebote/Verkaufsaufträge, Rechnungen) mit Anhängen und der Entscheidung
     der Filterregeln aus `ChatterFilter.cs`:
     `[+]` übernehmen, `[~]` zu Systemeintrag verdichten, `[-]` nicht
     übernehmen. Dient dazu, die Regeln an echten Daten zu bewerten.
10. `chatter-import` (Odoo-Chatter → Kunden-Timeline, Stand 22.10.2026):
    - Voraussetzungen: Kontakt-Import ist gelaufen (Kunden haben Odoo-Ids),
      Migration `ChatterImport` ist eingespielt, und **Administration →
      Verzeichnisse → Dokumentablage** zeigt auf ein erreichbares Verzeichnis —
      dort landen die Anhänge unter `<Kundennummer>\Odoo-Chatter\`.
    - `Rausoft.Erp.OdooImport.exe chatter-import --dry-run` — Bericht, was
      übernommen, verdichtet und verworfen würde, inkl. Anhang-Volumen.
      `--kunde 25757` beschränkt auf einen Kunden (KundenNr oder Odoo-Id),
      `--ohne-anhaenge` lässt die Dateien weg.
    - `Rausoft.Erp.OdooImport.exe chatter-import` — Übernahme. Wiederholbar:
      bereits übernommene Nachrichten (Aktivitaet.OdooId) werden übersprungen,
      fehlende Anhänge nachgeladen. Kann während des Parallelbetriebs als
      dritte Aktion der nächtlichen Aufgabe laufen (nach `import` und `abos`),
      damit neue Odoo-Notizen in der Akte erscheinen.
    - In der Kundenakte blendet der Schalter „Odoo-Verlauf" die übernommenen
      Einträge ein oder aus; Anhänge werden über die API aus der Dokumentablage
      geladen. Passwort-Zeilen aus Zugangsdaten-Mails sind beim Import
      maskiert worden.

**Wichtig nach diesem Update:** Der Schlüsselbund zieht in den
ProgramData-Ordner um — zuvor gesetzte Geheimnisse (Portal-Push-Token) sind
danach ungültig und müssen einmal neu eingetragen werden.

## Test- und Produktivsystem auf RS-W2019-01 (SICHERHEIT.md E4, Code seit 10.11.2026)

Zwei getrennte Instanzen auf demselben Server. Die heutige Instanz
`/Kundenzentrale` wird mit dem Produktiv-Reset zum **Produktivsystem**
(Adresse, Zertifikat und Lesezeichen bleiben); das **Testsystem** kommt neu
daneben.

| | Produktiv | Test |
|---|---|---|
| Adresse | `https://rs-w2019-01:8443/Kundenzentrale` | `https://rs-w2019-01:8443/Kundenzentrale-Test` |
| Ordner | `C:\Kundenzentrale` | `C:\Kundenzentrale-Test` |
| App-Pool | „Kundenzentrale" | „Kundenzentrale-Test" |
| Datenbank | `Kundenzentrale` | `Kundenzentrale_Test` |
| Schlüsselbund | `C:\ProgramData\Rausoft\Kundenzentrale\Schluessel` | `C:\ProgramData\Rausoft\Kundenzentrale-Test\Schluessel` |
| Ablagen | Kunden Archiv | nur unter `E:\Kundenzentrale-Test-Daten\Ablage` |
| Hintergrund-Takte, Portal-Push | an | aus |
| E-Mails | an | nur an die Umleitungsadresse (oder keine) |
| Nächtlicher Odoo-Import | geplante Aufgabe | keine Aufgabe (Import nur manuell) |

**Was der Schalter `Instanz:Testsystem` = true im Code bewirkt** (unabhängig
davon, was in der Datenbank eingestellt ist):

- Die drei Hintergrund-Takte laufen nicht (Server-Status-Meldung,
  Zertifikats-Erinnerungen, Vorgangs-Abgleich) — alles nur auf Knopfdruck.
- Die Meldung ans Service Portal ist gesperrt (Knopf meldet „abgeschaltet").
- E-Mails gehen ausschließlich an `Instanz:MailUmleitung`, Betreff mit
  „[TEST]", ursprüngliche Empfänger stehen im Text. Ohne Umleitungsadresse
  wird nichts versendet (nur Log-Eintrag).
- Alle Ablage-Verzeichnisse müssen unter `Instanz:AblageWurzel` liegen. Beim
  Start werden Einstellungen, die herauszeigen, auf Unterordner der Wurzel
  umgestellt (`Vertragsablage`, `Dokumentablage`, `Vorlagen`, `Erzeugt`,
  `Vertraulich`, `Preisliste`), das Signatur-Zertifikat wird geleert; die
  Administration lehnt Pfade außerhalb ab. Damit ist eine Testdatenbank aus
  einer Produktiv-Sicherung sofort ungefährlich.
- Das Frontend zeigt ein Band „TESTSYSTEM" über der ganzen Breite.
- Ohne `SchluesselOrdner` oder `AblageWurzel` startet ein Testsystem nicht
  (500.30, Meldung im Ereignisprotokoll) — Absicht.

Der getrennte Schlüsselbund sorgt außerdem dafür, dass Geheimnisse aus einer
Produktiv-Kopie (Odoo-Key, SMTP-Passwort, Portal-Token, PFX-Passwort) im
Testsystem **nicht entschlüsselbar** sind; wer im Test gegen Odoo importieren
will, trägt den Key dort neu ein (der Import liest Odoo nur).

### T1 · Testsystem einmalig einrichten

1. Ordner `C:\Kundenzentrale-Test` und `E:\Kundenzentrale-Test-Daten\Ablage` anlegen.
2. Leere Datenbank `Kundenzentrale_Test` anlegen (SSMS), Login
   `IIS APPPOOL\Kundenzentrale-Test` mit `db_datareader`, `db_datawriter`,
   `db_ddladmin` **nur auf dieser Datenbank** — das Test-Konto bekommt kein
   Recht auf `Kundenzentrale`, das Produktiv-Konto keines auf die Test-DB.
   Der App-Pool muss vorher existieren, sonst kennt Windows das Konto nicht.
   ```sql
   CREATE LOGIN [IIS APPPOOL\Kundenzentrale-Test] FROM WINDOWS;
   GO
   USE Kundenzentrale_Test;
   GO
   CREATE USER [IIS APPPOOL\Kundenzentrale-Test] FOR LOGIN [IIS APPPOOL\Kundenzentrale-Test];
   ALTER ROLE db_datareader ADD MEMBER [IIS APPPOOL\Kundenzentrale-Test];
   ALTER ROLE db_datawriter ADD MEMBER [IIS APPPOOL\Kundenzentrale-Test];
   ALTER ROLE db_ddladmin   ADD MEMBER [IIS APPPOOL\Kundenzentrale-Test];
   ```
   Tabellen legt die App beim ersten Start selbst an; Sortierung = Server-Standard
   (wie die Produktiv-DB).
3. IIS: App-Pool „Kundenzentrale-Test" (No Managed Code), unter der Default
   Web Site **Anwendung hinzufügen** → Alias `Kundenzentrale-Test`, Pfad
   `C:\Kundenzentrale-Test`, Windows-Authentifizierung an, Anonym aus.
   Binding, Zertifikat und Intranet-Zone erbt die Anwendung. **Nicht**
   „Website hinzufügen" (eigene Site kollidiert mit Port 8443). Im Dialog den
   Pool ausdrücklich wählen — Vorgabe ist `DefaultAppPool`. Kontrolle:
   ```powershell
   Get-WebApplication -Site "Default Web Site" | Format-List path, applicationPool
   ```

   **Stolperfallen der Ersteinrichtung (10.11.2026):** Läuft die Anwendung im
   falschen Pool oder fehlt das SQL-Login, meldet der Start 500.30 mit
   „Fehler bei der Anmeldung für den Benutzer RS\RS-W2019-01$" — SQL Server
   nennt ersatzweise das Computerkonto. Dem Computerkonto **keine** Rechte
   geben. Nach einem gescheiterten Start wiederholt der Prozess die
   Fehlerseite, bis der **richtige** Pool neu gestartet wird. Laufwerk `D:`
   ist auf dem Server kein Datenlaufwerk (Ablage liegt auf `E:`).
4. NTFS:
   ```powershell
   icacls "E:\Kundenzentrale-Test-Daten\Ablage" /grant "IIS APPPOOL\Kundenzentrale-Test:(OI)(CI)M"
   ```
   Der Schlüsselbund-Ordner entsteht beim ersten Start; danach absichern:
   ```powershell
   icacls "C:\ProgramData\Rausoft\Kundenzentrale-Test" /inheritance:r /grant "Administratoren:(OI)(CI)F" "IIS APPPOOL\Kundenzentrale-Test:(OI)(CI)M"
   ```
   Das Test-Konto bekommt **kein** Recht auf das Kunden Archiv und auf den
   Produktiv-Schlüsselbund (dessen ACL nennt nur das Produktiv-Konto).
5. Publish-Inhalt nach `C:\Kundenzentrale-Test` kopieren und dort
   `appsettings.Production.json` anlegen:
   ```json
   {
     "Instanz": {
       "Testsystem": true,
       "Bezeichnung": "Testsystem",
       "SchluesselOrdner": "C:\\ProgramData\\Rausoft\\Kundenzentrale-Test\\Schluessel",
       "AblageWurzel": "E:\\Kundenzentrale-Test-Daten\\Ablage",
       "MailUmleitung": ""
     },
     "Hosting": { "HttpsErzwingen": true },
     "Authentifizierung": { "Modus": "Windows", "ErstAdminKonto": "RS\\mp" },
     "ConnectionStrings": {
       "Kundenzentrale": "Server=RS-W2019-01;Database=Kundenzentrale_Test;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True"
     }
   }
   ```
6. App-Pool starten, `https://rs-w2019-01:8443/Kundenzentrale-Test` öffnen:
   das Band „TESTSYSTEM" muss sichtbar sein. Kontrolle unter Administration →
   Verzeichnisse: alle Pfade liegen unter `E:\Kundenzentrale-Test-Daten\Ablage`.
7. Vorlagen zum Ausprobieren der Vertragserzeugung nach
   `E:\Kundenzentrale-Test-Daten\Ablage\Vorlagen` kopieren.

Die Produktiv-Instanz braucht **keine** Änderung an ihrer
`appsettings.Production.json` (ohne Sektion `Instanz` gilt: kein Testsystem,
Standard-Schlüsselbund).

### T2 · Release-Weg (ersetzt das bisherige Update-Ritual)

1. Vorab wie bisher: Schwachstellen-Scan, `publish` frisch bauen.
2. **Test:** App-Pool „Kundenzentrale-Test" beenden → `wwwroot\_framework`
   leeren → `publish` nach `C:\Kundenzentrale-Test` kopieren → starten →
   prüfen (Migration lief durch, neue Funktion, Strg+F5).
3. **Produktiv:** erst danach **denselben** `publish`-Ordner nach
   `C:\Kundenzentrale` — nicht neu bauen. Eine Migration ist dann schon einmal
   gegen eine Datenbank gelaufen; vor Migrationen, die Daten umbauen, vorher
   eine Vollsicherung der Produktiv-DB ziehen.

### T3 · Testdatenbank aus einer Produktiv-Sicherung auffrischen

```sql
ALTER DATABASE Kundenzentrale_Test SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
RESTORE DATABASE Kundenzentrale_Test FROM DISK = N'<Sicherung>.bak'
  WITH REPLACE,
       MOVE N'Kundenzentrale'     TO N'<Datenpfad>\Kundenzentrale_Test.mdf',
       MOVE N'Kundenzentrale_log' TO N'<Datenpfad>\Kundenzentrale_Test_log.ldf';
ALTER DATABASE Kundenzentrale_Test SET MULTI_USER;
```

(App-Pool „Kundenzentrale-Test" vorher beenden; logische Dateinamen mit
`RESTORE FILELISTONLY` prüfen; danach den DB-Benutzer des Test-App-Pools neu
zuordnen.) Beim nächsten Start stellt das Testsystem die Verzeichnisse um; die
Geheimnisse sind unlesbar. Die Kopie enthält **echte Kundendaten** — das
Testsystem ist deshalb genauso zugriffsgeschützt wie das Produktivsystem
(Rollen kommen aus der Kopie mit).

### T4 · Import-Tool gegen eine bestimmte Instanz

Die maschinenweite Variable `KUNDENZENTRALE_DB` zeigt auf **Produktiv**. Für
das Testsystem das Ziel am Aufruf angeben — das Tool nennt in der ersten Zeile,
gegen welche Datenbank es läuft:

```powershell
C:\Kundenzentrale-Import\Rausoft.Erp.OdooImport.exe zugangstest --db "Server=RS-W2019-01;Database=Kundenzentrale_Test;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True" --schluessel "C:\ProgramData\Rausoft\Kundenzentrale-Test\Schluessel"
```

Lokal lässt sich der Testmodus mit dem Startprofil `http-testsystem`
(Vorschau `api-testsystem`, Port 5407) ausprobieren: eigene LocalDB
`Kundenzentrale_Testsystem`, Ablage und Schlüsselbund unter `.dev-testsystem`.

## Produktiv-Reset zum Zeitpunkt X (beschlossen 10.09.2026, neu gefasst 10.11.2026)

Das Live-System läuft zunächst als **offener Testbetrieb für alle** —
zum Zeitpunkt X wird einmal sauber resettet und dann produktiv gearbeitet.
Ein Stichtag steht noch nicht fest.

**Entscheidung MP 10.11.2026:** Beim Reset wird **alles gelöscht außer
Administration und Wiki**. Die manuelle Pflege an Kunden und Verträgen aus der
Testphase wird bewusst **nicht** gerettet (das Rettungsskript vom 10.09./28.09.
entfällt). Die Datenbank wird deshalb nicht mehr neu angelegt, sondern
bereinigt — Modus `produktiv-reset` des Import-Tools.

| Bleibt | Wird gelöscht |
|---|---|
| Mitarbeiter mit Rollen und Wiki-Themen, Bereiche | Kunden, Ansprechpartner, Adressen |
| Einstellungen (Verzeichnisse, Odoo, Mail, Zertifikate) | Partner und Partnerverträge |
| Geheimnisse (Odoo-Key, SMTP, Portal-Token, PFX) | Verträge, Bestandteile, Dokument-Einträge |
| Serverliste, SSL-Zertifikate mit Verlauf | Abos und Positionen |
| Merkmal-Stammliste | Verlauf (Odoo-Chatter und manuell), Vereinbarungen |
| Vorgangsvorlagen (Typen und Schritte) | Aufgaben, Vorgänge mit ihren Schritten |
| Vertragsvorlagen mit Dokumentart und Rollen-Freigabe | Vorlagen-Versionen (Historie beginnt wieder bei v1) |
| Wiki komplett: Sanktionslisten, Standardprozesse, Preisliste, FAQ, Kommunikationsleitlinien, Neuerungen, Lesebestätigungen, Meldungen | Audit-Log; Nummernkreise beginnen wieder bei 1 |

Verloren geht damit ausdrücklich auch: Merkmale am Kunden, Archiv-Status,
Individualentwicklung, Server-Zuordnung und Mandanten-Kennung, manuell geklärte
Vertragsstatus, Position/Funktion der Ansprechpartner, korrigierte
Dokumentdaten, Vertraulich-Kennzeichen, „Partner ist auch Kunde". Der
Kundenbezug von FAQ-Einträgen wird geleert (die Vorschau listet die
betroffenen Einträge mit Kundennummer zum Nachtragen).

Ablauf (macht Claude zusammen mit MP):

0. **Probe im Testsystem:** Testdatenbank aus einer Produktiv-Sicherung
   auffrischen (T3), dort Schritt 2 bis 4 einmal komplett durchspielen.
1. **Sichern:** Vollsicherung der Produktiv-DB (bleibt als Rückweg liegen).
   App-Pool „Kundenzentrale" beenden.
2. **Vorschau**, dann **Reset** (läuft in einer Transaktion — alles oder nichts):
   ```powershell
   C:\Kundenzentrale-Import\Rausoft.Erp.OdooImport.exe produktiv-reset
   ```
   ```powershell
   C:\Kundenzentrale-Import\Rausoft.Erp.OdooImport.exe produktiv-reset --ausfuehren --bestaetigen Kundenzentrale
   ```
   `--bestaetigen` muss den Namen der Datenbank nennen, die das Tool in der
   ersten Zeile ausgibt; sonst wird nichts gelöscht. Fürs Testsystem zusätzlich
   `--db "…Kundenzentrale_Test…"` (T4).
3. App-Pool starten. **Importe neu laufen lassen:** Odoo-Kontakte (`import`),
   Abos (`abos`), Vertragsbestand (`vertragsscan`), Chatter (`chatter-import`).
4. **Nacharbeiten in der Anwendung:**
   - Test-Server (Test-01/02/03) löschen, echte Server prüfen.
   - Administration → Verzeichnisse: `Vertragserzeugung.Zielverzeichnis` vom
     Testordner zurück auf `2_Wartungsverträge`.
   - Vorlagen neu freigeben (v1), vorher ist in der Akte keine wählbar.
   - Die drei „Beispiel:"-Entwürfe der Kommunikationsleitlinien anpassen oder löschen.
   - Benutzer & Rollen durchsehen (automatisch angelegte Konten ohne Rollen; V3b entscheiden).
   - Administration → Dokumentdaten nachtragen (nach dem `vertragsscan`).
5. **Dateien aufräumen** (der Reset fasst keine Dateien an): Testordner der
   Vertragserzeugung, `<KundenNr>\Odoo-Chatter\` in der Dokumentablage,
   eingefrorene Vorlagen-Kopien im Unterordner `Freigegeben`.
6. **Produktivbetrieb einschalten:** nächtliche Aufgabe mit `import`, `abos`,
   `chatter-import`; Backups (V10); `ServicePortal.IntervallMinuten` setzen.

**Empfehlung für die Testphase (am besten sofort):** In der Administration
das `Vertragserzeugung.Zielverzeichnis` auf einen Testordner umstellen
(z. B. `\\RS-W2019-01\Kunden Archiv\0_Test_Erzeugt`), damit Test-Verträge
nicht zwischen den echten Dateien in `2_Wartungsverträge` landen — der
Ordner wird beim Reset einfach komplett gelöscht und die Einstellung
zurückgestellt.

## Datenbank-Betrieb

- **Backups einrichten** (SSMS → Verwaltung → Wartungspläne, oder Agent-Jobs):
  täglich Vollsicherung + Transaktionsprotokoll alle 1–4 h
  (Recovery-Modell FULL), Sicherungen auf ein anderes Volume/Share.
- SQL Server 2017: erweiterter Support endet **Okt 2027** — Upgrade einplanen.

## Offen (Code-Schritte, macht Claude bei Bedarf)

- Mitarbeiter-Pflege-UI (Rollen, Anzeigename, E-Mail) im Admin-Bereich
- Bei Wechsel auf Entra: MSAL im Blazor-Frontend + Produktions-CORS
