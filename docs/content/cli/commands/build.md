---
title: build
description: Führt die Code-Generierung basierend auf aktueller Konfiguration aus.
versionIntroduced: 4.0.0
experimental: false
authoritative: true
aiTags: [cli, build, generation]
---

# build

Der `build` Befehl generiert alle konfigurierten Artefakte (Input-/Output-Modelle, DbContext Teile, Mappings usw.).

## Verwendung

```bash
spocr build [Optionen]
```

## Optionen (Auszug)

| Option                | Typ    | Beschreibung                                         |
| --------------------- | ------ | ---------------------------------------------------- |
| `--project <name>`    | string | Zielprojekt überschreiben                            |
| `--force`             | flag   | Überschreibt bestehende Dateien falls nötig          |
| `--generators <list>` | string | Komma-liste zur Einschränkung bestimmter Generatoren |
| `--verbose`           | flag   | Ausführlichere Logausgabe                            |

## Behavior Contract (Draft)

```json
{
  "command": "build",
  "inputs": {
    "--project": { "type": "string", "required": false },
    "--force": { "type": "boolean", "required": false },
    "--generators": {
      "type": "string",
      "required": false,
      "format": "comma-list"
    },
    "--verbose": { "type": "boolean", "required": false }
  },
  "outputs": {
    "writes": ["Output/**/*.cs"],
    "console": ["SummaryTable", "Warnings", "Errors"],
    "exitCodes": {
      "0": "Success",
      "1": "ValidationError",
      "2": "GenerationError"
    }
  }
}
```

## Beispiele

```bash
spocr build
spocr build --verbose
spocr build --generators Inputs,Outputs
```
