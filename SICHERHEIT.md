# Kundenzentrale · Sicherheits- und Qualitätsbewertung

Stand: 29.10.2026 · Grundlage: Code-Review des Repositories (Commit 66b3d27),
DEPLOYMENT.md, ANFORDERUNGEN.md, DATENMODELL.md, Build + Testlauf und ein
Paket-Schwachstellen-Scan (`dotnet list package --vulnerable`). Kein
Penetrationstest, keine Prüfung der Server-Konfiguration vor Ort (IIS, NTFS,
SQL Server, GPO).

Vorgänger: Bewertung vom 15.10.2026 (Commit dc87785). Seitdem 33 Commits mit
rund 30.000 neuen Zeilen: Vorgänge mit Teilaufgaben, „Meine Aufgaben",
Zertifikatsüberwachung mit Mailversand, Serverstatus, Odoo-Chatter-Import in
die Kunden-Timeline, Odoo-Abgleich der Vorgänge, Wiki-Formatierung, HTTPS.
Diese Bewertung prüft den Gesamtstand neu, mit Schwerpunkt auf den neuen
Bereichen.

Zweck: Teil 1 listet alle umgesetzten Sicherheitsregeln mit Bewertung, Teil 2
das Verbesserungspotenzial (Sicherheit), Teil 3 eine folientaugliche
Kurzfassung, Teil 4 Qualität und Performance (neu).

Bewertungsskala je Regel:
- **gut** — Regel ist vollständig und sauber umgesetzt, keine Lücke erkennbar.
- **solide** — Regel wirkt, hat aber eine kleine Einschränkung oder einen offenen Rand.
- **ausbaufähig** — Regel ist angelegt, greift aber nicht überall oder nicht tief genug.

---

## Einsatzkontext (für die Einordnung wichtig)

- Interne Web-Anwendung (Blazor WebAssembly + ASP.NET Core REST API, .NET 10 LTS),
  Betrieb als IIS-Unteranwendung auf einem Windows Server im Haus, nur aus dem
  Intranet erreichbar, seit 29.10.2026 über **HTTPS auf Port 8443**
  (selbstsigniertes Zertifikat per GPO verteilt, http leitet um).
- Anmeldung über **Windows-Authentifizierung gegen die lokale AD** (SSO per
  Kerberos/NTLM). Entra ID ist als zweiter Modus vorbereitet.
- Verarbeitet werden Kundenstammdaten, Ansprechpartner, Verträge inklusive
  Vertragsdokumenten (PDF/Word auf Dateifreigaben), vertrauliche
  Sondervereinbarungen, die interne Preisliste, der importierte Odoo-Chatter
  (E-Mail-Verkehr mit Kunden samt Anhängen), Vorgänge und Aufgaben sowie
  Zugangsdaten zu Odoo, zum externen Service Portal, zum SMTP-Server und zum
  PDF-Signaturzertifikat.
- Neu: die Anwendung öffnet selbst Verbindungen nach außen (TLS-Handshakes zu
  Kundenservern für die Zertifikatsüberwachung, Odoo-API, SMTP) und
  verschickt E-Mails.
- Datenbank: SQL Server, Zugriff per Windows-Auth des App-Pool-Kontos.
- Derzeit **offener Testbetrieb** für alle Mitarbeiter; Produktiv-Reset zum
  Zeitpunkt X (DEPLOYMENT.md).

---

## Teil 1 · Umgesetzte Sicherheitsregeln und Bewertung

### A · Authentifizierung

