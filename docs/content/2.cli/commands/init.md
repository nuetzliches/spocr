---
title: init
description: Initialize a SpocR project using the .env based configuration model (replaces legacy create).
versionIntroduced: 5.0.0
experimental: false
authoritative: true
authors: [core]
aiTags: [cli, init, env]
---

# init

Bootstraps a `.env` file (or updates an existing one) with core SPOCR\_\* keys. This replaces the legacy `create` command that generated `spocr.json`.

## Usage

```bash
spocr init [options]
```

## Options

| Flag                     | Description                     | Maps To               | Notes                                   |
| ------------------------ | ------------------------------- | --------------------- | --------------------------------------- | ---------------------- | ------------------- |
| `-p, --path <dir>`       | Target directory (defaults CWD) | n/a                   | Directory must exist or will be created |
| `-n, --namespace <name>` | Root namespace                  | `SPOCR_NAMESPACE`     | Required in v5 (fallback removed)       |
| `-m, --mode <mode>`      | Generator mode (`legacy         | dual                  | next`)                                  | `SPOCR_GENERATOR_MODE` | v5 default = `next` |
| `-c, --connection <cs>`  | Metadata pull connection string | `SPOCR_GENERATOR_DB`  | Use least-privilege account             |
| `-s, --schemas <list>`   | Comma separated allow-list      | `SPOCR_BUILD_SCHEMAS` | Example: `core,identity`                |
| `-f, --force`            | Overwrite existing `.env`       | n/a                   | Recreates from template                 |
| `-h, --help`             | Show help                       | n/a                   |                                         |

## Behavior Contract

```json
{
  "command": "init",
  "idempotent": true,
  "writes": [".env"],
  "reads": [".env.example", "spocr.json (optional, migration only)"],
  "exitCodes": { "0": "Success", "2": "WriteFailure" },
  "sideEffects": [
    "May prefill SPOCR_BUILD_SCHEMAS from legacy spocr.json or snapshot"
  ],
  "deprecates": "create"
}
```

## Examples

```bash
# Minimal (interactive off; uses defaults for unspecified keys)
spocr init --namespace Acme.Product.Data --connection "Server=.;Database=AppDb;Trusted_Connection=True;TrustServerCertificate=True;" --mode next

# Allow-list schemas and force recreate .env
spocr init -n Acme.Product.Data -c "Server=.;Database=AppDb;Trusted_Connection=True;" -s core,identity --force
```

## Migration From `create`

1. Run `spocr init` (will create or update `.env`).
2. Remove legacy `spocr.json` or keep read-only until cutover.
3. Adjust keys (namespace, schemas) as needed.
4. Execute `spocr pull` then `spocr build` / `spocr rebuild`.

## Notes

- The command is safe to run multiple times; it only updates specified keys.
- Unknown SPOCR\_\* keys in existing `.env` are preserved verbatim.
- In bridge phase (v4.5) it can still backfill from `spocr.json`; post-cutover it will ignore that file.

## See Also

- [Environment Bootstrap & Configuration](../../3.reference/env-bootstrap-v5.md)
- [create (deprecated)](./create.md)
