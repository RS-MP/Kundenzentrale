# Preislisten-Projekt: maschinenlesbarer Export für die Kundenzentrale

**Zweck:** Anleitung für das Projekt, das die ID.prove-Preislisten (intern und
extern) und das Management Summary erzeugt. Die Kundenzentrale (internes
rausoft-System, .NET/Blazor) soll die Preisliste in ihrem Wiki anzeigen und
später (Phase 3, Angebote) mit den Preisen rechnen. Dafür braucht sie neben
den beiden PDFs **eine JSON-Datei** mit demselben Inhalt in strukturierter Form.
Selbsterklärend geschrieben für eine Claude-Code-Session im Preislisten-Projekt
ohne Kontext aus der Kundenzentrale.

**Angefragt von:** Kundenzentrale · 14.10.2026 (M. Pätzold)

---

## Prompt für das Preislisten-Projekt

> Bitte ergänze die Erzeugung der Preislisten um einen dritten Ausgabe-Artefakt:
> eine Datei `ID.prove_SaaS_Preisliste.json` nach dem Schema unten. Sie wird bei
> **jeder** Erzeugung zusammen mit den beiden PDFs (intern + extern) geschrieben
> und enthält alle Inhalte beider PDFs in strukturierter Form — die interne
> Liste vollständig, jedes Element mit Sichtbarkeit `intern` oder `extern`.
> Die Kundenzentrale importiert diese Datei, zeigt die externen Teile allen
> Mitarbeitern und die internen nur Vertrieb/Compliance/Admin, und verlinkt die
> PDFs auf dem P-Laufwerk. Das Management Summary ist NICHT Teil des Exports.
>
> Zusätzlich bitte eine **fortlaufende Versionsnummer** einführen (ganze Zahl,
> bei jeder Erzeugung +1) und in allen drei Artefakten ausgeben: in der JSON als
> `version`, in beiden PDFs in der Fußzeile als „Version N · Stand Monat Jahr".
> Bisher tragen alle Fassungen nur „Stand: Oktober 2026" — die Kundenzentrale
> braucht die Versionsnummer, um zu erkennen, ob eine Datei neuer ist als die
> importierte. Dateinamen der PDFs bitte mit Version:
> `ID.prove_SaaS_Preisliste_v22.pdf` (intern),
> `ID.prove_SaaS_Preisuebersicht_Kunde_v22.pdf` (extern) — beide mit derselben
> Nummer, auch wenn sich nur eine Liste geändert hat.
>
> Validiere die JSON vor dem Schreiben gegen die Regeln im Abschnitt
> „Regeln", und lege eine Kopie des Schemas mit Beispielwerten im Projekt ab,
> damit spätere Änderungen am Aufbau abgestimmt werden können. Änderungen am
> Schema (neue Felder sind ok, Umbenennungen nicht) bitte in `schema` als
> Version hochzählen und der Kundenzentrale mitteilen.

## Schema `rausoft.preisliste/1`

Alle Preise als Zahl in Euro (ohne Währungszeichen, Dezimalpunkt), `null` =
nicht verfügbar, `"auf Anfrage"` als String, wenn kein fester Preis gilt.
Monatspreise, sofern nicht anders benannt. `sichtbarkeit` ist `"extern"`
(darf in der Kundenfassung stehen) oder `"intern"` (nur intern). Fehlt das
Feld, gilt `extern`. Beispielwerte unten sind Platzhalter, keine echten Preise.