| # | Regel | Umsetzung | Bewertung |
|---|---|---|---|
| A1 | Keine eigenen Passwörter, keine Tokens in der eigenen Datenbank | Identität kommt ausschließlich von der AD (Windows-Auth) oder von Entra ID. Die Mitarbeiter-Tabelle speichert nur Windows-Konto bzw. Entra-Objekt-ID. | **gut** |
| A2 | Jeder Endpunkt ist standardmäßig geschützt („deny by default") | `FallbackPolicy` und `DefaultPolicy` = angemeldet **und** aktiver Mitarbeiter-Datensatz (`Program.cs`). Einziges `[AllowAnonymous]` im Code ist der Test-Healthcheck, der außerhalb von Development 404 liefert. Alle 131 Endpunkte der 17 Controller wurden einzeln abgeglichen (Anhang). | **gut** |
| A3 | Entwicklungs-Login ohne Anmeldung nur lokal | Pseudo-Benutzer nur bei Environment = Development **und** Flag `Entwicklung:AuthDeaktiviert`; Start-Warnung im Log. Das Launch-Profil gegen die Produktions-DB ist seit 29.10. entfernt (V7). | **gut** |
| A4 | Unbekannte Domänenkonten werden ohne Rollen angelegt | Middleware legt beim ersten Zugriff einen aktiven Mitarbeiter mit `Rollen = Keine` an; Rollen vergibt nur ein Admin. | **ausbaufähig** — bewusst so bis zum Produktiv-Reset (Testbetrieb für alle). Vorher entscheiden: neue Konten inaktiv anlegen oder AD-Gruppe als Voraussetzung (V3b). |
| A5 | Windows-Auth über IIS, Negotiate-Handler nur für Kestrel | `AddNegotiate()`; unter IIS übernimmt IIS die Authentifizierung. | **gut** |

### B · Autorisierung und Rollenmodell

| # | Regel | Umsetzung | Bewertung |
|---|---|---|---|
| B1 | Berechtigungen werden serverseitig durchgesetzt, nie nur im Frontend | Policies `Admin`, `Compliance`, `Betrieb` lesen Rollen **aus der Datenbank** und prüfen `Aktiv`; Vertraulich-, Wiki-, Preislisten- und Vorgangstypen-Rechte werden je Request in der API geprüft. Stichproben im Frontend zeigen keine Stelle, an der das Frontend mehr erlaubt als der Server. | **gut** |
| B2 | Rollen als Flags, getrennte Verantwortungen | `MitarbeiterRolle`: Admin, Vertrieb, Support, Buchhaltung, Compliance, Vertrauliche Verträge, Wiki-Redaktion; dazu Bereichsleiter je Bereich (für Vorgangstypen). | **gut** |
| B3 | Funktionstrennung: Admin ≠ Einsicht in vertrauliche Verträge | Rolle „Vertrauliche Verträge" ist bewusst **nicht** in Admin enthalten. | **gut** |
| B4 | Vertrauliche Bestandteile: ohne Rolle nur Metadaten | Unverändert: Titel, Beschreibung, Dokumente und Dateipfade vertraulicher Bestandteile verlassen den Server nicht. Nicht-vertrauliche Dokumente liefern den UNC-Pfad an alle Mitarbeiter (gewollt: „Ordner öffnen"). | **gut** |
| B5 | Vorlagen: Verwaltung/Freigabe nur Compliance | `[Authorize(Policy="Compliance")]` auf allen schreibenden Vorlagen-Endpunkten. Die Policy ist als **Compliance oder Admin** implementiert (`AdminAnforderung.cs`), ebenso `Betrieb` = Support oder Admin. | **solide** — Admin ist damit bei Compliance-Themen stimmberechtigt; bei vertraulichen Verträgen wurde die Trennung bewusst gezogen, hier nicht. Entscheidung dokumentieren (E1). |
| B6 | Wiki: Redaktion nur für freigegebene Themen | `WikiBerechtigung`: Admin/Compliance alles, Wiki-Redaktion nur hinterlegte Themen. | **gut** |
| B7 | Interne Preise nur für Vertrieb/Admin/Compliance | Sichtbarkeitsfilter in der API, Unit-Test vorhanden. | **gut** |
| B8 | Keine Selbst-Aussperrung, Deaktivieren statt Löschen | Unverändert. | **gut** |
| B9 | Deaktivierte Konten verlieren alle Rechte | `AktiverMitarbeiterAnforderung` als Fallback-Policy lädt den Datensatz und prüft `Aktiv`; die Rollen-Handler prüfen zusätzlich selbst. | **gut** — V3 vom 15.10. umgesetzt. |
| B10 | Vorgänge und Aufgaben: Teamfunktionen ohne Rollenbindung | Jeder aktive Mitarbeiter darf Vorgänge anlegen, jeden Schritt übernehmen (auch einen bereits fremd zugewiesenen), zuweisen, erledigen, wieder öffnen, entfernen; „Meine Aufgaben" zeigt auf Wunsch jeden Bereich (Vertretung). So in ANFORDERUNGEN.md beschrieben, Frontend und Server konsistent offen. Jede Änderung landet im Audit-Log und im Vorgangsprotokoll. | **solide** — fachlich gewollt für ein kleines Team; nachvollziehbar, nicht eingeschränkt. Mindestschutz überlegen: „Entfernen" und „Wieder öffnen" auf Zuständigen oder Bereichsleiter begrenzen (E2). |
| B11 | Vorgangstypen (Vorlagen-Editor) nur Admin oder Bereichsleiter | Alle fünf schreibenden Endpunkte prüfen `DarfTypenPflegenAsync`; Bereichsleiter trägt nur ein Admin ein; laufende Nummer wird serverseitig vergeben. | **gut** |
| B12 | Zertifikate pflegen nur Support/Admin (Policy „Betrieb") | Alle schreibenden Endpunkte inklusive `pruefen` tragen `Betrieb`; Testmail nur Admin. (Lücke bei `pruefen` am 29.10. geschlossen, N1.) | **gut** |
| B13 | Mitarbeiter-, Server-, Einstellungs- und Importpflege nur Admin | Klassen-Policies; Rollen per Namens-Whitelist; `WindowsKonto` nicht änderbar; Odoo-Import und Chatter-Import nur Admin. | **gut** |
| B14 | Kein Over-Posting | Alle Eingaben über positionale DTO-Records ohne `Id`/`Rollen`/`Aktiv`, außer bei Admin-Endpunkten. | **gut** |

### C · Geheimnisse und Verschlüsselung

| # | Regel | Umsetzung | Bewertung |
|---|---|---|---|
| C1 | Geheimnisse nie im Klartext persistieren | `GeheimnisDienst`: Portal-Token, Odoo-API-Key, PFX-Passwort und neu das **SMTP-Passwort** liegen Data-Protection-verschlüsselt in `GeheimeEinstellungen`. Whitelist der vier Schlüssel in der API. | **gut** |
| C2 | Schlüsselschutz per DPAPI, maschinengebunden | Unverändert; Import-Tool nutzt denselben Schlüsselbund. | **solide** — Backup/Restore des Schlüsselbunds weiterhin nicht dokumentiert (V10). |
| C3 | Geheimnisse sind write-only | API liefert nur „gesetzt am"; Frontend nutzt `type="password"` und leert das Feld nach dem Speichern. SMTP-Host, Port, Benutzer, Absender liegen als normale Einstellungen in der DB (keine Geheimnisse). | **gut** |
| C4 | Geheimnisse nie in Logs, Audit oder Fehlermeldungen | Alle Logger-Aufrufe in Mail-, Zertifikat-, Serverstatus- und Odoo-Diensten geprüft: nur Betreff, Anzahl, Hostname, Bezeichnung. Token nur im Authorization-Header, Odoo-Key nur im JSON-Body; `OdooClient` loggt höchstens die Key-Länge. `GeheimeEinstellung` bleibt vom Audit ausgenommen. | **gut** |
| C5 | Keine Secrets im Repository | `appsettings*.json` ohne neue Geheimnis-Schlüssel; `*.pfx`, Telerik-Lizenz, `publish/`, lokale Ablagen in `.gitignore`; Git-Verlauf ohne Treffer auf Passwörter oder Zertifikatsdateien. | **gut** |
| C6 | Dokumentintegrität per Hash und Signatur | Unverändert (SHA-256, optionale PFX-Signatur, produktiv noch nicht aktiv). | **solide** |
| C7 | Vertragsdokumente unveränderlich | Unverändert, technisch im `DbContext` erzwungen. | **gut** |
| C8 | Keine Zertifikatsprüfung abgeschaltet | Kein `ServerCertificateCustomValidationCallback` oder `DangerousAcceptAnyServerCertificateValidator` im Code; SMTP validiert normal. Der TLS-Check der Zertifikatsüberwachung akzeptiert bewusst jedes Zertifikat, weil er nur liest und nichts sendet. | **gut** |

### D · Datei- und Dokumentzugriff

| # | Regel | Umsetzung | Bewertung |
|---|---|---|---|
| D1 | Kein freier Zugriff aufs Ablageverzeichnis | Downloads (Vertragsdokumente, Chatter-Anhänge) nur über IDs; Pfad kommt aus der Datenbank. | **gut** |
| D2 | Schutz vor Path Traversal | Registrierung und Zielverzeichnis mit `GetFullPath` + „liegt unter Basis"; Vorlagen-Freigabe nur reiner `.docx`-Dateiname. **Neu und sauber:** Chatter-Anhang-Download prüft die Basis; der Chatter-Import bereinigt Kundennummer und Odoo-Dateinamen (`SichererName`, 150 Zeichen, OdooId-Präfix). **Lücke:** die Vertragserzeugung baut den Dateinamen aus `Kundennummer` ohne Bereinigung; der Wert kommt nur aus Odoo (`ref`), ist über die API nicht setzbar. | **solide** (N5) |
| D3 | Upload begrenzt und validiert | Einziger Upload bleibt der Preislisten-Import (5 MB, Schema, Version strikt steigend). Chatter-Anhänge: 50 MB je Anhang im Import. | **gut** |
| D4 | Auslieferung registrierter Dateien | Vertragsdokumente seit 29.10. nur als PDF inline, alle anderen Typen als Download; Chatter-Anhänge (von externen Absendern!) waren schon Download. Dazu `nosniff` auf allen Antworten. | **gut** |
| D5 | Keine temporären Dateien in der API | Nur `MemoryStream`; das Import-Tool schreibt Berichte an einen vom Admin gewählten Pfad (CLI). | **gut** |

### E · Eingaben, Injection, Frontend

| # | Regel | Umsetzung | Bewertung |
|---|---|---|---|
| E1 | Kein SQL-Injection-Risiko | Ausschließlich EF Core LINQ, kein `FromSql`/`ExecuteSqlRaw`. | **gut** |
| E2 | Kein Over-Posting | Siehe B14. | **gut** |
| E3 | Fachliche Validierung in der API | Unverändert handgeschrieben; Längenüberschreitungen enden noch in 500 statt 400 (V12). Datenbankkonflikte (RowVersion, Unique-Index, Fremdschlüssel) liefern seit 29.10. 409 mit lesbarem Text (`DatenbankFehlerHandler`, Q5). | **solide** |
| E4 | Kein XSS im Frontend | Einzige `MarkupString`-Stelle ist der neue Wiki-Formatierer `WikiFormat.ZuHtml`: jeder Text wird zuerst HTML-kodiert, Links nur `http(s)://` aus bereits kodiertem Text, `rel="noopener noreferrer"`. Unit-Tests decken `<script>`, Attribut-Ausbruch und `javascript:` ab. | **gut** |
| E5 | Optimistic Concurrency | `RowVersion` auf Kunde, Ansprechpartner, Vertrag, seit 29.10. auch Vorgang. Konflikte enden als 409 (Q5). | **gut** |
| E6 | CSRF-Schutz | Seit 29.10.: jeder schreibende API-Aufruf braucht die Kopfzeile `X-Requested-With: Kundenzentrale` (Middleware `SchutzKopfzeilen`); ein HTML-Formular kann sie nicht setzen, fremde Skripte scheitern am Preflight. Das Frontend setzt sie zentral am HttpClient. | **gut** |
| E7 | Mail-Header-Injection | `MailAddress` verwirft ungültige Adressen, `MailMessage.Subject` verwirft CR/LF, Body ist Plaintext. | **gut** |

### F · Audit und Nachvollziehbarkeit

| # | Regel | Umsetzung | Bewertung |
|---|---|---|---|
| F1 | Zentrales Audit-Log automatisch | Unverändert: jede Änderung mit Entität, ID, Aktion, Mitarbeiter, Alt/Neu als JSON. Hintergrundläufe schreiben mit `MitarbeiterId = null`. | **solide** — weiterhin ohne Ansicht und Aufbewahrungsregel (V8). |
| F2 | Protokollcharakter-Entitäten ausgenommen | Audit-Log, Timeline, Nummernkreise, Geheimnisse, Server-Takt, Zertifikatsereignisse sind ausgenommen; seit 29.10. zusätzlich die Takt-Zeitstempel in `Einstellung`, die Live-/Erinnerungsfelder von `Zertifikat` und der Preislisten-JSON (Typ- bzw. Eigenschaftsausschluss in `ErpDbContext`). Ändert ein Lauf nur solche Felder, entsteht kein Eintrag. | **gut** |
| F3 | Verlauf (Timeline) mit Datensparsamkeit | Vertrauliche Titel/Pfade bleiben draußen. Verlaufseinträge können im Namen eines anderen Mitarbeiters erfasst werden (fachlich gewollt); der tatsächliche Erfasser steht nur im Audit-Log. | **solide** (E3 unten) |
| F4 | Sicherheitsereignisse im Log | Unverändert: keine eigenen Einträge für 403, Rollenwechsel, Geheimnis-Änderungen. | **ausbaufähig** (V8) |

### G · Außenzugriffe und Datensparsamkeit (Service Portal, Odoo, Zertifikate, Mail)

| # | Regel | Umsetzung | Bewertung |
|---|---|---|---|
| G1 | Nur Alias + Status verlassen das Haus (Service Portal) | Unverändert eingehalten. | **gut** |
| G2 | Generische Fehlertexte nach außen und in Antworten | `ServerStatusDienst` und seit 29.10. auch die Zertifikats-Live-Prüfung (`ZertifikatPruefregeln.FehlerText`: feste Kategorien, Original nur im Log) bilden Exceptions auf feste Texte ab. **Nicht** so beim Odoo-Abgleich (`ex.Message` bis 200 Zeichen als Hinweis an jeden Auslöser). | **solide** (N5) |
| G3 | Ausgehende Aufrufe nur mit Admin-gepflegten Zielen | Server-URLs und Portal-URL pflegt nur Admin, Schema nur http/https. Zertifikats-Hostnamen pflegt Support, seit 29.10. nur als DNS-Name oder IP ohne Loopback/Link-Local/Multicast und nur auf Admin-gepflegte Ports (`Zertifikate.ErlaubtePorts`, Standard 443/8443); die Regeln greifen auch für Bestandsdaten beim täglichen Takt. `Odoo.Url` und `ServicePortal.Url` nimmt der Einstellungs-Endpunkt ohne Schema-Prüfung an. Timeouts überall gesetzt (10–30 s). | **solide** (N5) |
| G4 | Odoo-Zugang ohne Dateien, nur lesend | Key aus `GeheimnisDienst`; API-Zugriffe sind `search_read`/`read`. Live-Abfrage von Kundenaufträgen und der Abgleich stehen jedem Mitarbeiter offen (gewollt: „per Knopf"). | **gut** |
| G5 | Zertifikats-Live-Prüfung als Netz-Orakel | Seit 29.10. begrenzt: Ziel nur auf erlaubten Ports, Hostname-Regeln, feste Fehlertexte ohne IP/Port-Details, Auslösen nur Support/Admin. Es bleibt erkennbar, ob auf einem erlaubten Port ein TLS-Dienst antwortet — das ist der Zweck der Funktion. | **gut** |
| G6 | Monitoring-Mails | Empfänger je Zertifikat müssen gültige Adressen in erlaubten Domänen sein (`Zertifikate.EmpfaengerDomaenen`, Standard: Domäne der Absenderadresse); beim Versand werden Adressen außerhalb übersprungen, auch aus den Admin-Einstellungen. Inhalt nennt Hostname, Port, Aussteller, Fehlertext, Notiz. | **gut** |

### H · Transport, Betrieb, Deployment

| # | Regel | Umsetzung | Bewertung |
|---|---|---|---|
| H1 | Nur intern erreichbar | IIS-Unteranwendung im Intranet. | **gut** |
| H2 | Transportverschlüsselung | **Seit 29.10.2026 HTTPS auf Port 8443**, `Hosting:HttpsErzwingen = true` auf dem Server, http leitet um. HSTS bewusst aus (Port 443 gehört einer anderen Site, Urlaubsplaner läuft http). Selbstsigniertes Zertifikat per Computer-GPO verteilt (keine AD-CA vorhanden). | **gut** — V1 umgesetzt. Rest: Zertifikat läuft 27.10.2029 ab, Erneuerung in den Kalender. |
| H3 | Verschlüsselung zum SQL Server | `Encrypt=True`, aber `TrustServerCertificate=True`. | **ausbaufähig** (V6) |
| H4 | Least Privilege für das App-Pool-Konto | Unverändert; `db_ddladmin` wegen Auto-Migration. | **solide** (V9) |
| H5 | CORS, OpenAPI, Test-Endpunkte nur in Entwicklung | Unverändert. | **gut** |
| H6 | HTTP-Security-Header | Seit 29.10.: `X-Content-Type-Options: nosniff`, `X-Frame-Options: SAMEORIGIN`, CSP `frame-ancestors 'self'`, `Referrer-Policy: strict-origin-when-cross-origin` auf jeder Antwort. Keine vollständige CSP (Blazor WASM bräuchte `unsafe-eval`). | **gut** |
| H7 | Aktuelle Plattform und Pakete | Alle Microsoft-Pakete 10.0.13, Scan am 29.10.2026: **keine anfälligen Pakete** in allen 7 Projekten. Verfügbar: 10.0.14, Microsoft.Identity.Web 4.16.1, Telerik 2026.3.1027. Scan ist Pflichtschritt im Update-Ritual. | **gut** — V2 umgesetzt. |
| H8 | Backups | Weiterhin nur beschrieben, nicht als erledigt vermerkt. | **ausbaufähig** (V10) |
| H9 | Dokumentation des Sicherheitsmodells | Unverändert gut; DEPLOYMENT.md ist allerdings zu einem Änderungsprotokoll mit über 20 offenen „Publish aktualisieren"-Kästchen gewachsen (Q-Hinweis). | **gut** |
| H10 | Hintergrunddienste robust | Serverstatus, Zertifikate, Vorgangs-Abgleich: eigener Scope je Lauf, `Exception` wird gefangen und geloggt, Abbruch beim Herunterfahren sauber. Importe laufen per `Task.Run` mit Einzel-Lauf-Sperre, aber ohne Abbruch-Token (bei IIS-Recycle harter Abbruch mitten im Batch, Teilstände konsistent). | **solide** (Q8) |

### I · Entwicklungsprozess

| # | Regel | Umsetzung | Bewertung |
|---|---|---|---|
| I1 | Tests für Berechtigungs- und Sicherheitslogik | 145 Unit-Tests, alle grün; sicherheitsrelevant: Wiki-XSS (3), Vorlagen-Nutzungsfreigabe, Preislisten-Sichtbarkeit. Keine Tests für Rollen-Policies, Pfadprüfungen, Vertraulich-Filter, Audit-Unveränderlichkeit. | **solide** (V11) |
| I2 | Automatisierte Prüfungen (CI, Scans) | Keine CI; Scan manuell im Update-Ritual. Build ohne Warnungen. | **ausbaufähig** (V11) |

---

## Teil 2 · Verbesserungspotenzial Sicherheit (priorisiert)

Priorität = Risiko im konkreten Einsatzkontext (internes Netz, Mitarbeiter als
einzige Nutzer), nicht die abstrakte Schwere.

**Umsetzungsstand der Punkte vom 15.10.2026:**
- V1 HTTPS — **erledigt** (29.10.2026, Port 8443, http-Umleitung aktiv).
- V2 Pakete/Scan — **erledigt** (10.0.13, Scan sauber, Pflichtschritt).
- V3 Aktive Mitarbeiter — **erledigt** (Fallback-Policy). Offen bleibt V3b:
  Auto-Anlage neuer Domänenkonten als aktiv, Entscheidung vor dem Produktiv-Reset.
- V4 bis V15 — **offen**, Beschreibung und Maßnahme unverändert (siehe unten,
  gekürzt).

### Neue Punkte (29.10.2026)

**N1 · Zertifikatsüberwachung härten.** (hoch, Aufwand klein) — **erledigt 29.10.2026**
(`ZertifikatPruefregeln` im Domain-Projekt mit 9 Unit-Tests, Migration
`ZertifikatPruefregeln` mit zwei Einstellungen, Policy auf `pruefen`; Regeln
gelten beim Speichern und beim täglichen Takt auch für Bestandsdaten.)
Vier Teile im selben Modul:
(a) `POST api/zertifikate/{id}/pruefen` bekommt `[Authorize(Policy="Betrieb")]`
wie alle anderen schreibenden Zertifikat-Endpunkte (`ZertifikateController.cs:125`).
(b) Hostname beim Speichern prüfen: DNS-Name oder IP, kein Loopback, keine
Link-Local-/Metadaten-Adressen, optional Port-Whitelist (443, 8443, konfigurierbar).
(c) `LiveFehler` wie in G2 auf feste Texte abbilden („Verbindung abgelehnt",
„Zeitüberschreitung", „Kein Zertifikat", „Kette ungültig"); den rohen Text nur
ins Server-Log.
(d) Empfänger-Adressen gegen eine erlaubte Domänenliste prüfen (Einstellung,
Standard: eigene Domäne), damit Statusmails über interne Hosts nicht an
Fremdadressen gehen.
Risiko heute: gering (nur Erreichbarkeits-Orakel im eigenen Netz, keine
Daten), aber Verstoß gegen die eigene Regel „Außenziele nur Admin-gepflegt,
Fehlertexte generisch".

**Umsetzungsstand 29.10.2026 (Paket 1 und 2, Commits nach 4464df7):**
N2, N3, N4, N5, V4, V5, V7 sowie Q1, Q3, Q5 sind umgesetzt — Details bei den
Punkten. Offen bleiben V3b, V6, V8–V15, Q2, Q4, Q6–Q14 und die Entscheidungen E1–E4.

**N2 · Vorgangs-Abgleich gegen Parallelläufe sichern.** (mittel, Aufwand klein) — **erledigt**
(`SemaphoreSlim` im Dienst, zweiter Start meldet „läuft bereits"; `RowVersion` auf `Vorgang`, Migration `VorgangRowVersionUndVerweisIndex`)
Der Abgleich läuft stündlich, per Knopf (jeder Mitarbeiter) und nach jedem
Chatter-Import, ohne gegenseitigen Ausschluss; `Vorgang`/`Aufgabe` haben keine
`RowVersion`. Zwei Läufe können denselben Schritt doppelt erledigen und zwei
Verlaufseinträge „Automatisch erledigt" schreiben. Maßnahme: `SemaphoreSlim(1,1)`
im Dienst (Muster aus `OdooImportDienst`) und `RowVersion` auf `Vorgang`.

**N3 · Mehrstufige Schreibvorgänge absichern.** (mittel, Aufwand mittel) — **erledigt**
(Vertrag anlegen und aus Vorlage in einer Transaktion, Dateien erst nach DB-Annahme und bei Fehler gelöscht;
Vorlagen-Freigabe erst DB dann Kopie; Unique-Kollisionen über Q5 als 409)
Vertrag anlegen (Nummer, Vertrag, Verlauf = drei Commits), Vertrag aus Vorlage
(PDF+DOCX auf Platte **vor** dem DB-Commit → verwaiste Dateien mit verbrauchter
Nummer bei Fehler), Vorlagen-Freigabe (`File.Copy` vor SaveChanges;
`Nummer = Max+1` im Speicher kollidiert bei gleichzeitiger Freigabe am
Unique-Index → 500), Dokument registrieren (`Version = Max+1`, gleiches Muster).
Maßnahme: Transaktion um die DB-Teile (Muster in `VorgaengeController.Anlegen`),
Dateien erst nach Commit schreiben oder bei Fehler löschen, `DbUpdateException`
→ 409.

**N4 · Audit-Log vom Rauschen befreien.** (mittel, Aufwand klein) — **erledigt**
(`ErpDbContext`: Takt-Einstellungen, `Zertifikat.Live*`/Erinnerungsfelder, `PreislistenVersion.Daten` und `ZertifikatEreignis` ausgenommen;
Index `IX_Aktivitaeten_VerweisTyp_VerweisId`)
Automatisch gepflegte Einstellungen (`…Letzter…`), `Zertifikat.Live*` und
`PreislistenVersion.Daten` vom Audit ausnehmen (Typ- oder Property-Ausschluss in
`ErpDbContext.NichtAuditieren`). Sonst wächst das Audit-Log um hunderte
nutzlose Zeilen pro Tag und kopiert bei jedem Preislisten-Import bis zu 5 MB.
Index `Aktivitaeten(VerweisTyp, VerweisId)` ergänzen (siehe Q1), weil sonst
jeder Vorgangsaufruf die Chatter-Tabelle komplett scannt.

**N5 · Kleine Härtungen.** (niedrig, Aufwand je sehr klein) — **erledigt** (alle fünf Punkte; `Dateinamen.Sicher` im Domain-Projekt mit Tests)
- `PdfDienst`: Serverpfad der PFX-Datei nicht in der Fehlermeldung an den
  Vertragsersteller ausgeben (nur ins Log); geladenes Zertifikat `using`/
  disposen.
- Vertragserzeugung: `Kundennummer` vor `Path.Combine` bereinigen
  (`SichererName` aus dem Chatter-Import wiederverwenden) und Ziel mit
  `LiegtUnter` prüfen.
- `EinstellungenController`: für `Odoo.Url` und `ServicePortal.Url` Schema
  `https://` erzwingen (Key und Token gehen dorthin).
- Odoo-Abgleich: Hinweis „Odoo nicht erreichbar" ohne `ex.Message`.
- `.gitignore`: `appsettings.Production.json` explizit aufnehmen (liegt heute
  nur auf dem Server, war nie im Repo).

### Entscheidungen (kein Code, sondern Festlegung)

**E1 · Admin in Compliance und Betrieb enthalten?** Heute: ja (`Compliance` =
Compliance oder Admin, `Betrieb` = Support oder Admin). Bei vertraulichen
Verträgen wurde Admin bewusst ausgeschlossen. Entweder so belassen und in
CLAUDE.md/ANFORDERUNGEN.md festhalten, oder Admin aus `Compliance` nehmen.

**E2 · Vorgänge/Aufgaben bleiben teamoffen?** Heute darf jeder jeden Schritt
übernehmen, umhängen, erledigen, entfernen, auch fremd zugewiesene. Für ein
kleines Team sinnvoll; wenn eingeschränkt, dann minimal: Entfernen und Wieder
öffnen nur für Zuständigen, Bereichsleiter oder Admin.

**E3 · Verlaufseinträge im Namen anderer.** Wer einen Eintrag mit fremden
Teilnehmern erfasst, erscheint nicht als Autor. Entweder akzeptieren (Audit-Log
kennt den Erfasser) oder `ErstelltVonId` zusätzlich speichern und anzeigen.

**E4 · Test- und Produktivumgebung auf dem Server.** Spätestens zum Produktiv-
Reset eine zweite IIS-Unteranwendung `/Kundenzentrale-Test` mit eigenem
App-Pool, eigener Datenbank, eigenen Ablageordnern (nie das Kunden-Archiv),
eigenem Data-Protection-Schlüsselring und abgeschaltetem Mailversand, Odoo-
Import und Portal-Push. Release-Weg: Publish zuerst nach Test, dann nach Prod.
Konzept gehört in DEPLOYMENT.md neben den Produktiv-Reset.

### Offen aus dem 15.10.2026 (Kurzform)

- **V3b** Neue Domänenkonten inaktiv anlegen oder AD-Gruppe verlangen (vor Produktiv-Reset).
- **V4** — **erledigt 29.10.**: `SchutzKopfzeilen`-Middleware verlangt `X-Requested-With: Kundenzentrale` bei jedem schreibenden `/api`-Aufruf (sonst 403), das Blazor-Frontend setzt die Kopfzeile am HttpClient.
- **V5** — **erledigt 29.10.**: `nosniff`, `X-Frame-Options: SAMEORIGIN`, CSP `frame-ancestors 'self'`, `Referrer-Policy`; Vertragsdokumente nur als PDF inline, alle anderen Typen als Download.
- **V6** SQL-Verbindung ohne `TrustServerCertificate` (Server-Zertifikat nötig). Mittel.
- **V7** — **erledigt 29.10.**: Launch-Profil `http-prod-db` entfernt.
- **V8** Audit-Ansicht für Admins, 403/Rollenwechsel/Geheimnis-Änderung als eigene Log-Einträge, Aufbewahrungsregel. Mittel.
- **V9** `db_ddladmin` begründen oder Migrationen als Publish-Schritt. Mittel.
- **V10** Backups einrichten, Restore einmal testen, Schlüsselbund-Notfallweg dokumentieren. Mittel.
- **V11** Tests für Pfadprüfung, Vertraulich-Filter, Policies; Build+Test+Scan als Skript. Niedrig.
- **V12** Einheitliche Eingabevalidierung (400 statt 500). Niedrig.
- **V13** Rate-Limits und globale Body-Limits vor einem Außenzugang. Niedrig.
- **V14** Admin kann jedes Verzeichnis als Ablage setzen; App-Pool per NTFS eng halten. Niedrig.
- **V15** `AllowedHosts` auf die echten Hostnamen. Niedrig.

---

## Teil 3 · Kurzfassung für Folien

**Gesamteinschätzung:** Das Sicherheitsniveau ist gegenüber dem 15.10.
**gestiegen**: die drei damaligen Hoch-Prio-Punkte (HTTPS, Pakete, aktive
Mitarbeiter) sind umgesetzt, und die neuen Bereiche (Vorgänge, Chatter-Import,
Zertifikate, Mail) halten die Grundregeln ein: deny-by-default, Rollen aus der
DB, Geheimnisse verschlüsselt und write-only, Pfade geprüft, externe Anhänge
nur als Download, kein XSS im Wiki-Formatierer. Der einzige echte neue Befund
ist die Zertifikats-Live-Prüfung (N1): ein fehlendes Policy-Attribut und zu
gesprächige Fehlertexte, beides in einer Stunde behoben. Alles Weitere sind
Robustheit (Parallelläufe, Transaktionen), Hygiene (Audit-Rauschen) und die
bekannten Betriebsthemen (CSRF-/Security-Header, SQL-Zertifikat, Backups).

**Fünf Stärken**
1. Keine Passwörter, keine Tokens im eigenen System; HTTPS seit 29.10.
2. 131 Endpunkte, alle geschützt; Rollen aus der Datenbank; Deaktivieren sperrt aus.
3. Vier Geheimnisse DPAPI-verschlüsselt, write-only, nie in Logs oder Antworten.
4. Externe Inhalte (Odoo-Chatter, Wiki-Text) werden sicher ausgeliefert bzw. kodiert.
5. Dokumente unveränderlich, alles im Audit-Log, Build warnungsfrei, 145 Tests grün.

**Fünf Maßnahmen als Nächstes** (N1 am 29.10. erledigt)
1. Vorgangs-Abgleich sperren, Transaktionen bei Vertrag/Vorlage (N2, N3).
2. Audit-Rauschen und fehlenden Index beheben (N4).
3. CSRF- und Security-Header, Profil `http-prod-db` entfernen (V4, V5, V7).
4. Kleine Härtungen: PFX-Pfad, Kundennummer, URL-Schema (N5).
5. Backups, SQL-Zertifikat, Test/Prod-Trennung zum Produktiv-Reset (V10, V6, E4).

**Zahlen für eine Folie**
| Kennzahl | 15.10.2026 | 29.10.2026 |
|---|---|---|
| Geprüfte Regeln | 50 | 61 |
| davon „gut" | 32 | 37 (nach N1) |
| davon „solide" | 6 | 15 |
| davon „ausbaufähig" | 12 | 9 |
| Offene Verbesserungspunkte Sicherheit | 15 | 17 (0 hoch, 11 mittel, 6 niedrig) + 4 Entscheidungen |
| Bekannte Paket-Schwachstellen | 8 in 4 Paketen | 0 |
| Unit-Tests | 28 | 177, alle grün |
| Build-Warnungen | – | 0 |

---

## Teil 4 · Qualität und Performance (neu)

Sortiert nach Nutzen. Aufwand: klein = unter einem Tag, mittel = wenige Tage,
groß = schrittweise.

**Q1 · Fehlender Index auf `Aktivitaeten(VerweisTyp, VerweisId)`.** (klein) — **erledigt 29.10.**
Vier heiße Abfragen filtern darauf (Vorgangs-Detail, Akte-Aufgaben,
Strang umhängen, Vertrag löschen). Nach dem Chatter-Import hat die Tabelle
zehntausende Zeilen, jeder Vorgangsaufruf scannt sie komplett.
`ErpDbContext.cs:111-131` + Migration, gefiltert auf `VerweisId IS NOT NULL`.

**Q2 · Vertragsliste und Kennzahlen laden alle Verträge in den Speicher.** (mittel)
`VertraegeController.Liste` materialisiert alle Verträge mit Bestandteilen,
filtert und pagt dann im Speicher (wegen des berechneten Vertragsendes);
`Kennzahlen` lädt dieselbe Menge erneut. Maßnahme: Vertragsende als persistierte
Spalte beim Speichern berechnen, dann `Skip/Take` in SQL.

**Q3 · Mitarbeiter-Lookup 3–5× pro Request.** (klein) — **erledigt 29.10.** (`AngemeldeterMitarbeiter`, scoped, lädt Id/Rollen/Aktiv/WikiThemen einmal; Policies, zehn Controller und `WikiBerechtigung` nutzen ihn; `ErpDbContext` behält seinen eigenen Cache für das Audit)
Fallback-Policy, Rollen-Handler, Controller-Hilfen und `ErmittleMitarbeiterId`
fragen denselben Datensatz je Request mehrfach ab (18 Vorkommen in 12 Dateien).
Maßnahme: `IAktuellerBenutzer` (scoped) um einen Lazy-Cache mit Id, Rollen,
Aktiv erweitern; alle Stellen darauf umstellen. Beseitigt zugleich Duplikate.

**Q4 · Kategorie-Regel in vier Kopien.** (mittel)
`KundenController` enthält die Regel für Kundenkategorien viermal (Filter,
Projektion, Einzeln mit acht sequenziellen `AnyAsync`, `KategorienAsync`).
Maßnahme: Expression-Bausteine im Domain-Projekt, einmal definiert.

**Q5 · Kein globales Exception-Handling.** (klein) — **erledigt 29.10.** (`DatenbankFehlerHandler` als `IExceptionHandler`: Concurrency, Unique 2601/2627, FK 547 → 409 mit Text; alles andere bleibt 500)
Kein `UseExceptionHandler`/`AddProblemDetails`; `DbUpdateException`,
Unique-Verstöße und `NummernkreisDienst`-Fehler enden als 500. Maßnahme:
ProblemDetails + Mapping `DbUpdateException` → 409, Validierung → 400.

**Q6 · Verlaufseintrag in sechs Varianten von Hand gebaut.** (mittel)
Vier Stellen im `KundenController`, je eine in `VertraegeController`,
`AkteController`, `VorgangsProtokoll`, mit unbeabsichtigten Unterschieden
(Teilnehmer, Stufe). Dazu `Kuerzen` fünffach. Maßnahme: ein `VerlaufDienst`
als einziger Schreibweg.

**Q7 · Vorgangs-Abgleich: Parallelläufe und N+1.** (mittel) Siehe N2; zusätzlich
je Kandidat zwei Abfragen und je Vorgang Laden + SaveChanges.

**Q8 · Importe ignorieren das Herunterfahren.** (klein bis mittel)
`OdooImportDienst` und `ChatterImportDienst` starten per `Task.Run` ohne
`ApplicationStopping`-Token; die Importer selbst nehmen keinen
`CancellationToken`. `ChatterImportDienst.Bericht` kopiert bei jedem Status-Poll
bis zu 4 MB. Maßnahme: Lifetime-Token durchreichen, Bericht ab Offset liefern.

**Q9 · Zeitwerte gemischt.** (klein)
`DateTime.Now`/`Today` (Vorgänge, Aufgaben, Verträge) neben `UtcNow`
(Zertifikate, Akte-Überblick). Zwischen 0 und 2 Uhr weichen Fälligkeiten und
Tagesläufe um einen Tag ab. Maßnahme: ein `Uhr`-Dienst mit `HeuteLokal`
(Europe/Berlin) und `JetztUtc`.

**Q10 · Zertifikats-Erinnerungen: Mail raus, Status erst am Ende gespeichert.** (klein)
Stirbt der Prozess in der Schleife, gehen beim nächsten Takt alle Mails erneut
raus. Maßnahme: SaveChanges nach jeder erfolgreichen Mail.

**Q11 · Sehr lange Methoden.** (groß, schrittweise)
19 Methoden über 80 Zeilen, Spitzenreiter `OdooImporter.AusfuehrenAsync` (359),
`VertragsBestandScanner` (220), `ChatterImporter` (188), `KundenController.Liste`
(131). Fachlogik in Controllern: PDF/Ablage/Nummern in `AusVorlage`,
Dateioperationen in `Freigeben`. Prioritär `AusVorlage` + `VorbereitenAsync` →
`VertragsErzeugungDienst`, `Freigeben` → `VorlagenFreigabeDienst`; dann sind
beide testbar (Q12).

**Q12 · Tests decken nur reine Domain-/Parser-Logik ab.** (mittel)
145 Tests ohne Datenbank: ChatterFilter (31), VorgangsLogik (19), Zertifikat-
Erinnerung (10), Wiki (15), Preisliste (7), Vertragsende, Dokumentdatum,
Aufgaben. Fehlt: Rollen-Policies, `WikiBerechtigung`, Pfadprüfungen,
Vertraulich-Filter, Nummernkreis-Retry, Vorgangs-Abgleich, Audit/Unveränderlichkeit,
Kategorie-Regel. Pfad- und Vertraulich-Hilfen sind `private static` — in eigene
Klassen ziehen, dann sind es reine Unit-Tests; für Policies und Nummernkreis
Sqlite-InMemory einführen.

**Q13 · Hintergrund-Intervalle hart codiert.** (klein)
Nur der Serverstatus-Takt ist konfigurierbar; Zertifikate und Abgleich
(1 h) sowie die Startverzögerungen sind fest. Maßnahme: Sektion `Hintergrund`
in `appsettings`.

**Q14 · DEPLOYMENT.md als Änderungsprotokoll.** (klein, Doku)
Über 20 offene „Publish aktualisieren"-Kästchen seit September. Maßnahme:
erledigte Publishes abhaken oder in ein `CHANGELOG.md` verschieben; in
DEPLOYMENT.md nur noch die tatsächlich offenen Serverschritte führen.

**Kleinere Hinweise:** `KundenController.Liste` mit 15 korrelierten `EXISTS`
pro Zeile (bei wenigen hundert Kunden in Ordnung); `VorgaengeController.Uebersicht`
lädt stets alle laufenden Vorgänge (in Ordnung bei heutiger Menge);
`VorlagenController` nutzt synchrones `File.Copy`; `OdooClient` ohne expliziten
Timeout (100 s Standard); `AsNoTracking` ist bei Lesezugriffen durchgängig
gesetzt (106 Vorkommen); Paging überall außer Vertragsliste.

**Paketstand (29.10.2026):** alle Microsoft-Pakete 10.0.13 (10.0.14 verfügbar),
Microsoft.Identity.Web 4.14.2 (4.16.1), Telerik 2026.3.923 (2026.3.1027),
Test-SDK 17.14.1 (18.11.0). Keine Schwachstellen.

---

## Anhang · Prüfumfang

Geprüft: `Program.cs`, alle 17 Controller mit 131 Endpunkten (Policy je
Endpunkt, Zusatzprüfungen im Methodenkörper, DTO-Felder), alle Dienste der API
(Policies, Geheimnisse, Mail, Zertifikate, Serverstatus, Odoo-Import, Chatter-
Import, Vorgangs-Abgleich, PDF, Vorlagen), `ErpDbContext` (Audit, Indizes,
Unveränderlichkeit), Domain-Entitäten, Import-Tool, Blazor-Frontend
(`MarkupString`, HttpClient-Setup, Admin-Seite), `appsettings*`,
`launchSettings`, `.gitignore`, Git-Verlauf auf Geheimnisse und
Zertifikatsdateien, Build, Testlauf, NuGet-Schwachstellen- und Veraltet-Scan,
DEPLOYMENT.md-Stand.

Nicht geprüft: tatsächliche IIS-, NTFS- und SQL-Server-Konfiguration auf dem
Server, GPOs, Netzsegmentierung, Telerik-Bibliotheken selbst, das externe
Service Portal, Odoo selbst.
