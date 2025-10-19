---
title: create (deprecated)
description: Legacy initialization command that generated spocr.json. Replaced by `spocr init` in v5.
versionIntroduced: 4.0.0
deprecatedSince: 5.0.0
experimental: false
authoritative: false
aiTags: [cli, create, deprecated]
---

# create (deprecated)

This command is part of the legacy configuration model that writes a `spocr.json`.  
Starting with v5, use **`spocr init`** which bootstraps a `.env` based configuration (no `spocr.json`).

## Replacement

```bash
spocr init --namespace Acme.App.Data --connection "Server=.\SQLEXPRESS;Database=AppDb;Trusted_Connection=True;TrustServerCertificate=True;" --mode next
```

The new `init` command:

| Flag           | Maps To                | Notes                                         |
| -------------- | ---------------------- | --------------------------------------------- |
| `--namespace`  | `SPOCR_NAMESPACE`      | Required (auto-derive fallback removed in v5) |
| `--connection` | `SPOCR_GENERATOR_DB`   | Metadata pull connection                      |
| `--mode`       | `SPOCR_GENERATOR_MODE` | v5 default = `next`                           |
| `--schemas`    | `SPOCR_BUILD_SCHEMAS`  | Comma separated allow-list                    |
| `--force`      | overwrite .env         | Recreates file from template                  |

`spocr init` is idempotent: re-running updates or appends only the targeted keys.

## Deprecation Behavior Contract

```json
{
  "command": "create",
  "status": "deprecated",
  "replacement": "init",
  "writes": ["spocr.json"],
  "removedIn": "v6? (tentative)",
  "exitCodes": { "0": "Success", "1": "AlreadyExists" }
}
```

## Legacy Examples (for historical reference)

```bash
spocr create --project Demo.Data   # legacy path (will emit deprecation notice in v5)
```

## Migration

1. Run `spocr init` in project root.
2. Delete obsolete `spocr.json` if present.
3. Commit `.env.example` (keep personal `.env` untracked) and add needed SPOCR\_\* keys.
4. Use `spocr pull` + `spocr build` to regenerate outputs.
