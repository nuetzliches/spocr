---
title: Environment Bootstrap & Configuration
description: How the SpocR CLI bootstraps `.env`, resolves configuration precedence, and manages preview flags.
layout: docs
version: 5.0
---

# Environment Bootstrap & Configuration

The SpocR CLI relies on environment-first configuration. Each project maintains a checked-in `.env` file generated from the authoritative template. CLI flags or environment variables can override any value at runtime.

## Precedence Chain

1. CLI arguments (e.g. `spocr build --output Generated`)
2. Process environment variables (`SPOCR_NAMESPACE=Acme.App.Data spocr build`)
3. Project `.env`
4. Internal defaults

The legacy `spocr.json` format is no longer consulted for generator settings.

## Bootstrap Workflow

`spocr init` handles initial setup and repeatable updates:

1. Detects absence of `.env` and offers to create one from the template.
2. Copies `.env.example` verbatim, preserving comment blocks and section ordering.
3. Applies inferred values (namespace, schemas, connection string) when provided via flags.
4. Leaves a review notice in the console so you can adjust sensitive entries (connection strings, schema allow-list) before committing.

Re-run `spocr init` at any time; use `--force` to overwrite existing values while keeping comments intact.

## Authoritative Template (`.env.example`)

The template is the single source of truth for available keys. Comment sections are grouped by feature area (core configuration, optional flags, auto-update control, diagnostics). When a key graduates from preview, update the template and this page together.

### Stable Keys

| Key                   | Purpose                                      | Notes                                                            |
| --------------------- | -------------------------------------------- | ---------------------------------------------------------------- |
| `SPOCR_NAMESPACE`     | Root namespace for generated code            | CLI flag `--namespace` overrides                                 |
| `SPOCR_GENERATOR_DB`  | Connection string used during `spocr pull`   | Use least-privilege credentials                                  |
| `SPOCR_BUILD_SCHEMAS` | Positive allow-list of schemas to generate   | Empty or absent = include all discovered schemas                 |
| `SPOCR_OUTPUT_DIR`    | Optional override for code output folder     | Defaults to `SpocR`                                              |
| `SPOCR_TFM`           | Target framework hint for template selection | e.g. `net8.0`, `netstandard2.1`                                  |
| `SPOCR_NO_UPDATE`     | Disables auto-update checks for the CLI      | Accepts `1`, `true`, `yes`, `on` (case-insensitive)              |
| `SPOCR_LOG_LEVEL`     | Controls CLI logging verbosity               | `info` by default; set `debug` for detailed pipeline diagnostics |

### Preview / Emerging Keys

These keys remain disabled until their respective features ship. Keep them documented but emphasize the activation criteria.

| Key                              | Activation Criterion                        | Description                                             |
| -------------------------------- | ------------------------------------------- | ------------------------------------------------------- |
| `SPOCR_STRICT_DIFF`              | Core coverage ≥60% & diff allow-list stable | Fails build on unexpected generator diffs               |
| `SPOCR_STRICT_GOLDEN`            | Same as strict diff                         | Enforces golden hash manifests                          |
| `SPOCR_ENABLE_JSON_DUAL`         | JSON dual-mode release                      | Emits raw + materialized JSON helpers                   |
| `SPOCR_ENABLE_ROW_STREAMING`     | Streaming helpers ready                     | Enables `IAsyncEnumerable`-based row streaming          |
| `SPOCR_ENABLE_ANALYZER_WARNINGS` | Analyzer package shipping                   | Surfaces generator diagnostics as compile-time warnings |
| `SPOCR_STRICT_NULLABLE`          | Nullable enforcement finalized              | Promotes nullable analysis warnings to errors           |
| `SPOCR_GENERATE_API_ENDPOINTS`   | API endpoint templating GA                  | Generates minimal API endpoint stubs                    |

## Security Guidelines

- Never commit secrets. Use integrated authentication or secure secret stores where possible.
- Provision a SQL login with read-only schema access for generator operations.
- Rotate credentials regularly and document the process in team runbooks.

## Determinism & Golden Hash Integration

Golden hash manifests summarize generator output for regression checks. Within the SpocR repository we maintain them under `debug/` for framework development, but consumer projects keep them alongside their configured output directory (default `SpocR/`). Gating remains opt-in until coverage and diff allow-list criteria are met. When you activate `SPOCR_STRICT_GOLDEN`, ensure checklists capture the decision and update automation accordingly.

## Namespace Override Example

Override the namespace via CLI flag or `.env` and inspect the diff to confirm the new root takes effect. The example below captures how `spocr build` responds when `SPOCR_NAMESPACE` changes from `Acme.App` to `Contoso.Billing`.

```diff
--- a/SpocR/SpocRDbContext.cs
+++ b/SpocR/SpocRDbContext.cs
-namespace Acme.App.SpocR;
+namespace Contoso.Billing.SpocR;

-public static class SpocRDbContextServiceCollectionExtensions
+public static class SpocRDbContextServiceCollectionExtensions
```

Tip: run `spocr build --namespace Contoso.Billing` to test overrides without editing the `.env`. When the diff looks correct, update `SPOCR_NAMESPACE` in the project `.env` and commit the change alongside regenerated artifacts.

## Example `.env` (Minimal)

```dotenv
SPOCR_NAMESPACE=Acme.App.Data
SPOCR_GENERATOR_DB=Server=localhost;Database=AppDb;Trusted_Connection=True;TrustServerCertificate=True;
SPOCR_BUILD_SCHEMAS=core,identity
SPOCR_OUTPUT_DIR=SpocR
SPOCR_TFM=net8.0
```

## Example `.env` (With Preview Flags)

```dotenv
SPOCR_NAMESPACE=Acme.App.Data
SPOCR_GENERATOR_DB=Server=localhost;Database=AppDb;Trusted_Connection=True;TrustServerCertificate=True;
SPOCR_BUILD_SCHEMAS=core,identity
SPOCR_STRICT_DIFF=1
SPOCR_STRICT_GOLDEN=1
SPOCR_ENABLE_JSON_DUAL=1
SPOCR_ENABLE_ROW_STREAMING=1
SPOCR_ENABLE_ANALYZER_WARNINGS=1
```

> Only enable preview flags in feature branches. Capture the rationale and rollback plan in the checklist when activating them.

## FAQ

**Why retire `spocr.json`?**

Environment-first configuration offers deterministic precedence, easy diffing, and straightforward automation via CI. The `.env` template keeps context close to the project, while CLI flags simplify one-off overrides.

**Can `.env` live outside the repo?**

Yes. The CLI searches the working directory first, then falls back to parent directories. For containerized or CI scenarios you can supply environment variables directly instead of committing the file.

**How do I refresh `.env` when new keys appear?**

Run `spocr init --force` after pulling the latest template. Review new keys in the diff, decide whether to enable them, and update the checklists accordingly.

---

Future revisions will expand guidance once strict diff, JSON dual mode, and streaming features reach general availability.
