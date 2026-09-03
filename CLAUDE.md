# Kundenzentrale (Arbeitstitel)

Eigenentwicklung der rausoft GmbH zur Ablösung von Odoo bis Ende 2027.
Ziel-Stack: .NET Server-Applikation mit REST API (Backend), Frontend entkoppelt
(geplant: Blazor), SQL Server, Betrieb lokal auf eigenen Windows Servern.
PDF-Erzeugung: Telerik Reporting (lizenziert).
Die Buchhaltung bleibt bei DATEV — dieses System macht Kontakte, Verträge,
Aufgaben, Timeline, Angebote/CRM, Tickets und schlanke Fakturierung.

## Wichtige Dateien

- `ANFORDERUNGEN.md` — gesammelte Anforderungen, Phasenplan, Datenmodell-Leitplanken. **Zuerst lesen.**
- `mockup/index.html` — klickbares HTML-Mockup (Stand der Konzeptdiskussion).
- `.claude/launch.json` — Vorschau: PHP-Server auf Port 8378 (`preview_start` mit Name `mockup`).

## Mockup-Artefakte

- **Variante A = gewählter Entwurf** (Entscheidung 2026-08-10;
  `mockup/index.html`, vollständig, alle Bereiche):
  https://claude.ai/code/artifact/6039a0c4-e0a5-4bfb-a960-d63b5553d7ec
- Variante B (`mockup/variante-b.html`, verworfener Vergleichsentwurf „ruhig &
  aufgeräumt" — als Ideenfundus behalten, nicht weiterpflegen):
  https://claude.ai/code/artifact/1d768777-0a28-457a-9047-623acd953640
- **Konzept „Kundenakte ruhig" 09/2026** (`mockup/akte-ruhig.html`,
  Beruhigungs-Iteration der laufenden App nach B-Prinzipien, in Diskussion):
  https://claude.ai/code/artifact/5646e699-a66b-4778-abae-7da8eadc5ebb

Bei Updates aus diesem Projekt die jeweilige URL als `url`-Parameter an das
Artifact-Tool übergeben, sonst entsteht eine neue URL.

## Kickoff-Entscheidungen Phase 1 (2026-08-13)

- **.NET 10 LTS** (SDK 10.0.400 installiert; .NET 8 wäre Nov 2026 aus dem Support).
- **Authentifizierung: Entra ID pur** — die API validiert Entra-ID-Tokens
  (`Microsoft.Identity.Web`), Mitarbeiter-SSO per M365-Konto, CTI/Service-Portal
  über eigene App-Registrierungen (Client Credentials). Keine Passwörter/Tokens
  in eigener DB; Benutzertabelle referenziert nur die Entra-Objekt-ID.
- **Frontend: Blazor WebAssembly (standalone)**, spricht ausschließlich über
  die REST API.
- **Namespace-Basis `Rausoft.Erp`**, Themen als Unter-Namespaces
  (z. B. `Rausoft.Erp.Domain.Customers`, `.Contracts`). Solution `Rausoft.Erp`
  im Unterordner `src/` + `tests/`.
- Datenmodell-Entwurf: `DATENMODELL.md`.

## Entwicklung

- Build: `dotnet build Rausoft.Erp.slnx` · Tests: `dotnet test Rausoft.Erp.slnx`
- Migrationen: `dotnet ef migrations add <Name> --project src/Rausoft.Erp.Data --startup-project src/Rausoft.Erp.Api`
  (dotnet-ef als lokales Tool im Manifest); anwenden mit `dotnet ef database update …`.
- Connection String `Kundenzentrale` in `appsettings.json` nutzt Windows-Auth
  (kein Secret); Entra-IDs (`EntraId`-Sektion) sind noch Platzhalter, bis die
  App-Registrierung existiert.

## Konventionen

- Sprache: Deutsch (UI-Texte, Doku, Kommunikation).
- Keine Secrets im Klartext; Secrets über Umgebungsvariablen.
- **Verschlüsselung at rest (verbindlich):** Tokens, API-Keys, SMTP-/IMAP-
  Zugangsdaten und alle sonstigen Geheimnisse werden **nie im Klartext
  persistiert** — weder in der Datenbank noch in Dateien oder Logs. In der DB
  nur verschlüsselt ablegen (ASP.NET Core Data Protection, Schlüsselschutz per
  Windows DPAPI auf dem Server). Benutzer-Passwörter ausschließlich als Hash
  (ASP.NET Identity / PBKDF2 o. Argon2), niemals entschlüsselbar. Auth-Tokens
  serverseitig nur als Hash speichern, damit ein DB-Leak keine gültigen
  Sitzungen preisgibt.
- Interne Preisliste ist vertraulich — Produktnamen ja, echte Preise nicht in
  teilbare Artefakte übernehmen.
- Layout-Entscheidung Kundenakte: Verlauf links, Fakten rechts (bewusst so
  entschieden, nicht erneut umdrehen).
