---
title: init
description: Initialize a SpocR project using the .env based configuration model.
versionIntroduced: 5.0.0
experimental: false
authoritative: true
authors: [core]
aiTags: [cli, init, env]
---

# init

Bootstraps a `.env` file (or updates an existing one) with core SPOCR\_\* keys.

## Usage

```bash
spocr init [options]
```

## Options

| Flag                     | Description                     | Maps To               | Notes                                   |
| ------------------------ | ------------------------------- | --------------------- | --------------------------------------- |
| `-p, --path <dir>`       | Target directory (defaults CWD) | n/a                   | Directory must exist or will be created |
| `-n, --namespace <name>` | Root namespace                  | `SPOCR_NAMESPACE`     | Required (no implicit fallback)         |
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
  "reads": [".env", ".env.example"],
  "exitCodes": { "0": "Success", "2": "WriteFailure" },
  "sideEffects": ["Preserves unknown SPOCR_* keys and updates provided values"]
}
```

## Examples

```bash
# Minimal non-interactive initialization
spocr init --namespace Acme.Product.Data --connection "Server=.;Database=AppDb;Trusted_Connection=True;TrustServerCertificate=True;"

# Allow-list schemas and force recreate .env
spocr init -n Acme.Product.Data -c "Server=.;Database=AppDb;Trusted_Connection=True;" -s core,identity --force
```

## Notes

- The command is safe to run multiple times; it only updates specified keys.
- Unknown SPOCR\_\* keys in existing `.env` are preserved verbatim.
- Follow up with `spocr pull` to refresh metadata before building or testing.

## See Also

- [Environment Bootstrap & Configuration](../../3.reference/env-bootstrap.md)
