---
title: pull
description: Synchronizes stored procedures & schema from the database.
versionIntroduced: 4.0.0
experimental: false
authoritative: true
aiTags: [cli, pull, sync]
---

# pull

Synchronizes stored procedures, parameters, and schema metadata into `.spocr/` using the connection defined in your `.env` file.

## Usage

```bash
spocr pull [options]
```

## Configuration

- `SPOCR_GENERATOR_DB` **must** be set in `.env`. Run `spocr init` to scaffold the key or update it manually.
- Optional allow-list filters come from `.env` (`SPOCR_BUILD_SCHEMAS=core,identity`).
- Use `SPOCR_BUILD_PROCEDURES` (set via `--procedure`) to target specific stored procedures when triaging issues.

If the connection string is missing or empty, the command fails fast with guidance to update `.env`.

## Options

| Option | Description |
| ------ | ----------- |
| `-p, --path <dir>` | Point to a different project root that already contains `.env`. |
| `--no-cache` | Ignore existing `.spocr/cache` entries and force a full re-parse. |
| `--procedure <schema.proc>` | Comma-separated filter that maps to `SPOCR_BUILD_PROCEDURES` for the current run. |
| `-v, --verbose` | Emit per-procedure progress, timings, and cache reuse hints. |
| `--debug` | Use the debug environment wiring (mirrors the legacy switch in v5). |

When `--no-cache` is specified you will only see `[proc-loaded]` entries (no `[proc-skip]`) and the banner `[cache] Disabled (--no-cache)`. Use this after modifying parsing/JSON heuristics or when validating metadata changes.

## Behavior Contract (Draft)

```json
{
  "command": "pull",
  "reads": [".env"],
  "writes": [".spocr/schema/**/*.json", ".spocr/cache/*.json"],
  "exitCodes": {
    "0": "Success",
    "1": "ValidationError",
    "2": "ExtractionError"
  }
}
```

## Examples

```bash
# Standard metadata refresh using the current directory
spocr pull

# Force fresh parsing while diagnosing snapshot issues
spocr pull --no-cache --verbose

# Run against the debug sandbox and inspect only the identity schema procedures
spocr pull -p debug --procedure identity.%
```
