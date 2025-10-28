# Migration to SpocRVNext (Draft)

> Scope: Captures the v5 target state derived from the delivery checklists. Use it to understand the desired configuration and architecture; operational migration steps live elsewhere.

> Status: Draft – references EPICs E001–E013 in `CHECKLIST.md`.

## Goals

Current Snapshot Parser Version: 8 (recursive JSON type enrichment + pruning IsNestedJson + HasSelectStar suppression)

- Transition from legacy DataContext generator to SpocRVNext
- Dual generation until v5.0
- Remove legacy code in v5.0 following cutover plan
- Prepare successor repo `nuetzliches/xtraq` (namespace `Xtraq`, semantic version `1.0.0`) and ensure SpocR v4.5 references the new home post-freeze

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

> Update (2025-10-29): The generator no longer reads `spocr.json`. Provide `SPOCR_GENERATOR_DB` via `.env` or environment variables before running vNext. Legacy files may remain for reference but are ignored.

## Dual CLI Strategy

- `spocr` (v5) bleibt das Brückentool für bestehende Installationen. Es läuft ausschließlich mit `.env` / `SPOCR_*` Eingaben, erzeugt SnapshotBuilder-Artefakte und warnt bei verbleibenden Legacy-Dateien (`spocr.json`, `DataContext/`, …).
- `xtraq` (Repository `nuetzliches/xtraq`, Paket `xtraq`, Namespace `Xtraq`, Version `1.0.0`) bildet den vollständigen Nachfolger. Neue Features fließen ausschließlich dort ein; SpocR verweist nach dem Freeze auf dieses Projekt.
- Beide Tools können parallel installiert werden, ohne dass Ausgabepfade kollidieren. Damit bleibt eine gestaffelte Migration möglich: Stabilisierung und Inventar mit `spocr`, produktive Weiterentwicklung mit `xtraq`.
- Bridge-Warnungen verlinken auf dieses Zielbild, `migration-v5.instructions` sowie das Xtraq-Repository, damit Konsumenten den offiziellen Pfad kennen.

## Configuration Changes

Removed (planned / already removed):

- `Project.Role.Kind`
- `Project.Role.DataBase.RuntimeConnectionStringIdentifier` (no replacement ENV; runtime connection only via host `AddSpocRDbContext` options)
- `Project.Output` (path steering being phased out in favor of fixed layout + auto namespace)

### v5 Configuration Model

- Configuration inputs come from environment variables and `.env`; the generator no longer loads settings from `spocr.json`.
- Legacy `spocr.json` files remain optional references only; the CLI ignores their contents but surfaces a warning when they are present.
- Precedence order is fixed: CLI flag > environment variable > `.env` file.
- `spocr pull` respects local configuration and does not overwrite `.env` values.
- Existing installations can mirror legacy keys through the mapping table below so `.env` becomes the authoritative source.
- The canonical example file resides at `samples/restapi/.env.example`.

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

Loader Support:

- Current releases accept snapshots authored with parser version 6 or newer; support for v5 shapes was removed alongside the `JsonResult` shim.

Target State Expectations:

- Tooling and scripts consume `.spocr/schema/*.json` using the column-level JSON flags (`ReturnsJson*`, `IsNestedJson`, `Columns`) instead of the removed `JsonResult` object.
- Repository diffs stabilized after the one-time regeneration that introduced the flattened structure.
- JSON-aware automation traverses nested `Columns` when deriving shapes; no logic depends on the obsolete `JsonPath` field.

Rationale:

1. Simplifies internal model & external snapshot (one fewer nesting level).
2. Reduces serialization size (omits redundant objects & default false flags).
3. Aligns with runtime generator APIs already flattened earlier in the refactor.

Repository Checklist:

- Snapshots in the tracked repositories correspond to parser version 6 (or later) and carry the updated fingerprint names.
- Custom schema processors already read `IsNestedJson` and nested `Columns` without relying on removed shim properties.

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
- Repositories are expected to store snapshots regenerated after the rename; the loader does not alias `JsonColumns` to `Columns`.

Parser / Fingerprint:

- `ResultSetParserVersion` bumped to 7; fingerprint changes accordingly triggering a new snapshot file emission.

Repository Checklist:

- Snapshot artefacts under `.spocr/schema` use parser version 7 (or newer) and expose the `Columns` property for nested JSON structures.
- Downstream tooling expects `Columns` and contains no remaining references to `JsonColumns`.

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

- Loader tolerates older v7 snapshots; the pruning changes are backward compatible, but new fields appear only after regeneration.
- Tooling refers to `ReturnsJson` (or nested `Columns`) to identify JSON containers instead of relying on `IsNestedJson=true`.

Target State Expectations:

- Managed repositories have regenerated snapshots so parser version 8 metadata (recursive enrichment, pruning) is in place.
- Custom processors interpret missing `IsNestedJson` as implied by `ReturnsJson=true` and no longer expect `HasSelectStar=false` markers.

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

Repository Checklist:

- Snapshot artefacts committed to source control reflect parser version 8 hashes.
- Schema processors and generated models operate on the enriched metadata without fallback-specific overrides.

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