```json
{
  "schema": "rausoft.preisliste/1",
  "produkt": "ID.prove SaaS",
  "version": 22,
  "stand": "2026-10",
  "gueltigAb": "2026-10-01",
  "waehrung": "EUR",
  "konditionen": "Alle Preise pro Monat, zzgl. MwSt., Abrechnung jährlich im Voraus.",
  "dokumente": {
    "intern": "ID.prove_SaaS_Preisliste_v22.pdf",
    "extern": "ID.prove_SaaS_Preisuebersicht_Kunde_v22.pdf"
  },

  "stufen": [
    {
      "schluessel": "starter",
      "name": "STARTER",
      "volumenVon": 1000,
      "volumenBis": 25000,
      "setupEinmalig": 0,
      "setupHinweis": null,
      "inklusiveBenutzer": 1,
      "inklusiveMandanten": 1,
      "merkmale": [
        { "text": "Sanktionslisten EU / UN / US (11 Standardlisten)", "enthalten": true },
        { "text": "REST-API / automatischer Datei-Upload", "enthalten": false },
        { "text": "WorldCheck verfügbar (nur auf Anfrage)", "enthalten": true, "sichtbarkeit": "intern" }
      ]
    }
  ],

  "volumenstufen": [
    { "stammdaten": 1000, "label": "1.000", "preise": { "starter": 0, "professional": 0, "enterprise": null } },
    { "stammdaten": 10000000, "label": "ab 10 Mio", "preise": { "starter": null, "professional": null, "enterprise": "auf Anfrage" } }
  ],
  "volumenRegeln": [
    { "text": "Strikt feste Listenpreise pro Stufe, keine Zwischenwerte — auf die nächsthöhere Stufe aufrunden.", "sichtbarkeit": "intern" }
  ],

  "addons": [
    {
      "schluessel": "erp-connector",
      "name": "ERP-Connector / REST-Anbindung",
      "nameIntern": null,
      "beschreibung": "Anbindung von ERP-Systemen über die REST-API …",
      "verfuegbarAb": "starter",
      "verfuegbarkeitHinweis": "in PROFESSIONAL/ENTERPRISE inklusive",
      "preisMonat": 0,
      "einrichtungEinmalig": 0,
      "einrichtungHinweis": "nur STARTER",
      "sichtbarkeit": "extern",
      "hinweisIntern": null
    },
    {
      "schluessel": "dow-jones",
      "name": "Dow Jones Modul",
      "beschreibung": "…",
      "verfuegbarAb": "professional",
      "preisMonat": 0,
      "sichtbarkeit": "intern",
      "hinweisIntern": "Nur intern / auf Anfrage — nicht in Nachschaltseiten."
    }
  ],

  "sanktionslistenPakete": {
    "standardlisten": ["UNSC", "CFSP", "EU-RU", "EU-BY", "DPL", "UVL", "EL", "MEUL", "SDN", "Non-SDN", "ISN"],
    "pakete": [
      { "schluessel": "europa", "name": "Nationale Listen Europa", "preisMonat": 0,
        "listen": ["SECO", "OFSI", "OeNB", "FR-GEL", "NLNST", "BENS", "PLSL", "CZNSL"] },
      { "schluessel": "einzelliste", "name": "Einzelliste", "preisMonat": 0, "listen": [], "hinweis": "beliebige Einzelliste nach Wahl" }
    ]
  },

  "benutzerStaffel": {
    "hinweis": "Zusätzliche Voll-Benutzer über die in der Stufe inkludierten hinaus.",
    "stufen": [
      { "von": 1, "bis": 4, "preisProBenutzerMonat": 0 },
      { "von": 50, "bis": null, "preisProBenutzerMonat": 0 }
    ],
    "pauschalen": [
      { "name": "Unlimited", "nurStufe": "enterprise", "preisMonat": 0 }
    ],
    "verkaufshilfen": [
      { "text": "Upgrade-Zonen: an den Staffelgrenzen …", "sichtbarkeit": "intern" }
    ]
  },

  "worldcheck": {
    "sichtbarkeit": "intern",
    "beschreibung": "Reseller-Modell von LSEG …",
    "verfuegbarAb": "professional",
    "pakete": [
      { "schluessel": "sanctions-only", "name": "Sanctions Only", "untertitel": "mit Ownership / 50%-Regel · Massenmarkt",
        "staffel": [ { "stammdaten": 1000, "preisMonat": 0, "preisJahr": 0 } ] }
    ],
    "regeln": [ "Stammdaten-Volumen und WorldCheck-Paket sind getrennte Lizenzen …" ]
  },

  "volumenPool": {
    "sichtbarkeit": "intern",
    "pools": [
      { "name": "Datenkörbe", "verwendung": "…", "reset": "persistent — kein Reset", "default": "80 % / 90 %" }
    ],
    "hinweise": [ "Hash-Deduplizierung — Standard für SaaS: …" ]
  },

  "begriffe": [
    { "intern": "Mengenstaffel", "extern": "Benutzer-Pakete", "erlaeuterung": "Preis pro zusätzlichem Voll-Benutzer, sinkt mit der Anzahl." },
    { "intern": "Call-API", "extern": "REST-API", "erlaeuterung": "Seit Oktober 2026 kein Add-on mehr, ab PROFESSIONAL inklusive." },
    { "intern": "Tier", "extern": "Stufe", "erlaeuterung": null },
    { "intern": "Nachschaltseiten", "extern": "Anlage zum Angebot", "erlaeuterung": "Verbindliche Kundendarstellung, Anlage zu jedem Odoo-Angebot." }
  ],

  "vertriebshinweise": [
    { "titel": "Kunden-Master", "text": "Die Nachschaltseiten sind die verbindliche Kundendarstellung …" },
    { "titel": "Mehrjahresverträge", "text": "2 Jahre: … Mit Geschäftsführung abstimmen." }
  ],

  "sicherheit": [
    { "thema": "Cloud Deutschland", "textExtern": "Mehrfach georedundanter Betrieb in Deutschland …", "textIntern": "Zwei RZ in Frankfurt … (Standorte NICHT nach außen)" }
  ]
}
```

