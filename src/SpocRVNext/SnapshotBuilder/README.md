# Snapshot Builder

The Snapshot Builder powers the `spocr pull` experience. It orchestrates Collect → Analyze
→ Write stages to produce deterministic metadata for the generator and keeps cache state
under `.spocr/cache`. Before touching this pipeline, align updates with
`CHECKLIST.md` and `src/SpocRVNext/CHECKLIST.md` so roadmap and migration tasks stay in
sync.

## Operating Principles

- **Checklist first.** Update the snapshot sections in both checklists whenever pipeline
  behavior, telemetry, or cache semantics change.
- **Determinism matters.** Every stage must produce stable output across runs. Refresh
  golden assets and record findings when determinism drifts.
- **Diagnostics belong in the guardrails.** Use existing switches (`--verbose`,
  `SPOCR_SNAPSHOT_SUMMARY[_PATH]`) and document new telemetry alongside the guardrail
  instructions.
- **ENV-first configuration.** The CLI bootstrap (`spocr init`) and `.env` flow replace
  direct edits to legacy configuration files; warn or block when they are detected.

## Pipeline Overview

| Stage   | Responsibility                                                     | Primary Artifacts                        |
| ------- | ------------------------------------------------------------------ | ---------------------------------------- |
| Collect | Read database metadata, honor ignore lists, hydrate cache entries  | Schema definitions, procedure signatures |
| Analyze | Bind inputs/outputs, enrich JSON/Aggregate heuristics, track diffs | Analyzer results, diagnostics, type refs |
| Write   | Persist deterministic metadata for generator consumption           | Snapshot files, cache fingerprints, logs |

Cache files remain JSON for diffability (`.spocr/cache/<fingerprint>.json`); any
additions require updating `.ai/README-dot-spocr.md` plus the roadmap checklist.

## Validation Loop

```cmd
:: Cold pull to validate full pipeline
dotnet run --project src\SpocR.csproj -- pull -p debug

:: Warm pull to exercise cache paths
dotnet run --project src\SpocR.csproj -- pull -p debug

:: Targeted procedure refresh for delta validation
dotnet run --project src\SpocR.csproj -- pull -p debug --procedure workflow.WorkflowListAsJson
```

After each change: compare results in `debug/SpocR` (or `debug/model-diff-report.md`),
regenerate golden assets if outputs move, and capture findings in the `Review-Findings`
checklist section.

## Diagnostics & Type Resolution Runs

Use the following recipes to exercise analyzer paths and capture the evidence we expect to
archive alongside pull results:

- **Cold verbose pull** – highlights every analyzer decision. Enable verbose mode and a
  strict log level.

  ```cmd
  set SPOCR_LOG_LEVEL=debug
  dotnet run --project src\SpocR.csproj -- pull -p debug --no-cache --verbose
  ```

- **Cache comparison** – verify that cached runs reuse metadata without drifting types.

  ```cmd
  dotnet run --project src\SpocR.csproj -- pull -p debug --verbose
  ```

- **Targeted rerun** – isolate a single procedure for regression hunting. Persist the
  resulting `debug/test-summary.json` and `debug/.spocr/schema/<proc>.json` entries when
  sharing findings.

  ```cmd
  set SPOCR_LOG_LEVEL=debug
  dotnet run --project src\SpocR.csproj -- pull -p debug --no-cache --procedure identity.UserListAsJson --verbose
  ```

During verbose runs the analyzer emits structured tags:

| Tag                       | Meaning                                                                |
| ------------------------- | ---------------------------------------------------------------------- |
| `[json-type-table]`       | Column bound to a table snapshot (Stage 2 enrichment).                 |
| `[json-type-upgrade]`     | Fallback `nvarchar(max)` replaced with a concrete type.                |
| `[json-type-summary]`     | Per-procedure totals (resolved vs upgraded JSON columns).              |
| `[json-type-run-summary]` | Run-level aggregate summarising all JSON enrichment operations.        |
| `[proc-forward-expand]`   | Placeholder result set replaced with forwarded metadata (EXEC bridge). |

Archive representative outputs (log tail + `debug/test-summary.json`) when closing checklist
items so the reasoning remains reproducible.

## Verbose Trace Example

```
[spocr vNext] Info: BuildSchemas allow-list active -> 42 of 57 procedures retained. Removed: 15. Schemas: core,identity
[proc-forward-expand] identity.UserListAsJson expanding placeholder -> identity.UserList sets=1
[json-type-table] identity.UserListAsJson roles.roleId -> core._id
[json-type-upgrade] identity.UserListAsJson gender.displayName -> core.displayName
[json-type-summary] identity.UserListAsJson resolved=12 upgrades=3 unresolved=0
[json-type-run-summary] resolved=184 upgrades=27 unresolved=0 cacheHits=312 cacheMisses=18
```

