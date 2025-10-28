title: v5 Differences
description: Key differences between the frozen v4.5 bridge release and the v5 environment-first CLI.
version: 5.0
---

# v5 Differences

This topic summarizes the practical differences teams experience when moving from the v4.5 bridge release to the modern v5 CLI.

## CLI & Configuration

- **Configuration source**: v4.5 preferred `spocr.json`; v5 reads only `.env`/`SPOCR_*` and runtime environment variables. The CLI warns if legacy files remain.
- **Commands**: `spocr init/pull/build/rebuild` are the supported verbs. Legacy dual-mode flags and generator mode toggles (`--mode`, `SPOCR_GENERATOR_MODE`) were removed.
- **Namespace inference**: Missing namespaces previously fell back to assembly names. v5 fails fast and instructs teams to set `SPOCR_NAMESPACE` or pass `--namespace`.

## Generator & Outputs

- **DataContext**: The old `DataContext/` output path is retired. Generated artefacts live under `SpocR/` (or `SPOCR_OUTPUT_DIR`). Any dependency on legacy helpers requires migration to the new templates.
- **SnapshotBuilder**: v4.5 included hybrid metadata; v5 relies solely on deterministic `.spocr/schema/*.json` snapshots. JSON typing and CTE alias capture happen during `spocr pull`.
- **Heuristics**: Result-set naming, column trimming, and procedure ordering heuristics were removed (see `removed-heuristics-v5.md`).

## Quality Gates & Tooling

- **Determinism**: v4.5 shipped relaxed diff reporting. v5 unlocks strict exit codes 21/23 when `SPOCR_STRICT_DIFF=1` and coverage targets are satisfied.
- **Golden manifests**: `write-golden`/`verify-golden` remain optional in v4.5; v5 positions them as the default guardrail, and documentation expects teams to refresh manifests after each intentional change.
- **Coverage expectations**: Bridge builds tracked coverage passively. The v5 roadmap enforces ≥60 % before strict diff activation, with a stretch goal of ≥80 % prior to cutover.

## API & Runtime Integration

- **Runtime registration**: Consumers wire up `AddSpocRDbContext` and supply runtime connection strings via host configuration. Generator secrets stay in `.env`, never in `spocr.json`.
- **Result helpers**: JSON helpers favor explicit `ReturnsJson` metadata, enabling typed wrappers without opt-in flags.
- **Backward compatibility**: SpocR 4.5 bleibt als eingefrorene Bridge-Version verfügbar. Der aktive Nachfolger lebt im `xtraq`-Repository; neue Features entstehen dort.

## Quick Comparison

| Area                  | v4.5 Bridge State                                 | v5 Target State                                                        |
| --------------------- | ------------------------------------------------- | ---------------------------------------------------------------------- |
| Configuration source  | `spocr.json` (env optional)                       | `.env` + environment variables only                                    |
| Output layout         | Dual: `DataContext/` + `SpocR/`                   | Single: configurable output directory (default `SpocR/`)               |
| Determinism           | Reporting only                                    | Strict diff / golden exit codes when enabled                           |
| Generator toggles     | Legacy mode flags, disable switches               | No toggles; fails fast when prerequisites missing                      |
| JSON metadata         | Mixed heuristics + partial snapshot coverage      | Snapshot-driven with explicit JSON typing and alias capture            |
| Tooling               | SpocR 4.5 (bridge release)                        | `spocr` (v5) + `xtraq` als Nachfolger                                   |

Use this page alongside `migration-v5.md` and `migration-v5.instructions` when planning the final cutover. Open issues with label `v5-planning` if additional differences need to be tracked.
