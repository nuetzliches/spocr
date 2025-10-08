# Changelog

All notable changes to this project will be documented in this file.
Format loosely inspired by Keep a Changelog. Dates use ISO 8601 (UTC).

## [Unreleased]

### Added
- Implicit "Modern Mode" activation when target framework >= `net10.0` (no explicit config flag required). Automatically provides dynamic in-memory template stubs while real modern templates are being authored.
- Optional `Project.Output` configuration: when omitted, namespace & folder structure are inferred (DataContext, Models, StoredProcedures, etc.).
- Dynamic Roslyn template stubs for modern mode (DbContext scaffolding & stored procedure extension placeholder) removing need to copy legacy template files.

### Changed
- Generators (Inputs, Models, TableTypes, StoredProcedures) updated to fully support file-scoped namespaces; resilient placeholder detection for stored procedure extensions.
- Configuration normalization fills missing Output/DataContext paths to maintain backwards-compatible structure while allowing minimal `spocr.json`.
- Debug output directory stabilization (root auto-detected via `SpocR.sln` or `.git`) ensuring artifacts land under a consistent `debug/` folder regardless of invocation depth.

### Deprecated
- `Role.Kind` (ignored during generation; emits deprecation warning – slated for removal after stable modern release).
- `RuntimeConnectionStringIdentifier` (ignored in modern mode; legacy resolution path retained for pre-net10 targets only).
- Mandatory presence of `Project.Output` node (now optional; provide only when overriding defaults).

### Planned
- Replace temporary modern stubs with finalized template implementations (DbContext enhancements, execution pipeline, DI helpers) before beta.
- Introduce suppression or removal path for deprecated `Role.Kind` warnings (multi-phase: warn -> silent ignore -> removal).

## [4.5.0-alpha.12] - 2025-10-07

### Fixed
- CI: Korrigierter CycloneDX SBOM Aufruf – ersetzte ungültige Argumentform (`dotnet CycloneDX dotnet --project-file ... --json`) durch gültige Syntax `dotnet CycloneDX <project> --output <dir> --output-format json --exclude-test-projects`.

## [4.5.0-alpha.11] - 2025-10-07

### Infrastructure
- SBOM Erzeugung: Wechsel von direktem Binary Aufruf `cyclonedx` (nicht vorhanden) zu korrektem `dotnet CycloneDX` Global Tool Invocation; vereinfacht Fallback-Logik und behebt `command not found` Fehler unter GitHub Actions.

## [4.5.0-alpha.10] - 2025-10-07

### Infrastructure
- Removed tag-push trigger from publish workflow (NuGet publish now only runs on GitHub Release or manual dispatch).
- Further hardened CycloneDX SBOM step (explicit PATH export + direct binary fallback).

## [4.5.0-alpha.9] - 2025-10-07

### Notes
- No functional changes since 4.5.0-alpha.8 (administrative tag only).

## [4.5.0-alpha.8] - 2025-10-07

### Infrastructure
- Hardened SBOM generation step (re-validates cyclonedx tool availability and reinstalls if missing to avoid intermittent 'command not found').
- Consolidated test artifacts under unified `.artifacts/` root with robust root detection helper (`TestPaths`).
- Added root-level `SpocR.runsettings` (session timeout, blame collector) replacing per-test location.

### Fixed
- CI publish workflow: removed `--no-build` from test step to prevent invalid argument errors when stale test binaries were not present (ensures a reliable build before execution).

## [4.5.0-alpha.3] - 2025-10-07

### Changed
- Migrated test assertion library from FluentAssertions to Shouldly (licensing simplification, leaner dependency footprint)
- `SpocrStoredProcedureManager` now accepts an injected configuration file manager (enables unit testing without internal FileManager construction)

### Removed
- Obsolete heuristic JSON parser test (`JsonParserHeuristicRemovalTests`) that asserted null `ResultSets`; implementation now standardizes on empty collections instead of null

### Build
- MinVer configuration enriched: explicit `MinVerAutoIncrement=patch`, default prerelease id `preview`, detailed verbosity, and build metadata placeholder `commit-%GIT_SHA%` for future CI substitution.

## [4.5.0-alpha] - 2025-10-06

### Added
- `spocr sp ls --json` option for machine-readable listing of stored procedures in a schema (always valid JSON array)
- Documentation page `2.cli/commands/sp.md` (stored procedure commands)
- JSON Stored Procedure Parser (alpha): baseline detection of first JSON result set with type inference heuristics
- Reference page `3.reference/json-procedures.md` (raw vs typed `DeserializeAsync` generation, model fallback behavior)
- CI test summary artifact (`--ci`) writing `.artifacts/test-summary.json`
- Per-suite metrics (unit / integration) with durations, skipped counts, failures
- JUnit single-suite XML export via `--junit` (aggregate)
- CLI flags: `--only <phases>`, `--no-validation`, `--junit`, `--output <file>`
- Granular exit codes (41 unit, 42 integration, 43 validation precedence)
- Console failure summary (top failing tests)
- Script `eng/kill-testhosts.ps1` for lingering testhost cleanup
- `spocr pull --no-cache` flag (force definition re-parse)
- Enhanced JSON heuristics (`WITHOUT ARRAY WRAPPER`, ROOT name extraction, multi-set inference)

### Changed
- Internal naming unified: `StoredProcdure` → `StoredProcedure` (BREAKING only for consumers referencing internal types; CLI surface unchanged)
- Test orchestration sequential for deterministic TRX parsing
- Test summary JSON schema expanded (nested suites + timestamps + phase durations)
- `spocr.json`: JSON-returning procedures no longer emit legacy `Output` array
- `spocr.json`: All procedures now expose unified `ResultSets`; root-level `Output` removed (BREAKING if tooling depended on it)
- `ResultSets[].Columns` enriched with `SqlTypeName` + `IsNullable` for non-JSON procedures
- Removed internal `PrimaryResultSet` property & root-level JSON flags (now only inside `ResultSets`)

### Fixed
- Invalid JSON output for `sp ls` (replaced manual concatenation with serializer; always returns `[]` or objects)
- Intermittent zero-count test parsing race (improved readiness checks)

### Deprecated
- `Project.Json.Enabled`, `Project.Json.InferColumnTypes`, `Project.Json.FlattenNestedPaths` (ignored; slated for removal)
- Legacy generation that emitted empty JSON models now includes XML doc note; encourage explicit SELECT lists to enable inference

### Notes
- Alpha: Helper-based deserialization (`ReadJsonDeserializeAsync<T>`) may evolve prior to beta
- Exit code map standardized (0,10,20,30,40,50,60,70,80,99) for future specialization

## [4.1.x] - 2025-10-01

### Added

- `spocr test` command (self-validation + future orchestration scaffold)
- Testing documentation in `tests/docs/`

### Changed

- Moved test projects from `src/` to `tests/` (clear separation from production code)
- Removed multi-targeting in tests (simpler build, resolves duplicate assembly attributes)
- Renamed class `Object` to `DbObject` (avoid conflict with `object` keyword)
- Expanded README with "Testing & Quality" section

### Removed

- Deprecated Testcontainers-based fixture (parked for future reconsideration)
- Legacy TESTING\*.md files from `src/`

### Fixed

- Build errors caused by duplicate assembly attributes
- Namespace conflicts (GlobalUsing on TestFramework) resolved

## History prior to 4.1.x

Earlier versions did not maintain a formal changelog.

---

Note: This document was translated from German on 2025-10-02 (auto-increment MSBuild target was removed; version now derived via Git tags using MinVer).
