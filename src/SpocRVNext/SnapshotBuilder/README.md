# Snapshot Builder vNext

The Snapshot Builder powers the `spocr pull` experience in vNext. It orchestrates
Collect → Analyze → Write stages to produce deterministic metadata for the generator and
keeps cache state under `.spocr/cache`. Before touching this pipeline, align updates with
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
- **ENV-first configuration.** The CLI bootstrap (`--init-v5`) and `.env` flow replace
  direct edits to `spocr.json`; warn or block when legacy config is detected.

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
dotnet run --project src\SpocR.csproj -- pull -p debug --no-cache --verbose

:: Warm pull to exercise cache paths
dotnet run --project src\SpocR.csproj -- pull -p debug

:: Targeted procedure refresh for delta validation
dotnet run --project src\SpocR.csproj -- pull -p debug --procedure workflow.WorkflowListAsJson --no-cache
```

After each change: compare results in `debug/DataContext` (or `debug/model-diff-report.md`),
regenerate golden assets if outputs move, and capture findings in the `Review-Findings`
checklist section.

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

## Troubleshooting & Follow-ups

- Re-run the validation loop after touching cache fingerprints, analyzer heuristics, or
  writer output. Log determinism regressions in the checklist immediately.
- For procedure-specific issues, rely on the targeted refresh command above and store
  traces under `debug/` when sharing findings.
- Pending improvements (batch reads, cache maintenance commands, richer diagnostics) live
  in the roadmap checklist backlog—keep this document in lockstep when those items move.
