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

### Important Options

| Option            | Description                                                                   |
| ----------------- | ----------------------------------------------------------------------------- |
| `--schema <name>` | Limit to a single schema (repeatable).                                        |
| `--verbose`       | Emit detailed per-procedure load / heuristic logs.                            |
| `--no-cache`      | Force a full re-parse of every stored procedure (ignore & don't write cache). |

When `--no-cache` is specified you will only see `[proc-loaded]` entries (no `[proc-skip]`) and a banner `[cache] Disabled (--no-cache)`. Use this after modifying parsing/JSON heuristics or when validating metadata changes.

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
spocr pull --connection "Server=.;Database=AppDb;Trusted_Connection=True;" --no-cache --verbose

---
Note: This document was translated from German on 2025-10-02 to comply with the English-only language policy.
```
