# Changelog

Alle relevanten Änderungen an diesem Projekt werden in dieser Datei erfasst.
Das Format orientiert sich lose an Keep a Changelog.

## [Unreleased]
- (geplant) Reaktivierung Integration Tests (LocalDB)
- (geplant) XML/JUnit Output für `spocr test --ci`
- (geplant) Rollback Mechanismus für KI-Agent Workflows

## [4.1.x] - 2025-10-01
### Added
- `spocr test` Command (Self-Validation + zukünftige Orchestrierung)
- Dokumentation zum Testing in `tests/docs/`

### Changed
- Testprojekte aus `src/` nach `tests/` verschoben (Klarere Trennung von Produktionscode)
- Multi-Targeting in Tests entfernt (vereinfacht Build, behebt doppelte Assembly Attribute)
- Klasse `Object` in `DbObject` umbenannt (Vermeidung Konflikt mit `object`)
- README erweitert um Abschnitt "Testing & Quality"

### Removed
- Veraltete Testcontainers-basierte Fixture (vorerst ausgelagert)
- Alte TESTING*.md Dateien aus `src/`

### Fixed
- Build-Fehler durch doppelte Assembly Attribute eliminiert
- Namespace Konflikte (GlobalUsing auf TestFramework) behoben

## Historie vor 4.1.x
Frühere Versionen hatten kein formal gepflegtes Changelog.

---
Hinweis: Patch-Versionen werden automatisch inkrementiert (MSBuild Target).