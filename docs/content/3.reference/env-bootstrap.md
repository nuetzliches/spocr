---
title: Environment Bootstrap & Configuration
description: Environment bootstrap applies starting in v4.5; spocr.json is fully removed in v5. This page documents the model and migration.
layout: docs
---

# Environment Bootstrap & Configuration (v4.5 → v5)

> The bootstrap mechanism (interactive creation of `.env` from `.env.example`) is already ACTIVE in v4.5. The only difference at v5 cutover: `spocr.json` is gone entirely (no fallback) and certain preview flags may become enforceable (strict diff / golden). Until coverage & allow‑list criteria are met, strict enforcement stays disabled.

## Precedence Chain

1. CLI arguments (`spocr generate --mode next --namespace Acme.App.Data`)
2. Process environment (e.g. `set SPOCR_NAMESPACE=Acme.App.Data` before invoking CLI)
3. `.env` file (working directory / sample project root)
4. Legacy `spocr.json` (read‑only fallback for connection string in dual|legacy modes; REMOVED entirely in v5)

Already in v4.5 the generator strongly prefers environment surfaces; v5 completes the transition (zero reliance on `spocr.json`). All active configuration surfaces are explicit and diff‑friendly.

## Bootstrap Workflow

When no `.env` exists the CLI (v4.5+) initiates an interactive bootstrap:

1. Detect absence of `.env` → display migration banner.
2. Offer merge of legacy `spocr.json` (connection string only) into a freshly generated `.env`.
3. Write `.env` using the authoritative template (`.env.example`) preserving comment blocks verbatim.
4. Emphasize manual review of: `SPOCR_NAMESPACE`, `SPOCR_GENERATOR_MODE`, and connection string security.

Repeatable via (planned) command: `spocr env init` (non‑interactive override: `--force` to overwrite existing file while preserving comments). In v4.5 this may still be part of the legacy CLI surface; in v5 it becomes first-class.

## Authoritative Template (`.env.example`)

The template contains structured comment sections. The generator copies sections without mutation. Future removal of deprecated keys uses a line filter that warns but still copies (soft deprecation) until major removal.

### Section Layout (high‑level)

```text
#################################################################################################
# SpocR Bridge Phase / v5 Preview – Authoritative .env Template
# CONFIG PRECEDENCE: CLI > ENV > .env > legacy spocr.json (removed in v5)
#################################################################################################
CORE MODE CONTROL
OPTIONAL FEATURE FLAGS
AUTO-UPDATE CONTROL
DATABASE CONNECTION
NAMESPACE & OUTPUT
SCHEMA SELECTION
TARGET FRAMEWORK HINT
GOLDEN HASH / DIFF (Preview)
JSON / STREAMING (Preview)
ANALYZER / DIAGNOSTICS (Preview)
#################################################################################################
```

### Stable Keys (v4.5 & v5)

| Key                    | Purpose                                                                         | Notes                                                                                          |
| ---------------------- | ------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------- | ------------------------ |
| `SPOCR_GENERATOR_MODE` | Bridge: `dual` default (legacy+vNext). v5: `next` recommended; `legacy` removed | Switch to `next` after parity confidence                                                       |
| `SPOCR_NAMESPACE`      | Root namespace (no `.SpocR` suffix)                                             | Auto-derive fallback EXISTS in v4.5 (directory-based); removed in v5 (must be explicit or CLI) |
| `SPOCR_GENERATOR_DB`   | Metadata pull connection                                                        | Use least-privilege account; only schema read rights                                           |
| `SPOCR_BUILD_SCHEMAS`  | Positive allow-list filtering                                                   | Empty = all except ignored; comma separated list                                               |
| `SPOCR_OUTPUT_DIR`     | Override output folder                                                          | Default `SpocR`; override sparingly                                                            |
| `SPOCR_TFM`            | Target framework hint                                                           | Affects optional endpoint generation / TFM-specific features                                   |
| `SPOCR_NO_UPDATE`      | Skip auto-update check                                                          | Accepts `1                                                                                     | true` (case-insensitive) |

### Deprecated / Removed Keys (Cutover)

The following bridge phase remnants (only present indirectly via `spocr.json`) are removed in v5:

- `Project.Role.Kind`
- `RuntimeConnectionStringIdentifier`
- `Project.Output.*` (all nested properties)

They previously existed only inside `spocr.json` and are superseded by explicit environment variables or generator defaults.

