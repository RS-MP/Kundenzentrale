# Kundenzentrale · Sicherheitsbewertung

Stand: 15.10.2026 · Grundlage: Code-Review des Repositories (Commit dc87785),
DEPLOYMENT.md, ANFORDERUNGEN.md, DATENMODELL.md und ein Paket-Schwachstellen-Scan
(`dotnet list package --vulnerable`). Kein Penetrationstest, keine Prüfung der
Server-Konfiguration vor Ort (IIS, NTFS, SQL Server, GPO).

Zweck: Input für eine Präsentation zum Thema Sicherheit. Teil 1 listet alle
umgesetzten Regeln mit Bewertung, Teil 2 das Verbesserungspotenzial, Teil 3 eine
folientaugliche Kurzfassung.

Bewertungsskala je Regel:
- **gut** — Regel ist vollständig und sauber umgesetzt, keine Lücke erkennbar.
- **solide** — Regel wirkt, hat aber eine kleine Einschränkung oder einen offenen Rand.
- **ausbaufähig** — Regel ist angelegt, greift aber nicht überall oder nicht tief genug.

---

## Einsatzkontext (für die Einordnung wichtig)

- Interne Web-Anwendung (Blazor WebAssembly + ASP.NET Core REST API, .NET 10 LTS),
  Betrieb als IIS-Unteranwendung auf einem Windows Server im Haus, nur aus dem
  Intranet erreichbar, aktuell über **http** (kein TLS).
- Anmeldung über **Windows-Authentifizierung gegen die lokale AD** (SSO per
  Kerberos/NTLM). Entra ID ist als zweiter Modus vorbereitet.
- Verarbeitet werden Kundenstammdaten, Ansprechpartner, Verträge inklusive
  Vertragsdokumenten (PDF/Word auf Dateifreigaben), vertrauliche
  Sondervereinbarungen, die interne Preisliste sowie Zugangsdaten zu Odoo,
  zum externen Service Portal und zum PDF-Signaturzertifikat.
- Datenbank: SQL Server, Zugriff per Windows-Auth des App-Pool-Kontos.

---

## Teil 1 · Umgesetzte Sicherheitsregeln und Bewertung

### A · Authentifizierung

