# Changelog

All notable changes to this project will be documented in this file.
Format loosely inspired by Keep a Changelog. Dates use ISO 8601 (UTC).

## [Unreleased]

- (planned) Reactivate integration tests (LocalDB)
- (planned) JUnit/XML output for `spocr test` (structured CI reporting)
- (planned) Rollback mechanism for AI‑agent workflows

### Added / Internal

- Introduced structured spaced exit code map (0,10,20,30,40,50,60,70,80,99) to allow future specialization; no public scripts depended on prior provisional values.

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
