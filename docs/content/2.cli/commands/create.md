---
title: create
description: Initialisiert SpocR Konfiguration und Verzeichnisstruktur.
versionIntroduced: 4.0.0
experimental: false
authoritative: true
aiTags: [cli, create, init]
---

# create

Initialisiert ein Projekt für die Nutzung mit SpocR und erzeugt u.a. eine `spocr.json`.

## Verwendung

```bash
spocr create [Optionen]
```

## Behavior Contract (Draft)

```json
{
  "command": "create",
  "inputs": {},
  "outputs": {
    "writes": ["spocr.json"],
    "console": ["CreatedConfig"],
    "exitCodes": { "0": "Success", "1": "AlreadyExists" }
  }
}
```

## Beispiele

```bash
spocr create --project Demo.Data
```
