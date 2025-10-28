# Migration to SpocRVNext (Draft)

> Status: Draft – references EPICs E001–E013 in `CHECKLIST.md`.

## Goals

Current Snapshot Parser Version: 8 (recursive JSON type enrichment + pruning IsNestedJson + HasSelectStar suppression)

- Transition from legacy DataContext generator to SpocRVNext
- Dual generation until v5.0
- Remove legacy code in v5.0 following cutover plan
- Prepare successor repo `nuetzliches/xtraq` (namespace `Xtraq`, semantic version `1.0.0`) and ensure SpocR v4.5 references the new home post-freeze

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

## Dual CLI Strategy

- Publish the frozen v4 CLI as the dotnet tool `spocrv4`. It continues to consume `spocr.json` and emit the legacy `DataContext/` structure, enabling projects to finish the cutover on their own timeline.
- During the bridge phase the v5 CLI keeps the `spocr` package name and operates solely against `.env` / `SPOCR_*` keys plus SnapshotBuilder artefacts.
- At cutover the modern CLI transitions to the new repository `nuetzliches/xtraq` and ships as tool/package `xtraq` (namespace `Xtraq`, version `1.0.0`) without historical SpocR references. The SpocR repository remains frozen at v4.5 and highlights Xtraq as the active successor.
- All CLIs install independently. Throughout the transition `spocrv4`, `spocr` (bridge), and later `xtraq` can coexist without overlapping generated outputs.
- To steer users toward the migration path, `spocr` detects legacy artefacts (`spocr.json`, `DataContext/`, legacy outputs) and prints a warning linking to this document, `migration-v5.instructions`, and (post-cutover) the Xtraq repository.

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
spocr generate [--output <dir>] [--no-validation]

Environment:
  SPOCR_NAMESPACE        Explicit namespace root (optional, generator scope).

Notes:
  - Generator runs in next-only mode.
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
  - Generator mode toggles removed: pipeline always emits next output.
  - CLI no longer accepts `--mode`; ensure `.env` exists before running generation.
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

## Post-Migration Successor (Xtraq 1.0.0)

- Repository: `nuetzliches/xtraq` beherbergt den fortgeführten Codegenerator.
- Namespace: Standard-Namespace lautet `Xtraq`; Generator, Templates und Samples verwenden keine `SpocR`-Präfixe mehr.
- Versionierung: Start mit `1.0.0`, semantische Versionierung fortlaufend über das neue Repository.
- Historie: Keine Legacy-Referenzen im neuen Projekt; Dokumentation fokussiert ausschließlich auf den Xtraq-Funktionsumfang.
- Freeze-Hinweis: Das SpocR-Repository bleibt bei v4.5 eingefroren und verweist in README, CHANGELOG sowie Auto-Updater-Hinweisen auf das Xtraq-Projekt.

## Decisions (Resolved Former Open Points)

### Snapshot JSON Flattening (ResultSet Parser v6)

Status: Introduced in vNext (ResultSetParserVersion = 6) – BREAKING for snapshot JSON shape.

Change Summary:

- Removed nested `JsonResult` object previously present inside each result column (v5 and earlier snapshot shape).
- Promoted its properties to the column level with a consistent flattened naming scheme:
  - `IsNestedJson` (nullable bool; only emitted when true)
  - `ReturnsJson`, `ReturnsJsonArray`, `ReturnsJsonWithoutArrayWrapper` (nullable bool; only emitted when true)
  - `JsonRootProperty` (string; omitted when null/empty)
  - (v6) `JsonColumns` renamed to (v7) nested `Columns` (array of nested flattened columns; omitted when empty)
- Removed legacy `JsonPath` emission (no longer produced; legacy readers still tolerate it if present in older snapshots).
- Writer prunes null / default / empty JSON metadata to keep snapshots smaller & diff‑friendly.

Reader Backward Compatibility:

- Loader support for older snapshots (v5) has been removed. Regenerate schema artifacts before installing the release that drops `JsonResult` shims.

Migration Impact:

- Any tooling or scripts consuming `.spocr/schema/*.json` must update field access: replace `column.JsonResult.ReturnsJson*` with direct column-level flags.
- Diff noise expected once per snapshot regeneration; after first commit diffs stabilize (less churn due to pruning of defaults).
- If your automation relied on `JsonPath`, switch to hierarchical traversal of nested `Columns`.

Rationale:

1. Simplifies internal model & external snapshot (one fewer nesting level).
2. Reduces serialization size (omits redundant objects & default false flags).
3. Aligns with runtime generator APIs already flattened earlier in the refactor.

Action Items for Consumers:

- Regenerate snapshots (run `spocr pull` / build) to produce v6 snapshot.
- Update any custom schema processors to look for `IsNestedJson` / nested `Columns`.
- Commit updated fingerprint files (fingerprint includes parser version so a new root snapshot file name is expected).

Fallback / Rollback:

- To temporarily continue using an older snapshot without regenerating, you must pin to an earlier tool version (before the commit removing legacy readers). Current releases refuse legacy shapes.

Documentation:

- CHANGELOG: Added under Changed section (flattened snapshot JSON structure; parser version bump to 6).
- This migration guide section serves as the authoritative reference for the structural change.

Example (Simplified):

Before (v5):

```json
{
  "Name": "Data",
  "SqlTypeName": "nvarchar",
  "JsonResult": {
    "ReturnsJson": true,
    "ReturnsJsonArray": false,
    "JsonRootProperty": "root",
    "Columns": [{ "Name": "Id", "SqlTypeName": "int" }]
  }
}
```

After (v6):

