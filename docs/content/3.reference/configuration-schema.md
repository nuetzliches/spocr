---
title: Configuration Reference
description: Structure and meaning of fields in spocr.json.
---

# Configuration Reference (`spocr.json`)

The configuration intentionally avoids persisting database schema state beyond explicit ignore lists. All structural metadata for generation (procedures, result sets, UDTTs, table column typings) is sourced from fingerprinted snapshot files under `.spocr/schema/`.

## Minimal Example

```jsonc
{
  "version": "4.1.0",
  "targetFramework": "net8.0",
  "project": {
    "role": { "kind": "Default" },
    "dataBase": {
      "connectionString": "Server=.;Database=AppDb;Trusted_Connection=True;",
    },
    "output": {
      "namespace": "Demo.Data.Generated",
      "dataContext": { "path": "src/DataContext" },
    },
    "defaultSchemaStatus": "Build",
    "ignoredSchemas": ["audit"],
    "ignoredProcedures": ["audit.CleanupJob"],
    "jsonTypeLogLevel": "SummaryOnly",
  },
}
```

## Field Reference

| Path                                | Type                    | Description                                                                                                         |
| ----------------------------------- | ----------------------- | ------------------------------------------------------------------------------------------------------------------- |
| `version`                           | string (semver)         | Configuration file version (tool interprets via migration rules)                                                    |
| `targetFramework`                   | string                  | Target TF for generated code (e.g. `net8.0`)                                                                        |
| `project.role.kind`                 | enum                    | Role strategy (affects generation surface)                                                                          |
| `project.dataBase.connectionString` | string                  | SQL Server connection string used for `pull` and snapshot creation                                                  |
| `project.output.namespace`          | string                  | Root namespace for generated artifacts                                                                              |
| `project.output.dataContext.path`   | string                  | Relative path where generated context/models are written                                                            |
| `project.defaultSchemaStatus`       | enum (`Build`/`Ignore`) | Default treatment for discovered DB schemas (controls which procedures are considered). Not persisted in snapshots. |
| `project.ignoredSchemas[]`          | string[]                | Explicit schema names to ignore (case-insensitive)                                                                  |
| `project.ignoredProcedures[]`       | string[]                | Fully-qualified procedure names (`schema.name`) to ignore even if the schema is built                               |
| `project.jsonTypeLogLevel`          | enum                    | JSON typing log verbosity: `Detailed`, `SummaryOnly`, `Off` (affects `[json-type-*]` & some `[proc-*]` verbosity)   |

## Design Principles

1. Snapshot Single Source: Only snapshots contain procedure & type metadata, never the config file.
2. Proc-Only Cache: Local cache controls whether procedure definitions are re-fetched; types (UDTTs, table columns) are always refreshed for cross-schema correctness.
3. Minimal Persistence: Ignored lists are additive and explicit; no historic per-schema status tracking.
4. Deterministic Generation: A changed snapshot fingerprint implies a real metadata change (procedures added/removed/modified, UDTT signature changes, parser version bump).

## Parser & Typing

| Version | Change                                                                                         |
| ------- | ---------------------------------------------------------------------------------------------- |
| v2      | Removed derived `HasJson` / `ResultSetCount`, filtered placeholder result sets                 |
| v3      | Two-stage JSON column typing (UDTT + base table columns) + guaranteed fallback `nvarchar(max)` |
| v4      | Fallback upgrade: previously unresolved JSON columns re-evaluated & upgraded to concrete types |

## Legacy `schema` Node Removal

The legacy `schema` array is no longer written. Any old entries should be removed; ignore intent is now expressed solely via `ignoredSchemas` / `ignoredProcedures`.

## Validation Tips

- Use `spocr pull --no-cache --verbose` for full diagnostics.
- Switch `jsonTypeLogLevel` to `Detailed` when inspecting per-column table matches or upgrades.
- Use `SummaryOnly` (recommended default) for concise per-proc + run summary.
- Set `Off` to suppress JSON typing logs entirely (still guarantees typing behavior).

## Future Additions

- Machine-readable JSON schema export.
- CLI command `spocr config validate` for static schema probing.
