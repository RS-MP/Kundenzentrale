# Service Portal: Release-Stand in der Antwort der Server-Status-API

**Zweck:** Anleitung für das Projekt „ID.prove Service Portal" (Erweiterung von
Feature #51). Die Kundenzentrale pusht Server-Status ans Portal und möchte in
der **Antwort** den im Portal gepflegten **Release-Stand je Server** zurückbekommen,
um ihn in ihrer Server-Übersicht anzuzeigen. Selbsterklärend geschrieben für
eine Claude-Code-Session im Portal-Projekt ohne Kontext aus der Kundenzentrale.

**Angefragt von:** Kundenzentrale · 09.09.2026 (M. Pätzold)

---

## Ist-Zustand (Feature #51)

`POST /api/server-status` (Bearer-Token) nimmt ein Array von Statusmeldungen an
und antwortet mit:

```json
{ "verarbeitet": ["Shared-01", "Private-A"], "unbekannt": ["Tippfehler-Alias"] }
```

Der Release-Stand je Server wird im Portal manuell gepflegt (Menü „Server",
Techniker trägt ihn nach dem Deployment ein) und ist bisher **nicht** Teil der
Schnittstelle.

## Gewünschte Erweiterung

Die Antwort um ein optionales Objekt **`releases`** ergänzen — je **verarbeitetem**
Alias der im Portal gepflegte Release-Stand (String, wie im Portal eingetragen):

```json
{
  "verarbeitet": ["Shared-01", "Private-A"],
  "unbekannt": [],
  "releases": {
    "Shared-01": "3.0.12",
    "Private-A": "3.0.9"
  }
}
```

Regeln:

- Nur Aliase aus `verarbeitet` aufnehmen. Server ohne gepflegten Release-Stand
  einfach weglassen (kein leerer String nötig).
- Werte-Format frei (max. ~50 Zeichen sinnvoll) — die Kundenzentrale zeigt den
  String unverändert an und kürzt notfalls auf 50 Zeichen.
- **Abwärtskompatibel:** Das Feld ist optional. Die Kundenzentrale kommt mit
  Antworten ohne `releases` klar (sie zeigt dann einfach keinen Release-Stand);
  andere/ältere Aufrufer des Endpunkts ignorieren das zusätzliche Feld.
- Keine Sicherheitsbedenken: Es fließen nur Daten zurück, die das Portal
  ohnehin kennt und anzeigt (Alias + Release) — keine Kunden- oder
  Infrastruktur-Informationen.

## Test (aus Sicht der Kundenzentrale)

1. Im Portal bei einem Server (z. B. `Test-01`, ggf. anlegen) einen
   Release-Stand eintragen, z. B. `3.0.12-test`.
2. Push senden:
   ```
   curl -X POST https://serviceportal.idprove.de/api/server-status \
     -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
     -d '[{"server":"Test-01","isAlive":true,"iDproveConnectorActive":true,"sanctionListDate":"09.09.2026 08:00"}]'
   ```
3. Erwartete Antwort: `releases` enthält `"Test-01": "3.0.12-test"`.

Die Kundenzentrale ist bereits vorbereitet: Sie liest `releases` tolerant aus,
speichert den Stand am Server-Datensatz und zeigt ihn in ihrer Server-Übersicht
(mit Absprung-Link ins Portal, wo der Stand gepflegt wird).