```json
{
  "Name": "Data",
  "SqlTypeName": "nvarchar",
  "IsNestedJson": true,
  "ReturnsJson": true,
  "JsonRootProperty": "root",
  "Columns": [{ "Name": "Id", "SqlTypeName": "int" }]
}
```

Note: Fields with `false` / empty values are pruned (e.g. `ReturnsJsonArray` omitted when false).

### Nested JSON Columns Rename (ResultSet Parser v7)

Status: Introduced in v7 (ResultSetParserVersion = 7) – BREAKING rename if you previously consumed v6 snapshots.

Change:

- Property `JsonColumns` inside each result column (holding nested JSON child columns) renamed to `Columns`.
- Rationale: Harmonize naming so nested JSON columns use the same property name as top-level result set columns, reducing concept count.
- Since no external developers consumed the v6 shape (internal refactor), no backward compatibility shim retained; loader expects `Columns` for nested JSON from v7 onward.
- Older v6 snapshots (with `JsonColumns`) must be regenerated; the loader does not alias `JsonColumns` to `Columns`.

Parser / Fingerprint:

- `ResultSetParserVersion` bumped to 7; fingerprint changes accordingly triggering a new snapshot file emission.

Migration Steps:

1. Regenerate schema snapshots (`spocr pull` / build) to produce v7 snapshot files.
2. Commit updated `.spocr/schema` artifacts.
3. Adjust any tooling referencing `JsonColumns` to use nested `Columns`.

Example Delta (simplified nested column):

Before (v6):

```json
{
  "Name": "Orders",
  "IsNestedJson": true,
  "ReturnsJson": true,
  "JsonColumns": [{ "Name": "OrderId", "SqlTypeName": "int" }]
}
```

After (v7):

```json
{
  "Name": "Orders",
  "IsNestedJson": true,
  "ReturnsJson": true,
  "Columns": [{ "Name": "OrderId", "SqlTypeName": "int" }]
}
```

No further structural differences; pruning behavior unchanged.

### Recursive Nested JSON Type Enrichment & Pruning (ResultSet Parser v8)

Status: Introduced in v8 (ResultSetParserVersion = 8) – structural & behavioral improvements.

Changes:

1. Recursive Type Enrichment

- The JSON type enrichment stage now traverses nested `Columns` recursively.
- Previously, only top-level JSON columns were upgraded from fallback (`nvarchar(max)` / `unknown`) to concrete SQL types using source bindings.
- Nested columns (e.g. `sourceAccount.accountId`) now resolve their `SqlTypeName`, `IsNullable`, and `MaxLength` when source bindings exist.
- Statistics counters include nested resolutions (run summary line).

2. Pruning `IsNestedJson`

- When a column also has `ReturnsJson=true`, `IsNestedJson` becomes redundant and is omitted (null). Consumers should treat any column with `ReturnsJson=true` as a JSON container regardless of `IsNestedJson` presence.

3. Suppressing `HasSelectStar=false`

- Snapshot now omits `HasSelectStar` entirely when false by emitting it as `null` (property is nullable). Only `true` values remain.
- Rationale: Reduces diff noise and size; default false conveys little value.

4. Parser Version Bump

- Fingerprint includes parser version; all snapshot file names change (new hash). Commit regenerated `.spocr/schema/*.json` artifacts.

5. Backward Compatibility

- Loader tolerates older v7 snapshots; no special shim required for the new pruning semantics.
- Tools expecting `IsNestedJson=true` must adjust logic: rely on `ReturnsJson` (or nested `Columns`) to identify JSON container columns.

Migration Impact:

- Regenerate snapshots (`spocr pull`) to pick up v8 improvements.
- Update any custom processors that relied on `IsNestedJson` presence; treat absence as implicit when `ReturnsJson=true`.
- Remove logic depending on `HasSelectStar=false`; check existence (or value true) only.

Example Before (v7 nested column – fallback typing):

```json
{
  "Name": "sourceAccount",
  "IsNestedJson": true,
  "ReturnsJson": true,
  "Columns": [{ "Name": "accountId", "SqlTypeName": "unknown" }]
}
```

After (v8):

```json
{
  "Name": "sourceAccount",
  "ReturnsJson": true,
  "Columns": [
    { "Name": "accountId", "SqlTypeName": "int", "IsNullable": false }
  ]
}
```

Notes:

- `IsNestedJson` pruned (derivable via `ReturnsJson`).
- Child `accountId` resolved from fallback to concrete type.
- `HasSelectStar` omitted when false.

Rationale:

- Simplifies consumer traversal, reduces redundant boolean flags.
- Provides richer typing for nested structures enabling stronger code generation (model properties get precise types earlier).

Action Items:

- Regenerate & commit snapshots.
- Update any schema processors to remove reliance on `IsNestedJson=true` and adjust for missing `HasSelectStar` property when false.
- Review generated models for improved typing (existing overrides may become unnecessary).

Documentation:

- CHANGELOG: Add under Changed (recursive enrichment) & Removed (redundant IsNestedJson emission, HasSelectStar=false emission).
- This section is canonical reference for v8 snapshot semantics.

### Namespace Strategy

- Primary resolution: Use the assembly root namespace derived from the main project file (csproj `RootNamespace` if present, otherwise project file name).
- Per-module override mechanism (future): Optional key in config `generation.namespaceOverride` (scoped) – not required for initial release.
- Fallback: `SpocR.Generated` only if resolution fails (should be extremely rare and treated as warning).

### Generator Mode Status

- Mode flag and environment variable removed; CLI always executes in next-only mode.
- Legacy pipeline outputs are no longer produced by default commands.

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
