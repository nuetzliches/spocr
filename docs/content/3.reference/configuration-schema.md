---
title: Configuration Reference
description: Structure and meaning of fields in spocr.json.
---

# Configuration Reference (`spocr.json`)

## Beispiel

```json
{
  "version": "1",
  "project": {
    "name": "Demo.Data",
    "targetFramework": "net8.0"
  },
  "database": {
    "connection": "Server=.;Database=AppDb;Trusted_Connection=True;",
    "schema": "dbo"
  },
  "generation": {
    "outputDir": "Output",
    "namespaceRoot": "Demo.Data.Generated"
  }
}
```

## Key Fields

| Field                      | Type   | Description                       |
| -------------------------- | ------ | --------------------------------- |
| `version`                  | string | Schema version                    |
| `project.name`             | string | Target project name               |
| `project.targetFramework`  | string | Target .NET TF                    |
| `database.connection`      | string | Database connection string        |
| `database.schema`          | string | Default schema                    |
| `generation.outputDir`     | string | Output directory                  |
| `generation.namespaceRoot` | string | Root namespace for generated code |

## TODO

- Full machine-readable JSON schema to follow.

---

Note: This document was translated from German on 2025-10-02 to comply with the English-only language policy.