### Preview / Emerging Keys (Inactive Until Criteria Met)

| Key                              | Activation Criterion                       | Description                                                                     |
| -------------------------------- | ------------------------------------------ | ------------------------------------------------------------------------------- |
| `SPOCR_STRICT_DIFF`              | Core coverage ≥60% & allow‑list stabilized | Fails build on unexpected generator diffs (excludes allow‑list)                 |
| `SPOCR_STRICT_GOLDEN`            | Same as strict diff                        | Enforces golden hash manifest across outputs                                    |
| `SPOCR_ENABLE_JSON_DUAL`         | v5 JSON feature release                    | Generates JSON specific invocation methods (raw, deserialize, elements, stream) |
| `SPOCR_ENABLE_ROW_STREAMING`     | Streaming implementation ready             | Enables IAsyncEnumerable row streaming helpers                                  |
| `SPOCR_ENABLE_ANALYZER_WARNINGS` | Analyzer package shipping                  | Emits warnings for duplicate method names / namespace conflicts                 |

### Planned Additions (Evaluation)

| Key                            | Status | Goal                                                       |
| ------------------------------ | ------ | ---------------------------------------------------------- |
| `SPOCR_STRICT_NULLABLE`        | Draft  | Activates nullable enforcement & warnings in vNext outputs |
| `SPOCR_GENERATE_API_ENDPOINTS` | Draft  | Opt‑in generation of minimal API endpoint stubs            |
| `SPOCR_ENABLE_JSON_LAZY_CACHE` | Draft  | Cache deserialized JSON per aggregate instance             |

## Security Guidelines

- Avoid committing secrets: connection strings in shared repos should omit credentials or use integrated security.
- Prefer dedicated SQL login with read metadata permissions only.
- Run `dotnet list package --outdated` monthly to surface vulnerable dependencies.

## Determinism & Golden Hash Integration

Golden hash manifests (SHA256) are written to `debug/golden-hash.json`. Strict validation is **deferred** until coverage & allow‑list stability thresholds are achieved. Environment flags for strict modes must remain disabled in v4.5 (bridge) until criteria satisfied.

## Migration Checklist (v4.5 → v5)

1. Ensure `.env.example` updated to latest comment format.
2. Run `spocr env init` to create or refresh `.env` (avoid manual copy errors).
3. Remove legacy `spocr.json` from active workflows (retain only for historical diff if needed). In v5 it should not exist.
4. Set `SPOCR_GENERATOR_MODE=next` once parity confidence established.
5. Track coverage; enable strict diff/golden once ≥60% threshold met.
6. Adopt JSON / streaming flags after verifying preview stability.

## Example Final v5 `.env` (Minimal)

```dotenv
SPOCR_GENERATOR_MODE=next
SPOCR_NAMESPACE=Acme.App.Data
SPOCR_GENERATOR_DB=Server=localhost;Database=AppDb;Trusted_Connection=True;TrustServerCertificate=True;
SPOCR_BUILD_SCHEMAS=core,identity
SPOCR_TFM=net10.0
```

## Example v5 `.env` (With Preview Flags)

```dotenv
SPOCR_GENERATOR_MODE=next
SPOCR_NAMESPACE=Acme.App.Data
SPOCR_GENERATOR_DB=Server=localhost;Database=AppDb;Trusted_Connection=True;TrustServerCertificate=True;
SPOCR_STRICT_DIFF=1
SPOCR_STRICT_GOLDEN=1
SPOCR_ENABLE_JSON_DUAL=1
SPOCR_ENABLE_ROW_STREAMING=1
SPOCR_ENABLE_ANALYZER_WARNINGS=1
```

> Only enable preview flags in feature branches; avoid polluting main CI until stability criteria are met.

## FAQ

**Q: Why remove spocr.json?**  
A: Environment variables + `.env` are simpler to diff, merge, and reason about; they avoid hidden hierarchical overrides and reduce configuration churn.

**Q: Why keep comments in `.env`?**  
A: They serve as living documentation synchronized with generator capabilities; duplication risk is mitigated by single authoritative template.

**Q: What triggers strict diff enablement?**  
A: Minimum coverage (≥60%) + stable allow‑list file (`.spocr-diff-allow`) with no pending churn >5 consecutive successful runs.

---

Future revisions will expand analyzer scenarios and JSON streaming patterns; track the roadmap for activation windows.
