---
title: Migration Guide (v5 Cutover)
description: High-level migration steps for moving projects from the frozen v4.5 toolchain to the modern SpocR CLI.
version: 5.0
---

# Migration Guide (v5 Cutover)

This page captures the desired end state for the v5 migration. Treat it as a checklist for project teams leaving the legacy `spocr.json` + `DataContext/` workflow and adopting the environment-first CLI.

## Prerequisites

- Install the modern CLI `spocr` (v5) and the frozen bridge tool `spocrv4` so both pipelines remain available during the transition.
- Ensure `.NET 8` is installed on build agents and developer workstations.
- Confirm repositories contain the latest `.env.example` template shipped with v5.

## Migration Flow

### 1. Inventory & Bootstrap Configuration

1. Run `spocr init` and accept the prompt to generate or refresh `.env`.
2. Copy `SPOCR_*` values from any existing `spocr.json`; delete the JSON file after verifying the `.env` matches.
3. Remove secrets from source control: check `.gitignore` entries and move runtime connection strings into host configuration (`AddSpocRDbContext`).
4. Record the `.env` validation outcome in your team checklist (refer to `migration-v5.instructions`).

### 2. Align Generator Outputs

1. Use the sandbox project (`-p debug`) to run:

	```cmd
	dotnet run --project src\SpocR.csproj -- pull -p debug --no-cache --verbose
	dotnet run --project src\SpocR.csproj -- pull -p debug --verbose
	```

2. Inspect `debug/.spocr/schema/` snapshots and confirm the generator no longer reads `spocr.json`.
3. Refresh golden manifests (`write-golden`, `verify-golden`) once the output is stable.
4. Attach `debug/snapshot-summary.json` when `SPOCR_SNAPSHOT_SUMMARY_PATH` is enabled so telemetry dashboards can ingest the metrics.

### 3. Decommission Legacy Outputs

1. Stop producing `DataContext/` artefacts. Consumer projects should depend on the new output folders under `SpocR/` (or the directory specified via `SPOCR_OUTPUT_DIR`).
2. Update CI jobs, scripts, and documentation to reference environment-based configuration and the new output layout.
3. When dual-running `spocrv4`, isolate its outputs (e.g. `legacy/`) so developers can diff changes without overwriting v5 artefacts.

### 4. Update Documentation & Communication

1. Refresh project READMEs to describe the `.env` workflow and warn that `spocr.json` is ignored.
2. Link migration notices to `MIGRATION_SpocRVNext.md`, this guide, and the instructions file.
3. Coordinate with stakeholders on the cutover timeline and the plan to archive the v4.5 branch.
4. Prepare the hand-off to `nuetzliches/xtraq` once the new repository is live (tool name `xtraq`, namespace `Xtraq`).

## Breaking Changes to Validate

- Namespace inference now relies on environment settings; there is no legacy fallback.
- Stored procedure result metadata uses the new SnapshotBuilder schema (JSON typing, deterministic ordering).
- Exit codes 21/23 enforce strict diff mode when `SPOCR_STRICT_DIFF=1`.
- Analyzer warnings surface only when `SPOCR_ENABLE_ANALYZER_WARNINGS=1`; confirm build pipelines tolerate the new diagnostics.

## Project Checklist

- [ ] `.env` committed (without secrets) and `spocr.json` removed.
- [ ] Generator runs succeed via `.env` in local and CI environments.
- [ ] Golden manifests refreshed and validated.
- [ ] Legacy DataContext references eliminated from code and docs.
- [ ] Team communication sent with upgrade guidance and support contacts.

Document evidence for each milestone in `CHECKLIST.md` and keep `migration-v5.instructions` synchronized when the process evolves.
