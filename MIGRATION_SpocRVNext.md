# Migration to SpocRVNext (Draft)

> Status: Draft – references EPICs E001–E013 in `CHECKLIST.md`.

## Goals

- Transition from legacy DataContext generator to SpocRVNext
- Dual generation until v5.0
- Remove legacy code in v5.0 following cutover plan

## Phases

1. Freeze legacy (E001)
2. Core structure & dual generation (E003/E004)
3. Template engine & modernization (E005/E006/E007/E009)
4. Configuration cleanup & documentation (E008/E012)
5. Cutover & obsolete markings (E010/E011)
6. Test hardening & release preparation (E013 + release tasks)

## Configuration Changes

Removed (planned / already removed):

- `Project.Role.Kind`
- `Project.Role.DataBase.RuntimeConnectionStringIdentifier`
- `Project.Output`

### Upcoming (vNext) Configuration Model

- Transitioning from `spocr.json` to environment variable / `.env` driven configuration for runtime & generation parameters.
- Phase-in: BOTH `spocr.json` (legacy) and `.env` are read during v4.x. Full removal of JSON fallback happens in v5.0.
- Rationale: Simplify deployment, enable secret-less container usage, reduce JSON schema churn.
- Precedence order (current draft): CLI flag > Environment variable > `.env` file > (legacy) `spocr.json` (fallback until v5.0 only).
- `spocr pull` no longer overwrites local configuration (it may still read schema & augment in-memory state).
- Migration path: Existing keys map to `SPOCR_*` variables (mapping table to be added). Users can gradually mirror required values into `.env`.
- Example template file now lives at `samples/restapi/.env.example` (moved from repository root for clarity).

### Example `.env` (Draft)

```
# Generator mode (legacy|dual|next)
SPOCR_GENERATOR_MODE=dual
# Connection string identifier (replaces Project.Role.DataBase.RuntimeConnectionStringIdentifier)
SPOCR_DB_DEFAULT=Server=...;Database=...;Trusted_Connection=True;
# Optional namespace override
SPOCR_NAMESPACE=MyCompany.Project.Data
```

### Legacy Key Mapping (Draft)

| Legacy `spocr.json` Path                             | Status v4.x       | Env Variable Replacement | Notes                                                          |
| ---------------------------------------------------- | ----------------- | ------------------------ | -------------------------------------------------------------- |
| `Project.Role.Kind`                                  | Deprecated        | (none)                   | Remove; default behavior assumed                               |
| `Project.DataBase.RuntimeConnectionStringIdentifier` | Deprecated        | `SPOCR_DB_DEFAULT`       | Direct connection now provided; identifier indirection removed |
| `Project.DataBase.ConnectionString`                  | Active (fallback) | `SPOCR_DB_DEFAULT`       | Move value verbatim; secrets should be managed outside VCS     |
| `Project.Output.Namespace`                           | Active            | `SPOCR_NAMESPACE`        | Optional; auto discovery if omitted                            |
| `Project.Output.*.Path`                              | Planned removal   | (TBD)                    | Will move to opinionated defaults; override strategy TBD       |
| `Version`                                            | Informational     | (none)                   | Not required; tool version from assembly/MinVer                |
| `TargetFramework`                                    | Informational     | (none)                   | Multi-TFM handled by project; no env mapping                   |

Unlisted keys remain legacy-only for now; if still needed they will be either:

1. Promoted to explicit `SPOCR_*` variable (documented here), or
2. Dropped with deprecation notice prior to v5.0.

Mapping table last updated: 2025-10-12.

### CLI Help (Draft Excerpt)

```
spocr generate [--mode <legacy|dual|next>] [--output <dir>] [--no-validation]

Environment:
  SPOCR_GENERATOR_MODE   Overrides generator selection if --mode omitted.
  SPOCR_NAMESPACE        Explicit namespace root (optional).
  SPOCR_DB_DEFAULT       Default runtime connection (not persisted by pull).

Notes:
  - In dual mode both legacy and next outputs are produced.
  - In v5.0 default mode changes from 'dual' to 'next'.
  - `spocr.json` fallback removed in v5.0; warnings introduced shortly before removal.

### Experimental CLI Flag

- The next generation CLI (System.CommandLine) is gated behind `SPOCR_EXPERIMENTAL_CLI=1`.
- When enabled, it intercepts execution first and provides early commands (currently: `generate-demo`).
- Fallback to legacy McMaster CLI occurs automatically if flag unset or command not recognized.
- Goal: Incremental migration with low risk, gather feedback before full command parity.
```

