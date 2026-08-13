# Datenmodell — Phase 1 (Stand 2026-08-13)

Umfang Phase 1: Kontaktverwaltung/Kundenstamm + Vertragsverwaltung + Querschnitt
(Benutzer, Bereiche, Nummernkreise, Timeline-Tabelle, Audit-Log). Spätere Phasen
(Aufgaben, Angebote, Abos, Rechnungen) docken an die hier definierten Tabellen an —
die Kette Angebot → Vertrag → Abo → Rechnung ist von Anfang an mitgedacht.

Technik: EF Core 10 (Code First) gegen SQL Server, int-Identity-PKs,
`rowversion`-Concurrency auf den Stammtabellen, `OdooId` als Import-Referenz.

## Querschnitt (`Rausoft.Erp.Domain.Shared`)

### Mitarbeiter
rausoft-Benutzer. Authentifizierung läuft komplett über Entra ID — hier liegen
**keine Passwörter/Tokens**, nur die Zuordnung Entra-Konto → interner Datensatz.

| Feld | Typ | Anmerkung |
|---|---|---|
| Id | int PK | |
| EntraObjectId | Guid, unique | Objekt-ID des M365-Kontos |
| Anzeigename | nvarchar(100) | z. B. „D. Stauch" |
| Email | nvarchar(200), unique | |
| BereichId | FK → Bereich, null | Heimat-Bereich |
| Rollen | int (Flags-Enum) | Admin, Vertrieb, Support, Buchhaltung — serverseitig durchgesetzt |
| Aktiv | bit | Deaktivieren statt Löschen (Timeline-Referenzen) |

### Bereich
Vertrieb, Support, Buchhaltung … Grundlage der zweistufigen Zuweisung
(Bereich immer, Person optional) ab Phase 2; Mitarbeiter referenzieren ihn schon jetzt.

### Nummernkreis
Zentrale Vergabe (Leitplanke). `Schluessel` (unique, z. B. `Kunde`, `Vertrag`,
später `Rechnung`), `Praefix`, `NaechsterWert`, `Format` (z. B. `{0:D5}`).
Vergabe in einer Transaktion mit Rowversion-Check — keine Lücken/Dubletten.

### Aktivitaet (Timeline)
Generische Aktivitäten-Tabelle (Leitplanke) — Phase 1 schreibt bereits
Vertrags- und Kontaktereignisse hinein, Phase 2 (E-Mail, Anrufe, Aufgaben) baut darauf auf.