## Regeln

- `version` ist eine positive ganze Zahl und steigt bei jeder Erzeugung um 1;
  `stand` hat das Format `JJJJ-MM`, `gueltigAb` das Format `JJJJ-MM-TT`.
- Jede Stufe hat einen eindeutigen `schluessel` (Kleinbuchstaben, ohne
  Leerzeichen); `volumenstufen[].preise` verwendet genau diese Schlüssel.
- `volumenstufen` sind aufsteigend nach `stammdaten` sortiert.
- Jedes Element mit `sichtbarkeit: "intern"` darf in der externen PDF nicht
  vorkommen; umgekehrt muss alles aus der externen PDF in der JSON als
  `extern` stehen. Bitte beim Erzeugen gegenprüfen.
- `sanktionslistenPakete.*.listen` verwendet die Kürzel aus dem
  Sanktionslisten-Wiki der Kundenzentrale (Spalte „Kürzel"): UNSC, CFSP,
  EU-RU, EU-BY, DPL, UVL, EL, MEUL, SDN, Non-SDN, ISN, DDTC, FDA, UFLPA, METI,
  SECO, OFSI, CASL, OeNB, FR-GEL, WBL, UEL, SG-MAS, AUCL, NLNST, BENS, PLSL,
  CZNSL. Damit kann die Kundenzentrale je Liste anzeigen, in welchem Paket sie
  steckt.
- Texte in der JSON sind Fließtext ohne Markdown. Zeilenumbrüche innerhalb
  eines Textes als `\n`.
- UTF-8, keine BOM, eingerückt (2 Leerzeichen), damit Unterschiede zwischen
  Versionen im Diff lesbar sind.

## Ablage und Übergabe

- Alle drei Dateien (zwei PDFs, eine JSON) nach `P:\01_Vertrieb\Preisliste\`
  mit Versionsnummer im Namen. Die Kundenzentrale liest die JSON von dort ein
  (Import durch einen Wiki-Redakteur des Themas „Preisliste") und verlinkt die
  PDFs. Ältere Versionen bleiben liegen — die Kundenzentrale führt die Historie.
- Erste Lieferung bitte als Version 22 (= aktueller Stand Oktober 2026), damit
  die Zählung an die bisherigen Download-Fassungen anschließt.
