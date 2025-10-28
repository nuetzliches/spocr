title: Removed Heuristics (v5)
description: Heuristic removals and rationale for a deterministic v5 generator.
version: 5.0
---

# Removed Heuristics (v5)

The v5 generator removes ad-hoc heuristics that previously papered over gaps in metadata. This section documents what changed, why it was removed, and how the CLI enforces the new deterministic behavior.

## Summary

- Generation now relies exclusively on SnapshotBuilder metadata emitted during `spocr pull`. The CLI no longer inspects legacy `spocr.json` hints or hidden defaults.
- Output ordering aligns with the schema order captured in `.spocr/schema/*.json`. No post-processing reorders models or stored procedure wrappers.
- Result-set naming is derived from concrete metadata (table names, JSON flags, CTE aliases) instead of guessing based on procedure name suffixes.
- Legacy disable flags (`Project.Role.Kind`, `Project.Output.NamespaceFallback`, various `Disable*` switches) were removed; the modern CLI treats missing metadata as validation errors.

## Removed Heuristics

| Area                         | Legacy Behavior (v4.5)                                               | v5 Behavior                                                                    |
| ---------------------------- | -------------------------------------------------------------------- | ------------------------------------------------------------------------------- |
| DataContext naming           | Suffix normalization + directory mirroring in `DataContext/`         | DataContext generation removed; modern outputs live under configurable folders. |
| Namespace fallback           | Auto-inserted fallback namespace when configuration missing          | Fails with guidance to populate `SPOCR_NAMESPACE` (or pass `--namespace`).      |
| ResultSet guesswork          | Guessed names for JSON/CTE result sets via string slicing heuristics | Uses SnapshotBuilder `ResultSets[]` metadata; aliases captured at pull time.    |
| Column trimming              | Dropped columns flagged as duplicates to avoid diff churn            | Emits every column recorded in the snapshot; callers own filtering logic.       |
| Ordering normalization       | Sorted procedures/models alphabetically post-generation              | Preserves discovery order for deterministic diffs (hash manifests rely on it).  |

## Verification Guidance

- Run the diagnostics pull described in `migration-v5.instructions` to capture verbose metadata. Compare snapshots before/after removing heuristics to confirm deterministic output.
- Strict diff mode (exit codes 21/23) surfaces any reintroduced heuristics once coverage ≥60 % unlocks the gate.
- Unit and integration tests cover ordering, namespace validation, and JSON metadata fidelity. Extend the suites when introducing new snapshot fields.

## Reporting Gaps

Open issues with label `heuristics-removal` when you encounter residual heuristic behavior. Include:

- Stored procedure definition or schema artifact that reproduces the issue.
- Relevant `debug/.spocr/schema/*.json` entries.
- CLI command (`spocr pull`, `spocr build`, etc.) and `.env` excerpt used during the run.

This documentation remains the canonical record of heuristic debt. Update it whenever new cleanups land or when deferred heuristics require temporary exceptions.
