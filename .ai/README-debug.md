# SpocR Debug & Development Guide

This guide explains how to efficiently debug, profile and iterate on SpocR code generation and schema pulling – independent of any specific feature (kept generic; former JSON‑specific notes were generalized).

## Goals

- Fast feedback loop when changing generators / schema loading
- Deterministic rebuilds for reproducible diffs
- Transparent diagnostics (what was pulled, what was generated, timings)
- Foundation for future performance improvements (batch queries, caching)

## Core Commands

| Scenario                             | Command (example)                                                                |
| ------------------------------------ | -------------------------------------------------------------------------------- |
| Pull database schema only            | `dotnet run --project src/SpocR.csproj -- pull -p debug\spocr.json --verbose`    |
| Build from existing schema in config | `dotnet run --project src/SpocR.csproj -- build -p debug\spocr.json`             |
| Full refresh (pull + build)          | `dotnet run --project src/SpocR.csproj -- rebuild -p debug\spocr.json --verbose` |
| Remove generated files               | `dotnet run --project src/SpocR.csproj -- remove -p debug\spocr.json`            |

`--verbose` adds detailed log lines (schema + stored procedure iteration, progress etc.).

## Recommended Debug Workflow

1. Adjust test configuration in `debug/spocr.json` (schemas, connection string, role etc.)
2. Run a full `rebuild` to regenerate baseline output
3. Make code changes (parsers, generators, services)
4. Re-run `build` (skip pull) to isolate generator effects
5. Compare results under `debug/DataContext` using Git diff
6. If structural DB changes occurred → run `pull` / `rebuild` again

## Output Layout (Debug Folder)

```
debug/
  spocr.json            # Project configuration (debug scope)
  spocr.global.json     # Optional global overrides (debug)
  DataContext/
    Models/
    Outputs/
    StoredProcedures/
    TableTypes/
    ...
```

Generated files adopt the namespace & role configured in `spocr.json`.

## Progress & Timing

SpocR prints per-stage timing in the build summary:

- CodeBase (template skeleton)
- TableTypes
- Inputs
- Outputs
- Models
- StoredProcedures

For large schema pulls, a progress indicator can surface total processed stored procedures vs. total. (If you need a more granular progress bar, hook into `SchemaManager` where the stored procedure loop resides.)

## Extending Diagnostics

| Need                       | Approach                                                             |
| -------------------------- | -------------------------------------------------------------------- |
| See raw SQL queries        | Add temporary logging in `DbContext` execution wrappers              |
| Inspect parser results     | Dump `StoredProcedureContentModel` properties (guard with `verbose`) |
| Measure query count        | Add counters in `SchemaManager` around each DB call                  |
| Trace generation decisions | Insert `Verbose(...)` lines in generator base classes                |

## Safe Experimentation Tips

| Action                       | Recommendation                                                     |
| ---------------------------- | ------------------------------------------------------------------ |
| Prototype parsing changes    | Add a new internal method & A/B compare outputs before replacing   |
| Large refactor of generation | Create a separate branch & snapshot baseline output first          |
| Temporary logging            | Use `Verbose` level so normal runs stay clean                      |
| Benchmarking                 | Run successive `build` (without `pull`) to remove DB latency noise |

## Caching (Planned Generic Design)

The local (non‑git) cache now lives under `.spocr/cache/` (created in the project root beside `spocr.json`). Add `.spocr/` to your `.gitignore` to avoid committing ephemeral state.

Purpose: speed up repeated pulls by reusing previously parsed stored procedure metadata when unchanged. Key design points:

- Location: `.spocr/cache/<fingerprint>.json`
- Fingerprint: server + database + selected schemas (order-normalized)
- Local cache file keyed by DB fingerprint (server + database + selected schemas)
- Store: name, schema, last modified timestamp, definition hash, input/output signatures
- Validate via (modify_date + hash) before skipping re-fetch
- Flags:
  - `--no-cache` to force refresh
  - Future `cache clear` command

## Batch Optimization (Planned)

| Area        | Current                                                 | Planned Improvement                                       |
| ----------- | ------------------------------------------------------- | --------------------------------------------------------- |
| Definitions | One query per procedure                                 | Single bulk query joining `sys.objects + sys.sql_modules` |
| Parameters  | Per procedure                                           | Bulk gather from `sys.parameters` filtered by object ids  |
| Outputs     | `dm_exec_describe_first_result_set_for_object` per proc | Possibly optional / deferred (expensive to batch)         |

## Common Debug Questions

**Q: My generated extension methods didn’t change even after code edits.**  
A: Ensure you ran `build` (not only `pull`). Also check you’re editing the correct generator variant (some code lives under versioned output roots).

**Q: Why are some models ‘empty’ or minimal?**  
A: When a procedure’s output columns can’t be reliably inferred, a skeleton is generated with XML documentation prompting manual extension.

**Q: How do I isolate generation performance?**  
A: Run multiple `build` commands in a row (without `pull`) and average the last timings.

**Q: How can I debug only one procedure?**  
A: (If a dedicated command exists) run a targeted stored procedure build; otherwise temporarily filter the schema list in `spocr.json` to a minimal subset.

## Minimal Troubleshooting Matrix

| Symptom                       | Probable Cause                      | Quick Check                                | Fix                                  |
| ----------------------------- | ----------------------------------- | ------------------------------------------ | ------------------------------------ |
| Missing generated files       | Wrong output root / path resolution | Look at `OutputService.GetOutputRootDir()` | Adjust path logic / framework string |
| Repeated auto-update prompt   | Global config not honoring skip     | Inspect global file / env flags            | Extend early skip condition          |
| Extremely slow pull           | Per-proc content fetch overhead     | Count DB calls (add counters)              | Implement batch reads / caching      |
| Incorrect namespace in output | Role mismatch (Lib vs Default)      | Inspect generated usings                   | Adjust config role                   |

## Suggested Next Enhancements (Generic)

- Local metadata cache (fingerprint + hash) (IN PROGRESS; directory structure established)
- Unified progress bar (pull + build combined view)
- Optional diff report summarizing modified vs up-to-date files
- Pluggable serializer / model post-processing hook

## Contribution Checklist (During Debug Enhancements)

- [ ] Add/update tests (snapshot or targeted)
- [ ] Keep `spocr.json` changes minimal (avoid noise)
- [ ] No sensitive connection strings committed
- [ ] Document any new flags / environment variables here

## Quick Command Reference

```cmd
:: Full refresh verbose
DOTNET_ENVIRONMENT=Development dotnet run --project src/SpocR.csproj -- rebuild -p debug\spocr.json --verbose

:: Build only (schema unchanged)
dotnet run --project src/SpocR.csproj -- build -p debug\spocr.json

:: Pull only
dotnet run --project src/SpocR.csproj -- pull -p debug\spocr.json
```

## Updating This Guide

When adding a new optimization (caching, batching, progress bar unification) add: goal, toggle/flag, rollback strategy.

---

Last update: (auto-generated base) – adapt freely as features evolve.
