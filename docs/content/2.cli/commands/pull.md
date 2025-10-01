---
title: pull
description: Synchronisiert Stored Procedures & Schema aus der Datenbank.
versionIntroduced: 4.0.0
experimental: false
authoritative: true
aiTags: [cli, pull, sync]
---

# pull

Liest Metadaten (Stored Procedures, Parameter, ggf. Tabellen) aus einer SQL Server Datenbank und aktualisiert interne Modelle.

## Verwendung

```bash
spocr pull --connection "<connection-string>" [Optionen]
```

## Behavior Contract (Draft)

```json
{
  "command": "pull",
  "inputs": {
    "--connection": { "type": "string", "required": true },
    "--schema": { "type": "string", "required": false },
    "--verbose": { "type": "boolean", "required": false }
  },
  "outputs": {
    "writes": ["Schema Cache"],
    "console": ["Summary", "Warnings", "Errors"],
    "exitCodes": {
      "0": "Success",
      "1": "ConnectionFailed",
      "2": "ExtractionError"
    }
  }
}
```

## Beispiele

```bash
spocr pull --connection "Server=.;Database=AppDb;Trusted_Connection=True;"
spocr pull --connection "Server=.;Database=AppDb;Trusted_Connection=True;" --schema custom
```