| Feld | Typ | Anmerkung |
|---|---|---|
| Id | int PK | |
| KundeId | FK → Kunde | Pflicht — Timeline hängt an der Kundenakte |
| Typ | Enum | Notiz, Anruf, EMail, Vertrag, Angebot, Rechnung, Ticket, Aufgabe, System |
| Zeitpunkt | datetime2 | |
| MitarbeiterId | FK, null | rausoft-Seite des Gesprächspaars |
| AnsprechpartnerId | FK, null | Kundenseite des Gesprächspaars |
| Richtung | Enum, null | Eingehend / Ausgehend / Intern (für „System → Einkauf") |
| Betreff | nvarchar(300) | Anzeigetext in der Timeline |
| Inhalt | nvarchar(max), null | Details/Notiztext |
| VerweisTyp / VerweisId | Enum + int, null | Sprungziel (Vertrag, später Aufgabe/Rechnung …) |

### AuditLog
`Zeitpunkt`, `MitarbeiterId?`, `Entitaet`, `EntitaetId`, `Aktion`
(Created/Updated/Deleted), `AenderungenJson` (alte/neue Werte). Wird zentral im
`DbContext.SaveChanges` befüllt.

## Kontakte (`Rausoft.Erp.Domain.Customers`)

Leitplanke: **Kunde ≠ Ansprechpartner ≠ Adresse ≠ Rechnungsempfänger.**

### Kunde

| Feld | Typ | Anmerkung |
|---|---|---|
| Id | int PK | |
| Kundennummer | nvarchar(20), unique | aus Nummernkreis |
| Firmenname | nvarchar(200) | |
| Kundengruppe | nvarchar(100), null | |
| Betriebsart | Enum, null | SharedCloud / PrivateCloud / OnPrem |
| Status | Enum | Aktiv / Inaktiv (Lead kommt in Phase 3) |
| VermitteltDurchPartnerId | FK → Partner, null | Tipp-Geber (CSB-Modell) |
| RechnungsempfaengerPartnerId | FK → Partner, null | gesetzt = „Rechnung an Partner" (FineSolutions-Modell); leer = Rechnung an den Kunden selbst |
| OdooId | int, null, unique | Import-Referenz |
| Notiz | nvarchar(max), null | |

Die beiden Partner-FKs bilden die zwei realen Modelle ab: CSB (Vermittlung,
Endkunde bleibt Rechnungspartner) und FineSolutions (Partner ist Rechnungsempfänger,
Endkunde wird trotzdem als Kunde geführt). Das Band „Endkunde via Partner ·
Rechnung an Partner" im Akten-Kopf leitet sich direkt daraus ab.

### Partner
Eigener Kontakttyp mit Provisionsmodell (Leitplanke): `Name`, Adressfelder,
`Email`, `Telefon`, `Provisionsmodell` (Enum: TippProvision / Vertriebspartner),
`ProvisionProzent` (decimal(5,2), z. B. 15.00), `OdooId?`, `Aktiv`.

### Ansprechpartner

| Feld | Typ | Anmerkung |
|---|---|---|
| Id | int PK | |
| KundeId | FK → Kunde | |
| Anrede | Enum | Keine / Herr / Frau — getrennte Felder (Odoo-Ärgernis) |
| Titel | nvarchar(50), null | „Dr." |
| Vorname | nvarchar(100), null | leer erlaubt (Sammelpostfächer) |
| Nachname | nvarchar(100) | |
| Position | nvarchar(100), null | „GF", „Einkauf" |
| Email | nvarchar(200), null | Grundlage der E-Mail-Zuordnung (Phase 2) |
| TelefonFestnetz / TelefonMobil | nvarchar(50), null | CTI-Klickziele |
| Rollen | int (Flags-Enum) | Vertrag, Technik/Tickets, Rechnungsempfang |
| Aktiv | bit | |
| OdooId | int, null | |

**Briefanrede wird nie gespeichert**, sondern immer berechnet
(`Ansprechpartner.Briefanrede()`): „Sehr geehrte Frau X" / „Sehr geehrter Herr Y" /
ohne Anrede „Sehr geehrte Damen und Herren".

### Adresse
`KundeId`, `Typ` (Enum: Hauptsitz / Rechnung / Sonstige), `Zusatz?`, `Strasse`,
`Plz`, `Ort`, `Land` (ISO-2, Default `DE`), `Aktiv`. Abweichende
Rechnungsanschrift = eigene Adresse mit Typ `Rechnung`.

## Verträge (`Rausoft.Erp.Domain.Contracts`)

### Vertrag

| Feld | Typ | Anmerkung |
|---|---|---|
| Id | int PK | |
| Vertragsnummer | nvarchar(20), unique | aus Nummernkreis |
| KundeId | FK → Kunde | |
| Status | Enum | Entwurf / Aktiv / Gekuendigt / Beendet |
| Beginn | date | |
| MindestlaufzeitMonate | int, null | |
| VerlaengerungMonate | int, null | automatische Verlängerung |
| KuendigungsfristMonate | int, null | |
| GekuendigtZum | date, null | |
| Notiz | nvarchar(max), null | |
| OdooId | int, null | |

Kündigungstermine werden aus Beginn/Laufzeit/Frist **berechnet**, nicht gespeichert.
Erinnerungen laufen ab Phase 2 über Aufgaben/Wiedervorlage. `AngebotId` (FK) kommt
in Phase 3 dazu (Kette Angebot → Vertrag).

### Vertragsbestandteil
Typisiert (explizite Anforderung: „Wer hat eine Sondervereinbarung?"):
`VertragId`, `Typ` (Enum: Lizenzvertrag / Avv / Nda / Sondervereinbarung /
Nachtrag / Sonstiges), `Titel`, `Beschreibung?`, `UnterzeichnetAm?`.

### Vertragsdokument
Unveränderliche Versionen (Leitplanke) — Dateien bleiben im bestehenden
Ablageverzeichnis, das System speichert Pfad + Prüfsumme:

| Feld | Typ | Anmerkung |
|---|---|---|
| Id | int PK | |
| VertragsbestandteilId | FK | |
| Version | int | unique je Bestandteil, aufsteigend |
| Dateipfad | nvarchar(500) | UNC/lokaler Pfad im Ablageverzeichnis |
| DateinameOriginal | nvarchar(260) | |
| Sha256 | char(64) | Prüfsumme |
| ErstelltAm | datetime2 | |
| ErstelltVonId | FK → Mitarbeiter | |

Zeilen werden **nie geändert oder gelöscht** — neue Version = neue Zeile
(im DbContext technisch erzwungen).

## DSGVO & Löschkonzept (Leitplanke, Umsetzung fortlaufend)

- Personenbezogen sind v. a. Ansprechpartner: statt Hard-Delete
  **Anonymisierung** (Name/Kontaktdaten überschreiben, Datensatz-ID bleibt für
  Timeline/Audit-Integrität erhalten).
- Audit-Log und Timeline referenzieren nur IDs; Inhalte mit Personenbezug
  (Gesprächsnotizen) werden beim Anonymisieren mitbehandelt.
- Keine Geheimnisse im Klartext (siehe CLAUDE.md): dieses Modell speichert
  bewusst keinerlei Credentials; kommende Konfigurationen (SMTP Phase 2)
  werden verschlüsselt abgelegt (ASP.NET Data Protection + DPAPI).
