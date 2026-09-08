# Kundenzentrale ↔ ID.prove Service Portal: Server-Status-Schnittstelle

**Zweck dieses Dokuments:** Vollständige Anleitung für das Projekt „Kundenzentrale",
damit dort die Server-Status-Meldung ans Service Portal umgesetzt werden kann –
selbsterklärend, auch für eine Claude-Code-Session ohne Kontext aus dem Portal-Projekt.

**Stand:** 08.09.2026 · Portal-Feature #51 (ab Portal-Version nach 2026-09-07)

---

## 1. Aufgabenteilung (Sicherheitsmodell – bitte einhalten!)

Das Service Portal läuft **extern** (IONOS, https://serviceportal.idprove.de) und ist
bewusst so gebaut, dass daraus **keine Rückschlüsse auf Kunden und Infrastruktur**
möglich sind. Die Kundenzentrale läuft **intern** und kennt die sensiblen Daten.

| Wissen | Kundenzentrale (intern) | Service Portal (extern) |
|---|---|---|
| Welcher Kunde auf welchem Server liegt (Vertragsverwaltung) | ✅ | ❌ niemals |
| Hostnamen, URLs, IP-Adressen der Server | ✅ | ❌ niemals |
| Mandanten-Kennungen (`customer`-Parameter) | ✅ | ❌ niemals |
| Server-**Alias** (neutraler Anzeigename, z. B. „Shared-01") | ✅ (als Fremdschlüssel) | ✅ (wird dort gepflegt) |
| Release-Stand je Server | optional | ✅ (Techniker trägt manuell ein) |
| Health-Status + Sanktionslisten-Stand | ✅ (ermittelt sie) | ✅ (nur das Ergebnis, per Push) |

**Konsequenz:** Die Kundenzentrale ruft die Server-APIs auf und **pusht nur die
Ergebnisse** ans Portal. Im Push stehen ausschließlich: Alias, Statuswerte,
Sanktionslisten-Datum, Fehlertext. Der Fehlertext darf keine internen URLs oder
Kundennamen enthalten (im Zweifel generisch formulieren, z. B. „Timeout").

Die Zuordnung läuft über den **Alias**: Er muss in beiden Systemen exakt gleich
geschrieben sein. Gepflegt wird er im Portal (Menü „Server", nur Rausoft-intern);
die Kundenzentrale speichert ihn als Referenzfeld am Server-Datensatz.

## 2. Datenquelle: HealthCheck-API der ID.prove-Server

Jeder ID.prove-Server bietet einen Healthcheck **ohne Token**:

```
GET https://<server>/api/HealthCheck?customer=<mandanten-kennung>
```

Antwort (JSON):

```json
{
  "isAlive": true,
  "iDproveConnectorActive": true,
  "sanctionListDate": "15.07.2026 10:30",
  "errorMessage": ""
}
```

| Feld | Bedeutung |
|---|---|
| `isAlive` | API erreichbar und Mandant bekannt |
| `iDproveConnectorActive` | ID.prove-Connector auf dem Server aktiv |
| `sanctionListDate` | Stand der aktuellen Sanktionsliste (String, deutsches Format) |
| `errorMessage` | Fehlertext, leer wenn alles ok |

Empfehlung für den Aufruf aus der Kundenzentrale: Timeout ~15 s; schlägt der
Aufruf fehl (Timeout, DNS, 5xx), gilt der Server als **nicht erreichbar** →
`isAlive: false` und eine kurze, generische Fehlermeldung melden.

## 3. Ziel: Push-Endpunkt des Portals

```
POST https://serviceportal.idprove.de/api/server-status
Authorization: Bearer <token>
Content-Type: application/json; charset=utf-8
```

- **Token:** steht im Portal unter **Admin → Einstellungen → „Server-Status
  (Push-API)"**. Er ist installationsspezifisch (aus dem APP_KEY abgeleitet) und
  ändert sich nur, wenn der APP_KEY des Portals wechselt.
  **Geheim halten:** in der Kundenzentrale als geschützte Konfiguration bzw.
  Umgebungsvariable speichern – niemals im Quellcode oder Repository.
- Falscher/fehlender Token → **HTTP 404** (bewusst kein 401, verrät nichts).
- Falsche Methode (GET) → 405. Ratenbegrenzung: 30 Aufrufe/Minute.
- Alternativ zum Header wird `"token": "<token>"` als Feld im JSON akzeptiert –
  der Header ist zu bevorzugen.

### Request-Body

Ein **Array** von Statusmeldungen (empfohlen: alle Server in einem Aufruf) –
ein einzelnes Objekt ohne Array geht auch:

```json
[
  {
    "server": "Shared-01",
    "isAlive": true,
    "iDproveConnectorActive": true,
    "sanctionListDate": "15.07.2026 10:30",
    "errorMessage": ""
  },
  {
    "server": "Private-A",
    "isAlive": false,
    "iDproveConnectorActive": false,
    "sanctionListDate": "",
    "errorMessage": "Timeout nach 15s"
  }
]
```

| Feld | Typ | Pflicht | Regeln |
|---|---|---|---|
| `server` | String | ✅ | **Alias exakt wie im Portal angelegt** (Groß-/Kleinschreibung zählt) |
| `isAlive` | Bool | ✅ (Default false) | Ergebnis des Healthchecks bzw. false bei Aufruf-Fehler |
| `iDproveConnectorActive` | Bool | optional | 1:1 aus dem Healthcheck durchreichen |
| `sanctionListDate` | String | optional | 1:1 durchreichen; max. 50 Zeichen (Portal kürzt) |
| `errorMessage` | String | optional | max. 500 Zeichen (Portal kürzt); KEINE internen URLs/Kundennamen |

### Response (HTTP 200)

```json
{ "verarbeitet": ["Shared-01", "Private-A"], "unbekannt": ["Tippfehler-Alias"] }
```

**`unbekannt` bitte auswerten und loggen/alarmieren:** Diese Aliase existieren im
Portal nicht (Tippfehler oder dort noch nicht angelegt) – ihre Status verfallen
stillschweigend. Das Portal legt bewusst **keine** Server automatisch an.

## 4. Was das Portal daraus macht

Die Server-Übersicht im Portal zeigt je Server eine Ampel plus Details:

- 🟢 **erreichbar** – `isAlive` und Connector aktiv
- 🟡 **Connector inaktiv** – erreichbar, aber `iDproveConnectorActive: false`
- 🔴 **nicht erreichbar** – `isAlive: false` (der `errorMessage`-Text erscheint als Tooltip)
- ⚪ **Status veraltet** – letzte Meldung älter als **26 Stunden** → daran erkennt
  das Team, dass die Kundenzentrale/das Skript nicht mehr meldet
- dazu: „Sanktionsliste: `<sanctionListDate>`" und „gemeldet vor …"

**Takt:** mindestens **1× täglich** senden (gern öfter, z. B. stündlich – die
26-h-Schwelle ist auf einen Tagesrhythmus mit Puffer ausgelegt). Immer **alle**
Server in einem Batch melden, auch die fehlerhaften – nur so bleibt die Ampel ehrlich.

**Nicht Teil dieser Schnittstelle:** der Release-Stand. Den trägt der Techniker
nach dem Deployment manuell im Portal ein (bewusste Entscheidung; der Healthcheck
liefert ihn nicht).

## 5. Referenz-Implementierung

Eine fertige, getestete PowerShell-Vorlage liegt als **`server-status-push.ps1`**
im Deploy-Ordner (`D:\Claude\serviceportal-deploy\`) – sie zeigt exakt den Ablauf
„Healthchecks abfragen → Batch pushen → `unbekannt` prüfen" und kann als
Übergangslösung per Aufgabenplanung laufen, bis die Kundenzentrale übernimmt.

Skizze für die Kundenzentrale (.NET):

```csharp
// je Server (Daten aus der eigenen Server-/Vertragsverwaltung):
//   GET {server.Url}/api/HealthCheck?customer={server.Kennung}  (Timeout 15 s)
//   → bei Fehler: isAlive=false, errorMessage kurz & generisch
// danach EIN Push für alle:
var request = new HttpRequestMessage(HttpMethod.Post,
    "https://serviceportal.idprove.de/api/server-status");
request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); // aus geschützter Config
request.Content = new StringContent(JsonSerializer.Serialize(statusListe),
    Encoding.UTF8, "application/json");
var response = await http.SendAsync(request);
// response: { verarbeitet: [...], unbekannt: [...] } – "unbekannt" loggen/alarmieren
```

## 6. Test-Checkliste für die Anbindung

1. Token aus Admin → Einstellungen kopieren, einen Test-Push mit einem Server senden:
   ```
   curl -X POST https://serviceportal.idprove.de/api/server-status \
     -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
     -d '[{"server":"<Alias>","isAlive":true,"iDproveConnectorActive":true,"sanctionListDate":"15.07.2026 10:30"}]'
   ```
2. Antwort prüfen: Alias unter `verarbeitet`? Sonst steht er unter `unbekannt` →
   Schreibweise mit dem Portal (Menü „Server") abgleichen.
3. Im Portal unter „Server" nachsehen: grüne Ampel + Sanktionslisten-Stand + „gemeldet vor wenigen Sekunden".
4. Negativtests: falscher Token → 404; `isAlive:false` mit `errorMessage` → rote Ampel, Text als Tooltip.
5. Zeitplan einrichten und nach >26 h ohne Meldung prüfen, dass die Ampel auf „Status veraltet" springt (so fällt ein Ausfall der Meldekette auf).
