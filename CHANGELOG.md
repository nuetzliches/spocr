# Changelog

All notable changes to this project will be documented in this file.
Format loosely inspired by Keep a Changelog. Dates use ISO 8601 (UTC).

## [Unreleased]

### Planned

- (none currently) – add new items here.

### Added

- Auto namespace resolution with deterministic precedence chain (ENV override > RootNamespace > AssemblyName > project filename > fallback). (EPIC E009)
- Major version bridge policy enforcement (blocks direct jump without env override `SPOCR_ALLOW_DIRECT_MAJOR`).
- Dual generation observability: SHA256 hashing + diff manifests + allow-list `.spocr-diff-allow` (informational mode).
- Global nullable enabled; legacy nullability warnings broadly suppressed via `.editorconfig` (phase 1 of Nullability Debt plan).

### Changed

- (placeholder)

### Migration / Freeze

- Legacy generator freeze (EPIC E001): No further functional enhancements to the existing DataContext code after introduction of this note. Only security or stability critical bug fixes allowed. Sentinel file `legacy-freeze.txt` denotes active freeze. Freeze effective date: 2025-10-13 (previous internal draft 2025-10-12 updated).
- Bridge Policy: Direct major upgrade attempts require transitional version path unless `SPOCR_ALLOW_DIRECT_MAJOR=1` is set. Documented to reduce accidental skipping of migration steps.

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
- Configuration keys (deprecated as of v4.5, removal in v5.0):
  - `Project.Role.Kind` (always implied Default; remove now)
  - `Project.Role.DataBase.RuntimeConnectionStringIdentifier` (removed without replacement – runtime connection configured solely via host `AddSpocRDbContext` options; no new ENV var)
  - `Project.Output` (path steering deprecated; rely on auto‑namespace + internal defaults; optional override via `SPOCR_NAMESPACE` until cutover)
- Coverage badge feature: deferred until v5 (only once higher stable core coverage + stricter gate is established)

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
