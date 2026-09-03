# Deployment: Entra ID + IIS auf RS-W2019-01

Stand 03.09.2026 · Ziel: Ablösung des Interimsbetriebs (lokal, ohne Anmeldung)
durch den IIS-Betrieb mit Entra-ID-Anmeldung. Reihenfolge einhalten —
**erst Entra, dann IIS**, sonst kann sich nach dem Deployment niemand anmelden.

## 1 · Entra-ID-App-Registrierungen (Azure-Portal, einmalig)

Portal: https://entra.microsoft.com → Anwendungen → App-Registrierungen.
Es braucht **zwei** Registrierungen:

### 1a · API („Kundenzentrale API")

1. Neue Registrierung → Name `Kundenzentrale API`, Konten: *nur dieses
   Organisationsverzeichnis*, ohne Redirect-URI → Registrieren.
2. **Übernehmen:** Anwendungs-ID (Client-ID) und Verzeichnis-ID (Tenant-ID)
   von der Übersichtsseite.
3. *Eine API verfügbar machen* → App-ID-URI übernehmen (Standard
   `api://<client-id>`) → *Bereich hinzufügen*:
   Name `Zugriff`, wer kann einwilligen: Administratoren und Benutzer,
   Anzeigename/Beschreibung z. B. „Zugriff auf die Kundenzentrale".

### 1b · Frontend („Kundenzentrale Web")

1. Neue Registrierung → Name `Kundenzentrale Web`, Konten: *nur dieses
   Organisationsverzeichnis*, Redirect-URI: Typ **Single-Page-Anwendung (SPA)**,
   URI `https://kundenzentrale.rs.local/authentication/login-callback`
   (an die echte URL anpassen; für Tests zusätzlich
   `http://localhost:5100/authentication/login-callback`).
2. *API-Berechtigungen* → Berechtigung hinzufügen → *Eigene APIs* →
   `Kundenzentrale API` → Bereich `Zugriff` anhaken →
   **Administratorzustimmung erteilen** (Button).
3. Client-ID dieser Registrierung ebenfalls notieren.

### 1c · Werte eintragen

- `src/Rausoft.Erp.Api/appsettings.json` → Sektion `EntraId`: TenantId +
  ClientId der **API**-Registrierung (keine Secrets nötig — die API validiert
  nur Tokens).
- Frontend: MSAL-Konfiguration (TenantId, ClientId **Web**, Scope
  `api://…/Zugriff`) — der MSAL-Einbau in Blazor ist noch offen und wird
  gemacht, sobald die IDs vorliegen (dann entfällt der Entwicklungs-Auth-Modus
  für den Serverbetrieb).
- CTI/Service-Portal später: je eigene Registrierung mit Client Credentials.

### 1d · Mitarbeiter anlegen

Die Benutzertabelle referenziert die Entra-Objekt-ID. Je Mitarbeiter:
Objekt-ID aus Entra (Benutzer → Übersicht) in `Mitarbeiter` eintragen
(Anzeigename, E-Mail, Rollen) — Pflege-UI dafür ist noch offen; übergangsweise
per SQL/Import.

## 2 · IIS auf RS-W2019-01

1. **ASP.NET Core Hosting Bundle** (Version 10.x) auf dem Server installieren:
   https://dotnet.microsoft.com/download/dotnet/10.0 → „Hosting Bundle" →
   danach `iisreset`.
2. **Dienstkonto** (gMSA oder Domänenkonto, z. B. `RS\svc-kundenzentrale`):
   - SQL Server: Login + in DB `Kundenzentrale` nur `db_datareader` +
     `db_datawriter` (Migrationen laufen beim Deployment unter deinem Konto,
     nicht unter dem Dienstkonto — Least Privilege).
   - Dateifreigabe: **Lesezugriff** auf `\\RS-W2019-01\Kunden Archiv`.
3. **Site „Kundenzentrale API"**: Ordner z. B. `D:\Apps\Kundenzentrale\Api`,
   eigener App-Pool (.NET CLR: *No Managed Code*), Identität = Dienstkonto,
   Binding z. B. `https://kundenzentrale-api.rs.local` (internes Zertifikat).
   Veröffentlichen vom Entwicklungsrechner:
   `dotnet publish src/Rausoft.Erp.Api -c Release -o <Zielordner>`
4. **Site „Kundenzentrale Web"** (statisch): Ordner `D:\Apps\Kundenzentrale\Web`,
   Inhalt aus `dotnet publish src/Rausoft.Erp.Web -c Release` →
   `publish/wwwroot`. `wwwroot/appsettings.json` der Web-App auf die
   API-URL stellen. URL-Rewrite für Blazor-Routing (alle Pfade → index.html);
   das Publish legt eine passende web.config bei.
5. **Produktions-CORS**: in `Program.cs` der API die Web-URL freigeben
   (der Development-CORS greift in Produktion nicht) — kleiner Codeschritt,
   zusammen mit MSAL.
6. **Migrationen** je Release vom Entwicklungsrechner:
   `dotnet ef database update --project src/Rausoft.Erp.Data --startup-project src/Rausoft.Erp.Api --connection "Server=RS-W2019-01;Database=Kundenzentrale;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True"`

## 3 · Datenbank-Betrieb

- **Backups einrichten** (SSMS → Verwaltung → Wartungspläne, oder Agent-Jobs):
  täglich Vollsicherung + Transaktionsprotokoll alle 1–4 h
  (Recovery-Modell FULL), Sicherungen auf ein anderes Volume/Share.
- SQL Server 2017: erweiterter Support endet **Okt 2027** — Upgrade einplanen.

## 4 · Offene Code-Schritte vor dem Go-Live (macht Claude, sobald IDs da sind)

- MSAL/Entra-Anmeldung im Blazor-Frontend (Login, Token an API-Aufrufe)
- Produktions-CORS auf die echte Web-URL
- Mitarbeiter-Pflege (Entra-Objekt-IDs) — UI oder Import aus Entra
