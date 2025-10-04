# Changelog

All notable changes to this project will be documented in this file.
Format loosely inspired by Keep a Changelog. Dates use ISO 8601 (UTC).

## [Unreleased]

- (planned) Reactivate integration tests (LocalDB)
- (planned) Multi-suite JUnit/XML output (separate unit/integration suites)
- (planned) Rollback mechanism for AI‑agent workflows

### Added

- CI test summary artifact (`--ci`) writing `.artifacts/test-summary.json` with per-suite statistics
- Per-suite metrics (unit/integration) including durations, skipped counts, failure details
- JUnit single-suite XML export via `--junit` (aggregate suite)
- CLI flags: `--only <phases>`, `--no-validation`, `--junit`, `--output <file>` for test artifacts
- Granular test exit subcodes 41 (unit), 42 (integration), 43 (validation) with precedence logic
- Console failure summary (top failing tests, truncated to 10)
- Process cleanup script `eng/kill-testhosts.ps1` for lingering `testhost` processes
- `spocr pull --no-cache` flag to bypass stored procedure definition cache (forces full re-parse; helpful after parser / heuristic changes)
- Enhanced JSON heuristics for `*AsJson` procedures (detects `WITHOUT ARRAY WRAPPER` including underscore variant, ROOT name extraction, multi-set inference)

### Changed

- Test orchestration made sequential to ensure deterministic TRX parsing (removed race conditions)
- JSON schema for test summary expanded (nested `tests.unit` / `tests.integration`, timestamps, per-phase durations)
- `spocr.json` serialization: For JSON-returning stored procedures (`ResultSets[0].ReturnsJson == true`), the legacy `Output` array is now omitted entirely (previously still emitted). Non-JSON procedures continue to emit `Output` until they are migrated to unified `ResultSets` modeling.
- `spocr.json` serialization: All stored procedures now use unified `ResultSets`. Classic non-JSON procedures have a synthesized first `ResultSet` (with `ReturnsJson=false`) reflecting former tabular columns. The legacy root-level `Output` array has been fully removed (property deleted) – BREAKING only if external tooling still parsed `Output`; migrate to `ResultSets[0].Columns`.
- `ResultSets[].Columns` now include `SqlTypeName` + `IsNullable` for non-JSON procedures (migrated from former `Output` metadata) enabling proper scalar type generation.
- Internal: Removed `PrimaryResultSet` helper property and root-level JSON flag serialization (`ReturnsJson*`) from `spocr.json` (flags remain within `ResultSets`). This is treated as non-breaking because consumer tooling should already read `ResultSets[0]`.

### Fixed

- Intermittent zero-count test parsing due to premature TRX access (retry & readiness checks)

### Notes

- Multi-suite JUnit output, `--require-tests` guard, stack trace enrichment, and trait-based suite classification are deferred (see roadmap Testing Framework remaining items)

### Added / Internal

- Introduced structured spaced exit code map (0,10,20,30,40,50,60,70,80,99) to allow future specialization; no public scripts depended on prior provisional values.

### Deprecated

- `Project.Json.Enabled`, `Project.Json.InferColumnTypes`, `Project.Json.FlattenNestedPaths` (always-on JSON model & type inference; properties ignored and slated for removal in a future minor release)
- Generation may emit empty JSON models with an XML documentation note when column discovery is impossible (dynamic SQL, wildcard `*`, variable payload). Consider rewriting procedures with explicit SELECT lists to enable property inference.

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