## Namespace Resolution

Automatic derivation from project root & (if present) explicit overrides.
Fallback: Default namespace `SpocR.Generated` (tbd).

## Dual Generation

- Legacy output remains deterministic (no refactoring)
- New output is generated in parallel for observability (not to enforce bit‑for‑bit parity)
- Principle: Quality & improved design of new output > strict non‑breaking parity. Breaking changes are acceptable when documented & justified.
- Guard rails / Tasks (updated):
  - Feature flag: env `SPOCR_GENERATOR_MODE=dual|legacy|next` (default in v4.5 = `dual`)
  - CLI flag: `spocr generate --mode <mode>` (falls back to env)
  - Idempotency focus applies only to each generator individually (legacy determinism, new determinism) – no requirement for cross-generator identical hash
  - Diff report may highlight differences but is informational; CI must NOT fail solely on semantic/structural improvements
  - Allow‑list file `.spocr-diff-allow` (glob) optional; can silence known churn while refactoring internals (purely advisory now)
  - Document every intentional breaking change in CHANGELOG (Added/Changed/Removed/Deprecated) and migration guide
  - Failure policy: CI fails if new files missing or structural drift detected (hash mismatch & not in allow-list)
  - Logging: Summarize counts (added/removed/changed) with exit code mapping

## Tests

- Focus: Functional correctness and stability of new generator (not legacy parity)
- Regression tests for removed heuristics (ensure no unintended fallback regressions)
- Coverage target: >= 80% core components (template engine, parser, orchestrator)

## Risks & Mitigation

| Risk                        | Description                       | Mitigation                                |
| --------------------------- | --------------------------------- | ----------------------------------------- |
| Legacy vs. next divergence  | Undetected behavioral differences | Automated diff step in CI                 |
| Unclear migration           | Users confused about steps        | This guide + README / ROADMAP updates     |
| Template engine limitations | Missing features => workarounds   | Early scope definition + extension points |
| Namespace mis-resolution    | Wrong namespaces break builds     | Fallback + logging + tests                |

## Cutover (v5.0)

- Remove legacy generator & output
- Move stable next modules into `src/`
- Remove obsolete markers

## Decisions (Resolved Former Open Points)

### Namespace Strategy

- Primary resolution: Use the assembly root namespace derived from the main project file (csproj `RootNamespace` if present, otherwise project file name).
- Per-module override mechanism (future): Optional key in config `generation.namespaceOverride` (scoped) – not required for initial release.
- Fallback: `SpocR.Generated` only if resolution fails (should be extremely rare and treated as warning).

### CLI Flags (Generator Mode)

- Single flag: `--mode <legacy|dual|next>` on `spocr pull/build/rebuild`.
- Environment variable precedence: `SPOCR_GENERATOR_MODE` used if flag omitted.
- Default (4.5 prerelease series): `dual`.
- Default (5.0+ after cutover): `next` (legacy path removed).

### Template Engine Scope (Initial)

- Supported: simple placeholders `{{ Name }}` and dotted access `{{ Outer.Inner }}`.
- Not supported initially: conditionals, loops, partial includes, inline expressions.
- Extension plan: Introduce directive syntax `{{# each X }}` or `{{# if X }}` only after stability (post v5.0) – avoid premature complexity.

### Breaking Change Policy

- Breaking changes permitted when they improve correctness, simplicity, or extensibility.
- Required documentation: CHANGELOG entry (Changed/Removed/Deprecated) + migration note with rationale and replacement path.
- Avoid silent semantic drift: if behavior changes (e.g. naming scheme) emit compile-time doc comment hint or CLI warning first (grace period where feasible).

### Diff / Observability Policy

- Diff reports are informational; they do not gate merges unless integrity violations occur (missing expected output, non-deterministic generation, unhandled exceptions).
- Allow-list only suppresses known transient noise (formatting, comment headers) – not a substitute for documenting breaking changes.
- Hashing used to prove intra-generator determinism; no cross-generator hash comparison mandated.

### Future Enhancements (Tracked Separately)

- Rich template directives (loops/conditionals)
- Pluggable formatting pipelines (code style normalization)
- Partial templates & macro system

Last updated: 2025-10-12 (decisions section added)
Last updated: 2025-10-12
