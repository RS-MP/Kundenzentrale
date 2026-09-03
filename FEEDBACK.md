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

Status: offen (Anforderung, umsetzen nach der Sammelphase)

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

Status: Konzept in Diskussion — Entscheidung ausstehend, dann Umsetzung in der App

## Nr. 5 · Weitere Bestände aufnehmen (2026-09-01, MP)

Im Kunden Archiv liegen weitere Ordner mit demselben KundenNr-Dateinamen-Muster:
1_Bestellungen (1.929), 3_Installations-Protokolle (27), 9_Kündigungen (2),
Verträge_NTT Data_Kunden (8), 5_Softwaremiete (1), 6_DORA (1), 4_Datenschutz (2).
In Odoo außerdem 1.150 Bestellungen (sale.order) und 1.542 Ausgangsrechnungen.
→ Scanner um Dokumentarten je Ordner verallgemeinern; Import nach Mockup-Entscheid
(Konzept sieht Dokumentarten in Akte + Verlauf bereits vor).

Status: offen

## Backlog (bereits bekannt, kein neues Feedback)

- Laufzeiten/Kündigungsfristen der 776 Bestandsverträge nachpflegen
  (Notiz-Marker „Bestandsimport" filterbar machen?)
- 6 nicht zugeordnete Vertragsdateien manuell erfassen: DowJones.pdf,
  perstorp.pdf, -17102.pdf, 12822.pdf, 15824.pdf, 233305.pdf
- KundenNr-Dubletten in Odoo bereinigen (13 Stück, siehe Import-Warnungen)
- Entra-App-Registrierung + IIS-Deployment auf RS-W2019-01
  (ASP.NET Core Hosting Bundle nötig; erst Entra, sonst keine Anmeldung möglich)
- Backup-Plan für die Produktiv-DB bestätigen
- SQL Server 2017 auf RS-W2019-01: Support endet Okt 2027, Upgrade einplanen
