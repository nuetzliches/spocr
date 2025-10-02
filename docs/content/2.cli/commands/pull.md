---
title: pull
description: Synchronizes stored procedures & schema from the database.
versionIntroduced: 4.0.0
experimental: false
authoritative: true
aiTags: [cli, pull, sync]
---

# pull

Reads metadata (stored procedures, parameters, optionally tables) from a SQL Server database and updates internal models.

## Usage

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

## Examples

```bash
spocr pull --connection "Server=.;Database=AppDb;Trusted_Connection=True;"
spocr pull --connection "Server=.;Database=AppDb;Trusted_Connection=True;" --schema custom

---
Note: This document was translated from German on 2025-10-02 to comply with the English-only language policy.
```
