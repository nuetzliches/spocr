# SpocR Debug & Development Guide

Companion playbook for fast iteration on the CLI generator. Pair these steps with the guardrails in `.github/instructions/spocr-v5-instructions.instructions.md` and keep `CHECKLIST.md` plus the sub-checklists aligned before and after each debugging session.

## Goals

- Fast feedback loop when changing generators / schema loading
- Deterministic rebuilds for reproducible diffs
- Transparent diagnostics (what was pulled, what was generated, timings)
- Foundation for future performance improvements (batch queries, caching)

## Core Commands

| Scenario                      | Command (example)        |
| ----------------------------- | ------------------------ |
| Pull database schema only     | `spocr pull -p debug`    |
| Build from existing snapshots | `spocr build -p debug`   |
| Full refresh (pull + build)   | `spocr rebuild -p debug` |
| Remove generated files        | `spocr remove -p debug`  |

Add `--verbose` when you need detailed diagnostics (schema iteration, stored procedure progress). Capture notable output in the checklist when investigating determinism.

## Recommended Debug Workflow

1. Adjust test configuration in `debug/.env` (schemas, connection string, flags etc.)
2. Run a full `rebuild` to regenerate baseline output
3. Make code changes (parsers, generators, services)
4. Re-run `build` (skip pull) to isolate generator effects
5. Compare results under `debug/SpocR` using Git diff or the `debug/model-diff-report.md` helper
6. If structural DB changes occurred → run `pull` / `rebuild` again and refresh golden hashes when the guardrails require it

## Progress & Timing

SpocR prints per-stage timing in the build summary:

- CodeBase (template skeleton)
- TableTypes
- Inputs
- Outputs
- Models
- StoredProcedures

For large schema pulls, a progress indicator can surface total processed stored procedures vs. total. (For deeper instrumentation hook into `SchemaManager` where the stored procedure loop resides and gate changes behind the checklists.)

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

## Cache Footprint (Current Design)

Runtime metadata cache lives under `.spocr/cache/`. The guardrails require keeping the folder out of source control.

- Path format: `.spocr/cache/<fingerprint>.json`
- Fingerprint covers server, database, and the ordered schema list
- Contents capture procedure metadata (name, schema, modified ticks, definition hash, input/output signatures)
- Validation uses `modify_date` plus hash; add `--no-cache` to bypass
- Clearing: manual delete for now; future `cache clear` command tracks in the roadmap checklist

## Batch Optimization (Backlog Snapshot)

| Area        | Current                                                 | Planned Improvement (track in roadmap checklist)          |
| ----------- | ------------------------------------------------------- | --------------------------------------------------------- |
| Definitions | One query per procedure                                 | Single bulk query joining `sys.objects + sys.sql_modules` |
| Parameters  | Per procedure                                           | Bulk gather from `sys.parameters` filtered by object ids  |
| Outputs     | `dm_exec_describe_first_result_set_for_object` per proc | Optional batching or opt-out toggle                       |

## Common Debug Questions

**Q: My generated extension methods didn’t change even after code edits.**  
A: Ensure you ran `build` (not only `pull`). Also check you’re editing the correct generator variant (some code lives under versioned output roots). When in doubt, note the investigation under `Review-Findings` in the checklist.

**Q: Why are some models ‘empty’ or minimal?**  
A: When a procedure’s output columns can’t be reliably inferred, a skeleton is generated with XML documentation prompting manual extension. Capture repeat offenders in the roadmap checklist if a structural fix is required.

**Q: How do I isolate generation performance?**  
A: Run multiple `build` commands in a row (without `pull`) and average the last timings. Log deltas in the guardrail checklists when you adjust batching or caching.

**Q: How can I debug only one procedure?**  
A: (If a dedicated command exists) run a targeted stored procedure build; otherwise temporarily filter the schema list in `debug/.env` to a minimal subset. Remember to reset the filters and note temporary changes in the checklist to avoid stale configs.

## Minimal Troubleshooting Matrix

| Symptom                       | Probable Cause                      | Quick Check                                | Fix                                  |
| ----------------------------- | ----------------------------------- | ------------------------------------------ | ------------------------------------ |
| Missing generated files       | Wrong output root / path resolution | Look at `OutputService.GetOutputRootDir()` | Adjust path logic / framework string |
| Repeated auto-update prompt   | Global config not honoring skip     | Inspect global file / env flags            | Extend early skip condition          |
| Extremely slow pull           | Per-proc content fetch overhead     | Count DB calls (add counters)              | Implement batch reads / caching      |
| Incorrect namespace in output | Role mismatch (Lib vs Default)      | Inspect generated usings                   | Adjust config role                   |

## Suggested Next Enhancements (Living Backlog)

- Consolidate metadata cache management (`cache clear`, diagnostics) – see roadmap checklist.
- Unified progress bar (pull + build) with guardrail documentation updates.
- Optional diff report summarizing modified vs. up-to-date files.
- Pluggable serializer / model post-processing hook (track determinism impacts).

## Skipping Auto-Update

You can disable the auto-update check in different ways:

| Method          | Usage                  | Notes                                        |
| --------------- | ---------------------- | -------------------------------------------- |
| Env Var         | `SPOCR_SKIP_UPDATE=1`  | Accepts: 1, true, yes, on (case-insensitive) |
| Env Var (alias) | `SPOCR_NO_UPDATE=true` | Alias for the same behavior                  |

When either variable is present the updater short-circuits before network calls.

## Contribution Checklist (During Debug Enhancements)

- [ ] Add/update tests (snapshot or targeted)
- [ ] Keep `debug/.env` changes minimal (avoid noise)
- [ ] No sensitive connection strings committed
- [ ] Document any new flags / environment variables here

## Quick Command Reference

```cmd
:: Full refresh verbose
spocr rebuild -p debug --verbose

:: Build only (schema unchanged)
spocr build -p debug

:: Pull only
spocr pull -p debug
```

## Updating This Guide

When adding a new optimization (caching, batching, progress bar unification) add: goal, toggle/flag, rollback strategy.

---

Last update: November 5, 2025 – keep aligned with checklist guardrails.
