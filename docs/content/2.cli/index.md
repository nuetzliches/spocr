---
title: CLI Übersicht
description: Überblick über die SpocR Kommandozeilenbefehle und globale Optionen.
---

# CLI Übersicht

Die SpocR CLI stellt Befehle zur Projektinitialisierung, Synchronisation und Code-Generierung bereit.

## Globale Optionen (Auszug)

| Option      | Beschreibung      |
| ----------- | ----------------- |
| `--help`    | Hilfe anzeigen    |
| `--verbose` | Ausführliche Logs |

## Kernbefehle

| Befehl    | Zweck                                           |
| --------- | ----------------------------------------------- |
| `create`  | Initialisiert Projektstruktur und Konfiguration |
| `pull`    | Liest Stored Procedures & Schema aus Datenbank  |
| `build`   | Führt Codegenerierung aus                       |
| `rebuild` | Löscht und generiert neu                        |
| `remove`  | Entfernt generierte Artefakte                   |
| `version` | Zeigt Version an                                |
| `config`  | Verwaltung der `spocr.json`                     |
| `project` | Projektbezogene Operationen                     |
| `schema`  | Arbeiten mit DB-Schema                          |
| `sp`      | Einzelne Stored Procedure Operationen           |

## Beispiele

```bash
spocr build --verbose
spocr pull --connection "Server=.;Database=AppDb;Trusted_Connection=True;"
```
