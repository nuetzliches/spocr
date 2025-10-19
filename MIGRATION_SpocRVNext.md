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

## Deprecation Timeline (v4.5 → v5.0)

| Item                                                      | 4.5 Status          | Action Now                                            | Removed in 5.0 | Notes                                      |
| --------------------------------------------------------- | ------------------- | ----------------------------------------------------- | -------------- | ------------------------------------------ |
| Legacy DataContext Generator                              | FROZEN              | Only security / stability fixes                       | Yes            | Dual mode ends; only new generator remains |
| `Project.Role.Kind`                                       | Deprecated          | Delete from `spocr.json`                              | Yes            | Implicit default behavior                  |
| `Project.Role.DataBase.RuntimeConnectionStringIdentifier` | Deprecated          | Replace with direct `AddSpocRDbContext` configuration | Yes            | Removes indirection                        |
| `Project.Output.*` (paths)                                | Deprecated (phased) | Minimize usage; prefer defaults + auto namespace      | Yes            | Simplifies layout                          |
| `Project.Output.Namespace`                                | Transitional        | Keep or migrate to `SPOCR_NAMESPACE`                  | Yes            | Auto namespace becomes default             |
| JSON heuristic flags (`Project.Json.*`)                   | Deprecated          | Ignored                                               | Yes            | Unified ResultSets model                   |
| Coverage badge (public)                                   | Deferred            | Do not implement yet                                  | Re-evaluate    | After stable ≥80% core coverage gate       |

Note: "Removed" means loader/parser will ignore & no longer bind; warning verbosity may increase shortly before v5.

> Upcoming (v5) Removal of `spocr.json` Dependency: In v4.5 the file is still read as a fallback (only when `SPOCR_GENERATOR_DB` is absent). In v5.0 the generator will neither read nor parse `spocr.json`. If the file still exists a one‑time WARNING will be emitted advising to delete it (fully .env / ENV driven operation). No feature will rely on its contents post‑cutover.

## Configuration Changes

Removed (planned / already removed):

- `Project.Role.Kind`
- `Project.Role.DataBase.RuntimeConnectionStringIdentifier` (no replacement ENV; runtime connection only via host `AddSpocRDbContext` options)
- `Project.Output` (path steering being phased out in favor of fixed layout + auto namespace)

### Upcoming (vNext) Configuration Model

- Transitioning from `spocr.json` to environment variable / `.env` driven configuration for runtime & generation parameters.
- Phase-in: BOTH `spocr.json` (legacy) and `.env` are read during v4.x. Full removal of JSON fallback happens in v5.0.
- v5 behavior: Presence of `spocr.json` only triggers a warning (no parsing, no fallback). Safe to delete once `.env` / ENV contains required `SPOCR_*` keys.
- Rationale: Simplify deployment, enable secret-less container usage, reduce JSON schema churn.
- Precedence order (current draft): CLI flag > Environment variable > `.env` file > (legacy) `spocr.json` (fallback until v5.0 only).
- Precedence order (updated): CLI flag > Environment variable > `.env` file > (legacy) `spocr.json` fallback (only used in dual|next if `SPOCR_GENERATOR_DB` is absent; ignored when `SPOCR_GENERATOR_DB` is present). Will be removed entirely in v5.0.
- `spocr pull` no longer overwrites local configuration (it may still read schema & augment in-memory state).
- Migration path: Existing keys map to `SPOCR_*` variables (mapping table to be added). Users can gradually mirror required values into `.env`.
- Example template file now lives at `samples/restapi/.env.example` (moved from repository root for clarity).

### Example `.env` (Generator Scope Only – Draft)

```
# Generator mode (legacy|dual|next) – generation scope only
SPOCR_GENERATOR_MODE=dual
# Optional namespace override (if auto namespace not desired)
SPOCR_NAMESPACE=MyCompany.Project.Data

# IMPORTANT: No runtime connection strings in .env – configure via:
# builder.Services.AddSpocRDbContext(o => o.ConnectionString = ...);
# (host configuration: appsettings.json / secrets / host environment)
```

### Legacy Key Mapping (Draft – updated: RuntimeConnectionStringIdentifier has no ENV replacement)

| Legacy `spocr.json` Path                             | Status v4.x       | Env Variable Replacement | Notes                                                             |
| ---------------------------------------------------- | ----------------- | ------------------------ | ----------------------------------------------------------------- | ---------------------------- |
| `Project.Role.Kind`                                  | Deprecated        | (none)                   | Remove; default behavior assumed                                  |
| `Project.DataBase.RuntimeConnectionStringIdentifier` | Deprecated        | (none)                   | Removed – direct pass via `AddSpocRDbContext` options             |
| `Project.DataBase.ConnectionString`                  | Active (fallback) | (none)                   | Runtime: host config (appsettings / secrets / vault)              |
| `SPOCR_GENERATOR_DB` (ENV)                           | Active (bridge)   | replaces spocr.json DB   | Takes precedence over `Project.DataBase.ConnectionString` in dual | next for generator DB access |
| `Project.Output.Namespace`                           | Active            | `SPOCR_NAMESPACE`        | Optional; auto discovery if omitted                               |
| `Project.Output.*.Path`                              | Planned removal   | (TBD)                    | Will move to opinionated defaults; override strategy TBD          |
| `Version`                                            | Informational     | (none)                   | Not required; tool version from assembly/MinVer                   |
| `TargetFramework`                                    | Informational     | (none)                   | Multi-TFM handled by project; no env mapping                      |

