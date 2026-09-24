# Deployment auf RS-W2019-01

Stand 10.09.2026 · Es gibt **zwei Wege** — Weg A ist der beschlossene
Sofort-Weg (wie beim Urlaubsplaner), Weg B (Entra ID) bleibt als spätere
Option dokumentiert.

> **Ist-Stand: seit 10.09.2026 produktiv über Weg A.**
> Ordner `C:\Kundenzentrale`, IIS-Site/App-Pool „Kundenzentrale",
> Binding Port **8081** ohne Hostname → `http://rs-w2019-01:8081`.
> Update-Ritual: App-Pool beenden → Inhalt von `publish` (frisch per
> `dotnet publish src/Rausoft.Erp.Api -c Release -o publish`) drüberkopieren
> (`appsettings.Production.json` liegen lassen) → App-Pool starten.
> Noch offen: DNS-Name + Port 80, Intranet-Zone per GPO, Nacharbeiten A5.

## Nachzuziehende Schritte (Testbetrieb → Server)

Laufende Liste aller Änderungen seit dem Go-Live vom 10.09.2026 — beim
nächsten Server-Update Publish kopieren und die Punkte abhaken:

- [ ] **Publish aktualisieren** (enthält: Benutzerverwaltung „Benutzer &
  Rollen" im Admin-Bereich, 10.09.2026). Migrationen laufen automatisch,
  manuelle Server-Schritte: keine.
- [ ] **Publish aktualisieren (24.09.2026):** „Ordner öffnen" + „Pfad kopieren"
  an den Vertragsdokumenten — Anwender kommen direkt in den Ablage-Ordner statt
  über den Download mit kryptischem Namen.
- [ ] **Edge-GPO für „Ordner öffnen"** (einmalig, AD-seitig): Damit der
  Ordner-Knopf den Explorer öffnet, in der GPO die Edge-Richtlinie
  **„IntranetFileLinksEnabled"** („Zulassen, dass Dateilinks der Intranetzone
  in Datei-Explorer geöffnet werden") aktivieren — braucht die Edge-ADMX-
  Vorlagen im Central Store. Ohne die Richtlinie funktioniert ersatzweise
  der „Pfad"-Knopf (kopieren + in Explorer-Adresszeile einfügen).
- [ ] **Odoo-Import als geplante Aufgabe einrichten** (24.09.2026) — Anleitung
  im Abschnitt „Odoo-Import als geplante Aufgabe", Paket liegt in
  `publish-import`.
- [ ] **Umzug auf /Kundenzentrale** (optional, 11.09.2026): Die App kann jetzt
  wie der Urlaubsplaner als IIS-**Unteranwendung** laufen (`http://rs-w2019-01/Kundenzentrale`).
  Dazu: neues Publish kopieren → im IIS unter der Default-Website
  „Anwendung hinzufügen" (Alias `Kundenzentrale`, physischer Pfad
  `C:\Kundenzentrale`, App-Pool „Kundenzentrale" auswählen) → auf der
  Anwendung Windows-Auth aktivieren/Anonym deaktivieren → alte Site auf
  Port 8081 löschen (und Firewall-Regel 8081 entfernen). Voraussetzung:
  Auf der Default-Website läuft der Urlaubsplaner-Pool weiter — die
  Unteranwendung nutzt ihren eigenen Pool, das stört sich nicht.
- [x] ~~GPO: Intranetzone~~ — erledigt 11.09.2026 (eigene GPO
  „Kundenzentrale Intranetzone", Benutzerkonfiguration, `http://rs-w2019-01` = 1). (einmalig, AD-seitig) — damit KEIN Benutzer je
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
Entwicklungsrechner. Der Odoo-API-Key wird dabei **nie in Dateien oder der DB
gespeichert** — nur als Umgebungsvariable auf dem Server.

1. **Paket kopieren:** `dotnet publish src/Rausoft.Erp.OdooImport -c Release -o publish-import`
   (macht Claude) → Inhalt nach `C:\Kundenzentrale-Import` auf dem Server.
2. **Umgebungsvariablen** auf dem Server setzen (PowerShell als Admin;
   maschinenweit — lesbar nur für lokal angemeldete Konten, am Server also Admins):
   ```powershell
   [Environment]::SetEnvironmentVariable("ODOO_URL", "https://<eure-odoo-url>", "Machine")
   [Environment]::SetEnvironmentVariable("ODOO_DB", "<odoo-datenbankname>", "Machine")
   [Environment]::SetEnvironmentVariable("ODOO_BENUTZER", "<api-benutzer@rausoft.de>", "Machine")
   [Environment]::SetEnvironmentVariable("ODOO_API_KEY", "<der-key>", "Machine")
   [Environment]::SetEnvironmentVariable("KUNDENZENTRALE_DB", "Server=RS-W2019-01;Database=Kundenzentrale;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True", "Machine")
   ```
   (Werte stehen auf dem Entwicklungsrechner in denselben Variablen.)
3. **Aufgabenplanung** → „Aufgabe erstellen":
   - Name `Kundenzentrale Odoo-Import`; Konto mit SQL-Rechten auf die DB
     (db_datareader + db_datawriter, z. B. dasselbe Dienstkonto wie der
     App-Pool); „Unabhängig von der Benutzeranmeldung ausführen".
   - Trigger: täglich 03:00.
   - Aktion 1: Programm `cmd.exe`, Argumente
     `/c ""C:\Kundenzentrale-Import\Rausoft.Erp.OdooImport.exe" import >> "C:\Kundenzentrale-Import\import.log" 2>&1"`
   - Aktion 2: wie Aktion 1, nur mit `abos` statt `import`.
4. Einmal per „Ausführen" testen und `import.log` prüfen.
5. `vertragsscan` (neue Dateien im Kunden Archiv) bleibt bewusst manuell bzw.
   kann als eigene wöchentliche Aufgabe ergänzt werden — das Konto braucht
   dafür Lesezugriff auf die Freigabe.

## Produktiv-Reset zum Zeitpunkt X (beschlossen 10.09.2026)

Das Live-System läuft zunächst als **offener Testbetrieb für alle** —
zum Zeitpunkt X wird einmal sauber resettet und dann produktiv gearbeitet.
Doppelarbeit ist einkalkuliert. Ablauf (macht Claude zusammen mit MP):

1. **Vorher sichern:** DB-Vollbackup + Export der übernehmenswerten manuellen
   Pflege per Skript (Schlüssel = Kundennummer/Vertragsnummer): Merkmale,
   Archiv-Status, Server-Zuordnung + Mandanten-Kennung, Individualentwicklung,
   manuell korrigierte Vertragsstatus (Kündigungs-Klärungen), Mitarbeiter-Rollen.
2. App-Pool stoppen, Datenbank neu anlegen (DROP/CREATE) — der nächste
   App-Start erzeugt Schema + Seeds (Nummernkreise, Einstellungen,
   Merkmal-Stammliste) automatisch.
3. **Importe neu laufen lassen:** Odoo-Kontakte (`import`), Abos (`abos`),
   Vertragsbestand (`vertragsscan`) — inklusive Kündigungsautomatik.
4. Gesicherte manuelle Pflege zurückspielen (Skript aus Schritt 1).
5. Nacharbeiten: Portal-Token neu setzen (Geheimnisse überleben den Reset
   nicht), echte Server anlegen (keine Test-Server mehr), Einstellungen prüfen.
6. Testdateien aufräumen, die die Vertragserzeugung während der Testphase
   erzeugt hat (siehe Empfehlung unten).

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