Include a short excerpt like the above when posting findings so others can spot the same
signals without re-running the entire pipeline.

## `FOR JSON` Validation & Fallbacks

When investigating odd JSON projections, rely on the following checks before opening a
new analyzer story:

1. **Inline comment paths** – ScriptDom strips inline comments before the analyzer runs,
   so patterns like `FOR JSON PATH -- audit` still set `Json.RootProperty` and
   `Json.IsArray`. Confirm by inspecting the corresponding snapshot file in
   `debug/.spocr/schema/<schema>.<proc>.json`; the `Json` object should be populated even
   if the SQL text mixes comments and formatting.
2. **Legacy fallback** – If ScriptDom misses root metadata the analyzer falls back to the
   textual segment and still flags `Json.IsArray` unless `WITHOUT_ARRAY_WRAPPER` is
   present. Enable verbose logging (`SPOCR_LOG_LEVEL=debug`) and look for
   `[json-type-summary]` entries to ensure the run classified the set.
3. **Nested projections** – For nested `JSON_QUERY` calls the snapshot marks the column
   with `IsNestedJson=true`; during generation those columns remain string-based unless a
   deferred expansion is available. Validate by checking the regenerated snapshot and the
   generated record struct (under `debug/SpocR/<Schema>/<Proc>.cs`).

If any of these checks fail, capture the SQL text, the verbose trace, and the relevant
snapshot file and add the scenario to the analyzer backlog.

## Performance Baseline (2025-10-26)

Recorded against `samples/restapi` with `SPOCR_LOG_LEVEL=info`. Refresh the table when
pipeline changes affect throughput.

| Scenario        | Description                                                                 | Command                                                                                                     | Total (ms) | Collect (ms) | Analyze (ms) | Write (ms) |
| --------------- | --------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------- | ---------- | ------------ | ------------ | ---------- |
| Cold Cache      | End-to-end run without cache reuse; forces full analysis and hashing        | `dotnet run --project src/SpocR.csproj -- pull -p debug --no-cache`                                         | 7832       | 260          | 7319         | 242        |
| Warm Cache      | Repeated run leveraging cached analysis results                             | `dotnet run --project src/SpocR.csproj -- pull -p debug`                                                    | 9238       | 3465         | 5493         | 227        |
| Procedure Delta | Targeted refresh for `workflow.WorkflowListAsJson` after cache invalidation | `dotnet run --project src/SpocR.csproj -- pull -p debug --procedure workflow.WorkflowListAsJson --no-cache` | 645        | 246          | 185          | 202        |

Warm-cache numbers include the cost of summarizing unchanged artifacts; expect variance
based on ambient load and network latency.

## Instrumentation & Telemetry

- Use `--verbose` and `SPOCR_LOG_LEVEL=info` for per-stage timings during development.
- Set `SPOCR_SNAPSHOT_SUMMARY=1` (default path `snapshot-summary.json`) or
  `SPOCR_SNAPSHOT_SUMMARY_PATH=<file>` to persist machine-readable metrics for CI.
- Document new telemetry fields or switches in both this README and the checklists.
- Route experimental logging through existing verbosity gates so production output stays
  clean.

### Snapshot Summary Payload

The summary file captures the metrics we forward to monitoring. Example:

```jsonc
{
  "timestamp": "2025-10-27T09:14:05.381Z",
  "project": "debug",
  "stages": {
    "collect": { "durationMs": 312, "cacheHits": 128, "cacheMisses": 6 },
    "analyze": { "durationMs": 4412, "jsonResolved": 184, "jsonUpgraded": 27 },
    "write": { "durationMs": 205, "artifacts": 623 }
  },
  "procedures": {
    "identity.UserListAsJson": { "jsonResolved": 12, "jsonUpgraded": 3 }
  }
}
```

Configure CI to point `SPOCR_SNAPSHOT_SUMMARY_PATH` at a workspace-relative location
(`debug/snapshot-summary.json`) and attach the file to build artefacts. Update the
monitoring checklist whenever new fields are added so alerts stay aligned.

## Troubleshooting & Follow-ups

- Re-run the validation loop after touching cache fingerprints, analyzer heuristics, or
  writer output. Log determinism regressions in the checklist immediately.
- For procedure-specific issues, rely on the targeted refresh command above and store
  traces under `debug/` when sharing findings.
- Pending improvements (batch reads, cache maintenance commands, richer diagnostics) live
  in the roadmap checklist backlog—keep this document in lockstep when those items move.