Unlisted keys remain legacy-only for now; if still needed they will be either:

1. Promoted to explicit `SPOCR_*` variable (documented here), or
2. Dropped with deprecation notice prior to v5.0.

Mapping table last updated: 2025-10-12.

### Migration Example

Before (`spocr.json` excerpt – pre migration):

```jsonc
{
  "project": {
    "role": { "kind": "Default" },
    "dataBase": {
      "connectionString": "Server=.;Database=AppDb;Trusted_Connection=True;"
    },
    "output": { "namespace": "MyCompany.App.Data" }
  }
}
```

After (bridge phase v4.5 – generator only, runtime via DI):

```
# .env (generator only)
SPOCR_GENERATOR_MODE=dual
# Optional override
SPOCR_NAMESPACE=MyCompany.App.Data

// Program.cs (runtime configuration – replaces former RuntimeConnectionStringIdentifier usage)
builder.Services.AddSpocRDbContext(o =>
{
  o.ConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
});
```

Consequence: remove `role.kind`; no new ENV variable for DB connections; output path = internal defaults; override namespace only if required.

### CLI Help (Draft Excerpt)

```
spocr generate [--mode <legacy|dual|next>] [--output <dir>] [--no-validation]

Environment:
  SPOCR_GENERATOR_MODE   Overrides generator selection if --mode omitted (Generator Scope).
  SPOCR_NAMESPACE        Explicit namespace root (optional, generator scope).

Notes:
  - In dual mode both legacy and next outputs are produced.
  - In v5.0 default mode changes from 'dual' to 'next'.
  - `spocr.json` fallback removed in v5.0; warnings introduced shortly before removal.

### Versioned Documentation Plan

Assumption: `docs/content` will have version roots (e.g. `4.5/`, `5.0/`). Bridge pages include banner:

> Note (Bridge v4.5): This page describes transitional behavior. See the v5 documentation for the final state.

When creating the v5 docs, legacy-specific transitional explanations are dropped in favor of the final state only.

### Experimental CLI Flag

- The next generation CLI (System.CommandLine) is gated behind `SPOCR_EXPERIMENTAL_CLI=1`.
- When enabled, it intercepts execution first and provides early commands (currently: `generate-demo`).
- Fallback to legacy McMaster CLI occurs automatically if flag unset or command not recognized.
- Goal: Incremental migration with low risk, gather feedback before full command parity.
```

## Namespace Resolution

Implemented via `NamespaceResolver` (v4.5).

Resolution order:

1. Explicit environment variable `SPOCR_NAMESPACE`
2. `<RootNamespace>` from the primary csproj (if defined)
3. `<AssemblyName>` from the primary csproj (if defined)
4. Project file name (sans extension)
5. Fallback constant `SpocR.Generated`

Notes:

- A warning is logged if the final fallback (5) is used – treat this as a configuration smell.
- Future: fine‑grained per‑module overrides (post cutover) – out of current scope.

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

Status: Relaxed ("weiter-relaxed" selection) – implemented in `DualGenerationDispatcher` + `DirectoryDiff`.

Principles:

1. Informational first: structural differences (added/removed/changed counts) are reported, not failed.
2. Determinism focus: Each generator must be individually deterministic (hash manifest per run) – non-determinism MAY raise dedicated exit code once enforcement added.
3. Allow-list: `.spocr-diff-allow` (glob patterns) filters known/accepted churn from the "changed" set for noise reduction only.
4. Exit Codes reserved (21–23) for future enforcement (non-determinism, missing artifacts, diff anomalies) – currently not triggered in relaxed mode.
5. Documentation > Parity: Breaking functional improvements proceed with clear CHANGELOG/Migration entries; we do not chase cosmetic parity.

Artifacts:

- `debug/codegen-demo/manifest-legacy.json` and `manifest-next.json` (SHA256 per file + aggregate hash)
- `debug/codegen-demo/diff-summary.txt` (counts + allow-list info)

Planned escalation path:

- Phase 1 (current): Informational only
- Phase 2: Warn on non-determinism or missing expected core artifacts
- Phase 3 (opt-in): Fail CI for integrity violations (not semantic diffs)

### Future Enhancements (Tracked Separately)

- Rich template directives (loops/conditionals)
- Pluggable formatting pipelines (code style normalization)
- Partial templates & macro system

Last updated: 2025-10-12

## Auto-Update Gating (Major Bridge Policy)

Implemented: Direct major jumps are suppressed unless explicitly allowed.

Logic (in `AutoUpdaterService.ShouldOfferUpdate`):

1. If latest version <= current: no offer.
2. If `SkipVersion` matches latest: no offer.
3. If `latest.Major > current.Major` and env `SPOCR_ALLOW_DIRECT_MAJOR` is NOT truthy (1/true/yes/on) → suppress offer and emit guidance to first upgrade to the latest minor of the current major (bridge release).
4. Otherwise: offer update.

Rationale:

- Encourages incremental adoption & migration steps, reducing support risk.
- Ensures users encounter deprecation warnings before disruptive jumps.

Override:

- Set `SPOCR_ALLOW_DIRECT_MAJOR=1` (or true/yes/on) to bypass (e.g. for automated integration testing or controlled environments).

Documentation TODO:

- Add CHANGELOG entry once released.
- Add brief note to README upgrade section.
