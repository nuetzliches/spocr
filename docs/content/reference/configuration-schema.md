---
title: Konfigurations-Referenz
description: Struktur und Bedeutung der Felder der spocr.json.
---

# Konfigurations-Referenz (`spocr.json`)

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
    "namespaceRoot": "Demo.Data.Generated",
    "includeJsonProcedures": true
  }
}
```

## Wichtige Felder

| Feld                               | Typ    | Beschreibung           |
| ---------------------------------- | ------ | ---------------------- |
| `version`                          | string | Schema-Version         |
| `project.name`                     | string | Zielprojektname        |
| `project.targetFramework`          | string | Ziel .NET TF           |
| `database.connection`              | string | Verbindung zur DB      |
| `database.schema`                  | string | Default Schema         |
| `generation.outputDir`             | string | Ausgabeordner          |
| `generation.namespaceRoot`         | string | Wurzel-Namespace       |
| `generation.includeJsonProcedures` | bool   | JSON Procs einbeziehen |

## TODO

- Vollständiges maschinenlesbares JSON Schema folgt.