| # | Regel | Umsetzung | Bewertung |
|---|---|---|---|
| A1 | Keine eigenen Passwörter, keine Tokens in der eigenen Datenbank | Identität kommt ausschließlich von der AD (Windows-Auth) oder von Entra ID (Token-Validierung). Die Mitarbeiter-Tabelle speichert nur Windows-Konto bzw. Entra-Objekt-ID (`Mitarbeiter.cs`). | **gut** — größte Angriffsfläche (Passwortdatenbank) existiert schlicht nicht. |
| A2 | Jeder Endpunkt ist standardmäßig geschützt („deny by default") | `FallbackPolicy = DefaultPolicy` in `Program.cs`; ohne ausdrückliches `[AllowAnonymous]` ist jede Route anmeldepflichtig. Einziges `[AllowAnonymous]` ist der Test-Healthcheck, der in Produktion 404 liefert. | **gut** — ein vergessenes `[Authorize]` kann keine Lücke öffnen. |
| A3 | Entwicklungs-Login ohne Anmeldung nur lokal | Pseudo-Benutzer nur bei Environment = Development **und** Flag `Entwicklung:AuthDeaktiviert`; beim Start wird eine Warnung geloggt. | **solide** — doppelte Absicherung ist richtig. Einschränkung: das Launch-Profil `http-prod-db` kombiniert genau diesen Pseudo-Admin mit der **Produktions-Datenbank** (siehe V7). |
| A4 | Unbekannte Domänenkonten werden ohne Rollen angelegt | Middleware legt beim ersten Zugriff einen Mitarbeiter mit `Rollen = Keine` an; Rollen vergibt nur ein Admin. Der konfigurierte Erst-Admin bekommt beim Start einmalig die Admin-Rolle. | **ausbaufähig** — Rollen sind sauber „leer", aber **jedes** Domänenkonto (auch Dienst- oder Testkonten) erhält damit Lesezugriff auf Kunden, Verträge und Preisliste (siehe V3). |
| A5 | Windows-Auth über IIS, Negotiate-Handler nur für Kestrel | `AddNegotiate()`; unter IIS übernimmt IIS die Authentifizierung, anonymer Zugriff ist an der Anwendung deaktiviert (DEPLOYMENT.md). | **gut** — Standardweg, wie beim Urlaubsplaner. |

### B · Autorisierung und Rollenmodell

| # | Regel | Umsetzung | Bewertung |
|---|---|---|---|
| B1 | Berechtigungen werden serverseitig durchgesetzt, nie nur im Frontend | Policies `Admin` und `Compliance` lesen die Rollen **aus der Datenbank**, nicht aus Claims (`AdminAnforderung.cs`). Vertraulich-, Wiki- und Preislisten-Rechte werden je Request in der API geprüft; das Frontend blendet nur aus. | **gut** — Rollen lassen sich clientseitig nicht fälschen. |
| B2 | Rollen als Flags, getrennte Verantwortungen | `MitarbeiterRolle`: Admin, Vertrieb, Support, Buchhaltung, Compliance, Vertrauliche Verträge, Wiki-Redaktion. | **gut** — feingranular genug für die Phase 1. |
| B3 | Funktionstrennung: Admin ≠ Einsicht in vertrauliche Verträge | Rolle „Vertrauliche Verträge" ist bewusst **nicht** in Admin enthalten; Admin-Löschung einer Akte gibt vertrauliche Pfade nicht preis. | **gut** — seltene, aber richtige Entscheidung (technische Administration heißt nicht Dateneinsicht). |
| B4 | Vertrauliche Bestandteile: ohne Rolle nur Metadaten | API liefert ohne Rolle nur Dokumentart, Datum, Kurzinfo; Titel, Beschreibung, Dokumente und **Dateipfade verlassen den Server nicht**. Verlaufseinträge nennen nur die Dokumentart. Dokumente liegen in einer getrennten Ablage; Registrierung erzwingt die richtige Ablage. | **gut** |
| B5 | Vorlagen: Verwaltung/Freigabe nur Compliance, Nutzung je Rolle | `[Authorize(Policy="Compliance")]` auf allen schreibenden Vorlagen-Endpunkten; `Vorlage.ErlaubtFuer()` prüft die Nutzungsfreigabe serverseitig (Unit-Test vorhanden). | **gut** |
| B6 | Wiki: Redaktion nur für freigegebene Themen | `WikiBerechtigung`: Admin/Compliance alles, Wiki-Redaktion nur die am Mitarbeiter hinterlegten Themen; unbekannte Themenschlüssel werden verworfen. | **gut** |
| B7 | Interne Preise nur für Vertrieb/Admin/Compliance | Sichtbarkeitsfilter in der API, interne Datei-Links werden entfernt; Unit-Test „OhneBerechtigung_EnthaeltNichtsInternes". | **gut** |
| B8 | Keine Selbst-Aussperrung, Deaktivieren statt Löschen | Admin kann sich weder deaktivieren noch die eigene Admin-Rolle entziehen; Konten mit Verlaufs-/Audit-Verweisen werden deaktiviert statt gelöscht. | **gut** |
| B9 | Deaktivierte Konten verlieren Rechte | `Aktiv` wird in den Admin-, Compliance-, Vertraulich-, Wiki- und Preislisten-Prüfungen beachtet. | **ausbaufähig** — die **Grundberechtigung** (alle lesenden Endpunkte) prüft `Aktiv` nicht: ein deaktivierter Mitarbeiter kann weiterhin Kunden, Verträge und die externe Preisliste lesen (siehe V3). |

### C · Geheimnisse und Verschlüsselung

| # | Regel | Umsetzung | Bewertung |
|---|---|---|---|
| C1 | Geheimnisse nie im Klartext persistieren | `GeheimnisDienst`: Portal-Push-Token, Odoo-API-Key und PFX-Passwort werden mit ASP.NET Core Data Protection verschlüsselt in der Tabelle `GeheimeEinstellungen` abgelegt. | **gut** |
| C2 | Schlüsselschutz per DPAPI, maschinengebunden | Schlüsselbund unter `ProgramData\Rausoft\Kundenzentrale\Schluessel`, `ProtectKeysWithDpapi(protectToLocalMachine: true)`; NTFS-ACL-Anleitung in DEPLOYMENT.md. Import-Tool nutzt denselben Schlüsselbund. | **solide** — korrekt umgesetzt; der Schlüsselbund ist bewusst nicht portabel. Offen: kein dokumentiertes Backup/Restore des Schlüsselbunds (siehe V10). |
| C3 | Geheimnisse sind write-only | API liefert nur „gesetzt am"; Werte werden nie zurückgegeben, UI nutzt `type="password"`. „Bearer "-Präfix wird beim Speichern entfernt. Nur bekannte Schlüssel sind erlaubt. | **gut** |
| C4 | Geheimnisse nie in Logs oder im Audit-Log | `GeheimeEinstellung` steht auf der Nicht-auditieren-Liste; Import-Tool gibt den Key nie aus („vorhanden, wird nie angezeigt"). | **gut** |
| C5 | Keine Secrets im Repository | `appsettings.json` enthält nur Platzhalter; Connection String nutzt Windows-Auth (kein Passwort); Telerik-Lizenz, Offline-Pakete, `*.pfx` und lokale Ablagen sind in `.gitignore` und waren nie im Git-Verlauf. | **gut** |
| C6 | Dokumentintegrität per Hash und Signatur | SHA-256 für jede Dokumentversion und jede Vorlagen-Version; Vorlagen-Dateitabelle erkennt veränderte eingefrorene Kopien; PDFs erhalten optional eine digitale Signatur (PFX). | **solide** — sehr gut angelegt; die Signatur ist produktiv noch nicht aktiv (Zertifikat fehlt, DEPLOYMENT.md). |
| C7 | Vertragsdokumente unveränderlich | `ErpDbContext` wirft bei Änderung/Löschung von `Vertragsdokument`; einzige Ausnahme ist die explizite Admin-Löschung einer Akte, bei der die Dateien liegen bleiben. | **gut** — Leitplanke ist technisch erzwungen, nicht nur Konvention. |

### D · Datei- und Dokumentzugriff

| # | Regel | Umsetzung | Bewertung |
|---|---|---|---|
| D1 | Kein freier Zugriff aufs Ablageverzeichnis | Download nur über registrierte Dokument-IDs; der Pfad kommt aus der Datenbank, nie aus dem Request. | **gut** |
| D2 | Schutz vor Path Traversal | Registrierung: `Path.GetFullPath` + Prüfung „liegt unter Basisverzeichnis"; Zielverzeichnis der Vertragserzeugung muss in der Ablage liegen; Vorlagen-Freigabe akzeptiert nur reine Dateinamen mit `.docx` und prüft ungültige Zeichen. | **gut** |
| D3 | Upload begrenzt und validiert | Preislisten-Import: `[RequestSizeLimit(5 MB)]`, Pflicht-Schema `rausoft.preisliste/`, Strukturprüfung, Version muss strikt steigen, Datei wird unverändert mit SHA-256 abgelegt. | **gut** |
| D4 | Auslieferung registrierter Dateien | Content-Type aus der Dateiendung, Anzeige inline (Vorschau). | **ausbaufähig** — ohne `X-Content-Type-Options: nosniff` und ohne Typ-Whitelist könnte eine im Archiv abgelegte HTML/SVG-Datei im Kontext der Anwendung ausgeführt werden (siehe V5). Risiko gering, da nur Mitarbeiter Dateien ablegen. |

### E · Eingaben, Injection, Frontend

| # | Regel | Umsetzung | Bewertung |
|---|---|---|---|
| E1 | Kein SQL-Injection-Risiko | Ausschließlich EF Core LINQ, kein `FromSql`/`ExecuteSqlRaw` im gesamten Code. | **gut** |
| E2 | Kein Over-Posting | Eingaben laufen über eigene DTO-Records (`Rausoft.Erp.Dtos`), Entitäten werden nie direkt gebunden oder zurückgegeben. | **gut** |
| E3 | Fachliche Validierung in der API | Pflichtfelder, Eindeutigkeit (Alias, E-Mail, Konto), URL-Schema nur http/https, Rollen- und Themen-Whitelists, Seitengrößen gedeckelt (max. 100). Längen über `HasMaxLength` in der DB. | **solide** — Validierung ist handgeschrieben und daher nicht einheitlich; Längenüberschreitungen enden in einem 500 statt 400. |
| E4 | Kein XSS im Frontend | Keine Verwendung von `MarkupString`/innerHTML; Blazor rendert kodiert. JSON ausschließlich über `System.Text.Json`. | **gut** |
| E5 | Optimistic Concurrency | `RowVersion` auf Kunde, Ansprechpartner, Vertrag u. a. | **gut** |
| E6 | CSRF-Schutz | Keine explizite Maßnahme. JSON-Endpunkte sind faktisch geschützt (kein CORS in Produktion, JSON erzwingt Preflight). | **ausbaufähig** — der Multipart-Upload (Preislisten-Import) ist ein „simple request" und mit **ambienter Windows-Anmeldung** über ein fremdes Formular auslösbar (siehe V4). |

### F · Audit und Nachvollziehbarkeit

| # | Regel | Umsetzung | Bewertung |
|---|---|---|---|
| F1 | Zentrales Audit-Log automatisch | `ErpDbContext.SaveChanges` schreibt für jede Änderung Entität, ID, Aktion, Mitarbeiter und Alt/Neu-Werte als JSON. Rollenänderungen, Einstellungen, Verträge, Vorlagen sind damit lückenlos erfasst. | **solide** — sehr guter Mechanismus; es fehlen Ansicht, Aufbewahrungsregel und Integritätsschutz (siehe V8). |
| F2 | Protokollcharakter-Entitäten ausgenommen | Audit-Log, Timeline, Nummernkreise, Geheimnisse, Server-Healthcheck-Takt werden nicht auditiert. | **gut** — bewusste, dokumentierte Ausnahmen. |
| F3 | Verlauf (Timeline) mit Datensparsamkeit | Löschungen, Registrierungen und Änderungen landen im Kundenverlauf; vertrauliche Titel/Pfade bleiben draußen. | **gut** |
| F4 | Sicherheitsereignisse im Log | Nur Start-Warnung bei Dev-Auth, Import-Fehler, Portal-Warnungen. | **ausbaufähig** — abgewiesene Zugriffe (403), Rollenwechsel oder Geheimnis-Änderungen erzeugen keinen eigenen Log-Eintrag (siehe V8). |

### G · Datensparsamkeit nach außen (Service Portal, Odoo)

| # | Regel | Umsetzung | Bewertung |
|---|---|---|---|
| G1 | Nur Alias + Status verlassen das Haus | `ServerStatusDienst` pusht ausschließlich Alias, Erreichbarkeit, Connector-Status, Sanktionslisten-Datum, Fehlertext. Interne URLs, Mandanten-Kennungen und Kundenbezüge bleiben intern. | **gut** — Sicherheitsmodell ist in der Schnittstellen-Anleitung festgehalten und im Code eingehalten. |
| G2 | Generische Fehlertexte | Exceptions werden auf feste Texte („Timeout nach 15s", „Verbindungsfehler") abgebildet, Antwortfelder werden gekürzt. | **gut** |
| G3 | Ausgehende Aufrufe nur mit Admin-gepflegten Zielen | Server-URLs und Portal-URL pflegen nur Admins; Timeouts 15 s/30 s; Bearer-Token aus dem verschlüsselten Speicher. | **gut** |
| G4 | Odoo-Zugang ohne Dateien | URL/DB/Benutzer in Einstellungen, API-Key verschlüsselt; Import-Tool liest denselben Schlüsselbund, Umgebungsvariablen nur als Override. | **gut** |

### H · Transport, Betrieb, Deployment

| # | Regel | Umsetzung | Bewertung |
|---|---|---|---|
| H1 | Nur intern erreichbar | IIS-Unteranwendung im Intranet, keine Veröffentlichung nach außen; alte Port-Site und Firewall-Regel entfernt. | **gut** |
| H2 | Transportverschlüsselung | **Kein HTTPS** im Windows-Modus (bewusst, wegen Zertifikatswarnung an der Default Web Site). `UseHttpsRedirection` nur im Entra-Modus. | **ausbaufähig** — Kundendaten, Vertrags-PDFs und NTLM-Handshakes laufen im Klartext durchs LAN (siehe V1). |
| H3 | Verschlüsselung zum SQL Server | `Encrypt=True`, aber `TrustServerCertificate=True`. | **ausbaufähig** — Verbindung ist verschlüsselt, der Server aber nicht authentifiziert (siehe V6). |
| H4 | Least Privilege für das App-Pool-Konto | Eigener App-Pool, SQL-Rechte `db_datareader/db_datawriter/db_ddladmin`, NTFS-Hinweise für Schlüsselbund und vertrauliche Ablage. | **solide** — `db_ddladmin` dauerhaft nur wegen Auto-Migration (siehe V9). |
| H5 | CORS, OpenAPI, Test-Endpunkte nur in Entwicklung | CORS nur für Loopback-Origins im Development; `MapOpenApi` und Test-Healthcheck nur im Development. | **gut** |
| H6 | HTTP-Security-Header | Keine gesetzt (CSP, nosniff, frame-ancestors, Referrer-Policy). | **ausbaufähig** (siehe V5) |
| H7 | Aktuelle Plattform und Pakete | .NET 10 LTS, Pakete auf 10.0.11; Microsoft.Identity.Web 4.14. | **ausbaufähig** — der Scan vom 15.10.2026 meldet bekannte Schwachstellen in 10.0.11-Paketen, Fix ist 10.0.13 (siehe V2). Es gibt keinen regelmäßigen Scan. |
| H8 | Backups | In DEPLOYMENT.md beschrieben (Voll + Log), Checkbox noch offen. | **ausbaufähig** (siehe V10) |
| H9 | Dokumentation des Sicherheitsmodells | CLAUDE.md (verbindliche Regeln), DEPLOYMENT.md (ACLs, Konten, Geheimnisse), Schnittstellen-Anleitung (Datensparsamkeit), Kommentare im Code nennen die Regel, die sie umsetzen. | **gut** — ungewöhnlich gut nachvollziehbar für ein Projekt dieser Größe. |

### I · Entwicklungsprozess

| # | Regel | Umsetzung | Bewertung |
|---|---|---|---|
| I1 | Tests für Berechtigungslogik | 28 Unit-Tests, davon zwei sicherheitsrelevant (Vorlagen-Nutzungsfreigabe, Preislisten-Sichtbarkeit). | **ausbaufähig** — keine Tests für Pfadprüfung, Vertraulich-Filter, Rollen-Policies (siehe V11). |
| I2 | Automatisierte Prüfungen (CI, Scans) | Keine CI-Pipeline, kein automatischer Schwachstellen-Scan, kein Secret-Scan. | **ausbaufähig** (siehe V2, V11) |

---

## Teil 2 · Verbesserungspotenzial (priorisiert)

Priorität = Risiko im konkreten Einsatzkontext (internes Netz, Mitarbeiter als
einzige Nutzer), nicht die abstrakte Schwere.

**Umsetzungsstand 15.10.2026 (nach dem Review):**
- V1 — Code-Teil erledigt: HTTPS-Umleitung + HSTS im Windows-Modus per
  Schalter `Hosting:HttpsErzwingen` (Standard `false`, damit der http-Testbetrieb
  weiterläuft). Offen: Zertifikat + https-Binding am Server, Anleitung in
  DEPLOYMENT.md „HTTPS im Intranet".
- V2 — erledigt: alle Pakete auf 10.0.13, `System.Security.Cryptography.Pkcs`
  explizit auf 10.0.13 gepinnt; Scan meldet für alle Projekte „keine anfälligen
  Pakete". Scan ist jetzt Pflichtschritt im Update-Ritual (DEPLOYMENT.md).
  Offen: Hosting Bundle / Desktop Runtime auf dem Server auf den aktuellen Patch.
- V3 — erledigt: Fallback- und Default-Policy verlangen einen **aktiven**
  Mitarbeiter-Datensatz (`AktiverMitarbeiterAnforderung`); Deaktivieren sperrt
  vollständig aus. Bewusst beibehalten (Testphase): neue Domänenkonten werden
  weiterhin automatisch **aktiv** ohne Rollen angelegt — vor dem Produktiv-Reset
  entscheiden, ob neue Konten inaktiv starten oder eine AD-Gruppe Voraussetzung wird.

### Hohe Priorität

**V1 · HTTPS im Intranet einführen.**
Alles läuft über http: Kundendaten, Vertrags-PDFs mit Sondervereinbarungen,
NTLM-Authentifizierung. Jeder im LAN mitlesende Rechner sieht die Daten; NTLM
über http ist zudem relay-fähig.
Maßnahme: internes Zertifikat aus der AD-Zertifizierungsstelle, https-Binding
an der Default Web Site, Intranetzone-GPO auf die https-URL erweitern,
anschließend `UseHttpsRedirection` und HSTS auch im Windows-Modus aktivieren.
Aufwand: klein (Serverseite), eine Zeile im Code.

**V2 · Pakete auf 10.0.13 heben und regelmäßigen Schwachstellen-Scan einführen.**
Der Scan vom 15.10.2026 meldet für die eingesetzten 10.0.11-Pakete:
- `Microsoft.AspNetCore.Authentication.Negotiate` — falsche LDAP-Zuordnung bei
  aktivierter LDAP-Claim-Auflösung (GHSA-vrrp-6v32-2p8g). Nicht genutzt, daher
  hier nicht ausnutzbar, aber im Produktpfad.
- `Microsoft.AspNetCore.Components.WebAssembly.Server` — zwei Lücken im Debug-
  Proxy/Debug-Endpunkt (nur Entwicklungsrechner betroffen).
- transitiv `System.Security.Cryptography.Pkcs` und `.Xml` — CPU-Erschöpfung bei
  Signaturprüfung (relevant für die PDF-Signatur-Pipeline).
Maßnahme: alle Microsoft-Pakete auf 10.0.13, ASP.NET Core Hosting Bundle und
Desktop Runtime auf dem Server nachziehen; `dotnet list package --vulnerable`
als festen Schritt vor jedem Publish in DEPLOYMENT.md aufnehmen.
Aufwand: klein.

**V3 · Zugang auf aktive, freigegebene Mitarbeiter beschränken.**
Zwei Lücken im selben Mechanismus: (a) die Grundberechtigung prüft nur „ist
angemeldet", nicht `Mitarbeiter.Aktiv` — Deaktivieren sperrt nur die
Rollenfunktionen, nicht das Lesen; (b) jedes Domänenkonto wird automatisch
angelegt und darf sofort alles lesen, was keine Rolle braucht (Kunden,
Ansprechpartner, Verträge, Server-Übersicht, externe Preisliste).
Maßnahme: eine Policy „AktiverMitarbeiter" als Fallback-Policy, die den
Datensatz lädt und `Aktiv` prüft; optional neue Konten mit `Aktiv = false`
anlegen oder den Zugang an eine AD-Gruppe binden. Dann wird Deaktivieren zum
echten Aussperren.
Aufwand: klein bis mittel (eine Policy, ein Handler, Caching je Request).

### Mittlere Priorität

**V4 · CSRF-Schutz für den Multipart-Upload.**
Windows-Auth wird vom Browser automatisch mitgeschickt. JSON-Endpunkte sind
durch den fehlenden CORS-Freischein geschützt, der Preislisten-Import
(`multipart/form-data`) nicht: eine fremde Intranet-Seite könnte ein Formular
im Namen des angemeldeten Redakteurs abschicken. Schaden begrenzt (nur neuere,
schema-gültige Preislisten-Version), aber ein Muster, das bei weiteren Uploads
wiederkehrt.
Maßnahme: globaler Filter, der bei schreibenden Requests einen eigenen Header
(z. B. `X-Requested-With`) verlangt, den das Blazor-Frontend immer setzt.
Aufwand: klein.

**V5 · Security-Header und sichere Dateiauslieferung.**
Maßnahme: Middleware setzt `X-Content-Type-Options: nosniff`,
`Content-Security-Policy` mit `frame-ancestors 'self'`, `Referrer-Policy`;
Dokument-Download nur PDF/DOCX inline, alle anderen Typen mit
`Content-Disposition: attachment`.
Aufwand: klein.

**V6 · SQL-Verbindung ohne `TrustServerCertificate`.**
`Encrypt=True` ist gesetzt, aber ohne Zertifikatsprüfung ist ein Man-in-the-
Middle zum SQL Server möglich.
Maßnahme: SQL Server ein AD-CS-Zertifikat geben, Flag entfernen (Connection
String in `appsettings.Production.json`, Umgebungsvariable des Import-Tasks).
Aufwand: klein.

**V7 · Launch-Profil `http-prod-db` entschärfen.**
Das Profil startet die API im Development-Modus mit Pseudo-Admin ohne
Anmeldung gegen die **Produktions-Datenbank**. Auf dem Entwicklungsrechner
lokal gebunden, aber ein Fehlgriff reicht.
Maßnahme: Profil entfernen oder Dev-Auth im Code zusätzlich an einen
LocalDB/localhost-Connection-String koppeln.
Aufwand: sehr klein.

**V8 · Audit-Log sichtbar und auswertbar machen, Sicherheitsereignisse loggen.**
Der Mechanismus ist vorbildlich, aber ohne Ansicht wird er nur per SQL
genutzt. Zusätzlich fehlen Einträge für abgewiesene Zugriffe (403),
Geheimnis-Änderungen (nur „gesetzt am") und Rollenwechsel als eigenes Ereignis.
Maßnahme: Admin-Seite „Audit-Log" mit Filter (Mitarbeiter, Entität,
Zeitraum), Logging von 403 mit Konto und Route, Aufbewahrungsregel festlegen
(Datenschutz-Löschkonzept aus DATENMODELL.md).
Aufwand: mittel.

**V9 · Datenbankrechte des App-Pools reduzieren.**
`db_ddladmin` dauerhaft, weil Migrationen beim Start laufen.
Maßnahme: akzeptieren und in DEPLOYMENT.md begründen (Einzelinstanz, Haus-
Betrieb) **oder** Migrationen als expliziten Publish-Schritt mit separatem
Konto ausführen.
Aufwand: klein; eher eine bewusste Entscheidung als Arbeit.

**V10 · Backups und Schlüsselbund-Sicherung tatsächlich einrichten.**
DB-Backup steht als offener Punkt in DEPLOYMENT.md. Der DPAPI-Schlüsselbund
ist maschinengebunden; nach Serververlust sind die Geheimnisse weg (bewusst so,
aber nur dann unkritisch, wenn alle drei Werte beschaffbar bleiben).
Maßnahme: Wartungsplan Voll + Log, Restore-Test einmal durchführen; für die
Geheimnisse dokumentieren, wo Token, Odoo-Key und PFX-Passwort im Notfall
neu bezogen werden.
Aufwand: klein (Betrieb).

### Niedrige Priorität

**V11 · Sicherheitstests und CI.** Tests für `LiegtUnter`/Pfadprüfung,
Vertraulich-Filter in `NachDetail`, Rollen-Policies (mit In-Memory-DB) sowie
ein Build + Test + Schwachstellen-Scan als GitHub-Action oder lokales Skript.

**V12 · Einheitliche Eingabevalidierung.** DataAnnotations oder FluentValidation
für Längen und Formate, damit Überlängen als 400 statt 500 enden.

**V13 · Rate-Limits und globale Body-Limits.** Im Intranet nicht dringend;
sinnvoll vor einem späteren Entra-/Außenzugang (`AddRateLimiter`,
`MaxRequestBodySize`).

**V14 · Admin-Rechte sind weitreichend.** Ein Admin kann jedes Verzeichnis als
Ablage setzen und darüber Dateien lesen, die das App-Pool-Konto lesen darf.
Vertretbar (Admins sind vertrauenswürdig), aber Grund, das App-Pool-Konto
strikt auf die benötigten Freigaben zu beschränken (NTFS, nicht Code).

**V15 · `AllowedHosts: "*"`.** Auf die tatsächlichen Hostnamen einschränken,
sobald HTTPS und DNS-Name stehen (Host-Header-Injection, geringes Risiko).

---

## Teil 3 · Kurzfassung für Folien

**Gesamteinschätzung:** Für eine interne Fachanwendung in Phase 1 ist das
Sicherheitsniveau **überdurchschnittlich**. Die schwer nachrüstbaren Dinge
(keine eigene Passwortdatenbank, deny-by-default, serverseitige Rollen,
verschlüsselte Geheimnisse, unveränderliche Dokumente, Audit-Log, Datensparsamkeit
nach außen) sind von Anfang an richtig gebaut. Die offenen Punkte sind
überwiegend **Betriebs- und Härtungsthemen** (TLS, Paket-Updates, Header), die
sich mit geringem Aufwand schließen lassen. Ein echter Logikfehler ist nur V3
(deaktivierte Konten lesen weiter).

**Fünf Stärken**
1. Keine Passwörter, keine Tokens im eigenen System — Identität kommt aus der AD.
2. Jeder Endpunkt geschützt, Rollen werden aus der Datenbank geprüft, nie aus dem Frontend.
3. Funktionstrennung: Admin sieht keine vertraulichen Verträge; Compliance gibt Vorlagen frei.
4. Geheimnisse DPAPI-verschlüsselt, write-only, nie in Logs oder Audit.
5. Dokumente unveränderlich mit SHA-256, optional signiert; alles landet im Audit-Log.

**Fünf Maßnahmen als Nächstes**
1. HTTPS mit internem Zertifikat (V1).
2. Pakete auf 10.0.13, Scan vor jedem Publish (V2).
3. Zugang nur für aktive Mitarbeiter, Deaktivieren = Aussperren (V3).
4. CSRF-Header und Security-Header (V4, V5).
5. SQL-Zertifikat, Backups, Audit-Ansicht (V6, V8, V10).

**Zahlen für eine Folie**
| Kennzahl | Wert |
|---|---|
| Geprüfte Regeln | 50 |
| davon „gut" | 32 |
| davon „solide" | 6 |
| davon „ausbaufähig" | 12 |
| Verbesserungspunkte | 15 (3 hoch, 7 mittel, 5 niedrig) |
| Bekannte Paket-Schwachstellen (Stand 15.10.2026) | 8 Advisories in 4 Paketen, alle mit Fix in 10.0.13 |

---

## Anhang · Prüfumfang

Geprüft: `Program.cs`, alle Controller und Dienste der API, `ErpDbContext`,
Domain-Entitäten (Mitarbeiter, Geheimnisse, Verträge, Vorlagen, Wiki),
Import-Tool (`OdooZugang`, `OdooClient`), Blazor-Frontend (`index.html`,
`Program.cs`, Razor-Seiten auf Roh-HTML), `appsettings*`, `launchSettings`,
`web.config`, `.gitignore`, Git-Verlauf auf Lizenz-/Schlüsseldateien,
NuGet-Schwachstellenscan über alle Projekte.

Nicht geprüft: tatsächliche IIS-, NTFS- und SQL-Server-Konfiguration auf dem
Server, GPOs, Netzsegmentierung, Telerik-Bibliotheken selbst, das externe
Service Portal.
