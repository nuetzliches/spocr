---
title: sp Command
description: List and inspect stored procedures configured in spocr.json.
---

# sp Command

Der `sp` Befehl bündelt Operationen rund um konfigurierte Stored Procedures.

> Hinweis (Alpha): Der JSON Stored Procedure Parser befindet sich im Alpha-Status. Die Ausgabe von `sp ls --json` reflektiert den aktuell bekannten Stand aus `spocr.json`. Typ-Upgrades können nach erneutem `pull` ohne Warnung erfolgen.

## Unterbefehle

| Subcommand | Beschreibung |
| ---------- | ------------ |
| `ls`       | Listet Stored Procedures eines Schemas als JSON |

## Optionen (global + sp-spezifisch)

| Option | Beschreibung |
| ------ | ------------ |
| `--schema <name>` | Name des Schemas, dessen Stored Procedures gelistet werden sollen (Pflicht für `ls`) |
| `--json` | Erzwingt reine JSON-Ausgabe (unterdrückt Warnungen) |
| `--quiet` | Unterdrückt Standardausgabe (leere JSON-Liste bleibt) |
| `--verbose` | Zusätzliche Detailausgaben (Warnings werden bei `--json` weiterhin unterdrückt) |

## Ausgabeformat

`sp ls --schema demo --json` gibt ein JSON-Array zurück:

```json
[
  { "name": "UserFind" },
  { "name": "UserList" }
]
```

Leerresultate (Schema fehlt oder keine Stored Procedures) resultieren in:

```json
[]
```

## Beispiele

```bash
# Liste aller Stored Procedures des Schemas "core"
spocr sp ls --schema core --json

# Menschlich lesbare Ausgabe (ohne --json):
spocr sp ls --schema reporting

# Unterdrücke Warnung bei leerem Ergebnis
spocr sp ls --schema foo --json --quiet
```

## Verhalten & Exit Codes

| Situation | Output | Exit Code |
| --------- | ------ | --------- |
| Erfolgreiche Liste (>=1 Eintrag) | JSON-Array mit Objekten | 0 |
| Leeres Ergebnis / Schema fehlt | `[]` | 1 (Aborted) |
| Config nicht vorhanden | `[]` | 1 (Aborted) |

> Hinweis: Exit Code 1 für leere Ergebnisse ist identisch zum bisherigen Verhalten und kann in zukünftigen Versionen ggf. auf 0 angepasst werden (Feedback willkommen).

## Änderungen ab Version (pending)

- Korrigierte Schreibweise: `StoredProcedure` (vorher: `StoredProcdure`).
- Neue Option `--json` für maschinenlesbare Ausgabe.
- Immer valides JSON für `ls`.
