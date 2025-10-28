---
title: Configuration Reference
description: v5 configuration precedence and SPOCR_* environment keys.
---

# Configuration Reference (v5)

The modern CLI sources all generator settings from environment variables and the `.env` file located at the project root. Legacy JSON configuration (`spocr.json`) is ignored and remains in the repository only for historical reference. This page captures the desired steady-state precedence and supported keys.

## Precedence

1. CLI overrides (e.g. `spocr pull --path <dir>` with explicit options)
2. Process environment variables (`SPOCR_*`)
3. `.env` file in the target project directory

If a value is not present in any of these layers the CLI uses built-in defaults. No generator behavior reads configuration values from JSON files.

## Required Keys

| Key                  | Purpose                                               | Notes                                                  |
| -------------------- | ----------------------------------------------------- | ------------------------------------------------------ |
| `SPOCR_GENERATOR_DB` | Connection string used for metadata pulls and builds | Required. Must be stored in `.env` or environment not checked into source control. |

## Optional Keys

| Key                | Purpose                                                                 | Default behavior when unset                  |
| ------------------ | ----------------------------------------------------------------------- | -------------------------------------------- |
| `SPOCR_NAMESPACE`  | Overrides the inferred root namespace for generated artefacts           | Namespace inferred from project/assembly name |
| `SPOCR_OUTPUT_DIR` | Customizes the relative output directory for generated artefacts        | `SpocR`                                      |
| `SPOCR_BUILD_SCHEMAS` | Comma-separated allow list of schemas to include during generation | Empty list → all schemas except ignored defaults |
| `SPOCR_NO_UPDATE` / `SPOCR_SKIP_UPDATE` | Disables auto-update prompts when set             | Prompts remain enabled                       |
| `SPOCR_VERBOSE`    | Emits additional diagnostics when set to `1`                            | Standard informational logging               |

> Keep `.env` under source control only when it contains non-sensitive values. Connection strings and credentials belong in secrets management.

## File Layout

- `.env` lives at the repository root (or project directory when using `--path`).
- `samples/restapi/.env.example` provides the canonical template for bootstrapping new environments.
- Generator outputs are written beneath `SpocR/` by default; change with `SPOCR_OUTPUT_DIR` if required.

## Legacy JSON Configuration

- `spocr.json` is treated as a legacy artefact. The CLI warns when the file is present so teams know to migrate values into `.env`.
- Schema metadata lives exclusively under `.spocr/schema/` (fingerprinted snapshots). The generator never persists metadata inside JSON configuration files.
- Historical documentation for `spocr.json` has moved to the legacy documentation stream; new features do not extend the legacy file format.

## Validation Checklist

- `.env` exists in every project that runs the v5 CLI and contains at least `SPOCR_GENERATOR_DB`.
- CI and local scripts rely on environment variables / `.env` exclusively—no automation reads from `spocr.json`.
- Legacy configuration files are retained only when needed for archival purposes and are no longer treated as inputs.
