title: Testing Framework (v5)
description: Current state and roadmap for the SpocR CLI validation pipeline.
---

# Testing Framework (v5)

The modern CLI ships with a consolidated testing command that exercises generated code, verifies snapshot determinism, and produces machine-readable artifacts for CI/CD. This page documents the steady state, how to use `spocr test`, and which enhancements remain on the roadmap.

## Overview

- **Command**: `spocr test`
- **Phases**: unit, integration, validation (configurable)
- **Artifacts**: JSON summary (`test-summary.json`), optional JUnit XML, TRX logs
- **Exit Codes**: 0 = success, 41/42/43 = phase failures, 50+ reserved for validation/automation issues

The CLI relies on `.env`/`SPOCR_*` for configuration when spinning up integration environments. No legacy `spocr.json` settings are consulted.

## Phase Breakdown

### Unit Tests

- Executes `tests/SpocR.Tests/SpocR.Tests.csproj`
- Filters out meta suites via `Category!=Meta`
- Emits `unit.trx` under `.artifacts` in CI mode

### Integration Tests

- Runs `tests/SpocR.IntegrationTests/SpocR.IntegrationTests.csproj`
- Uses the generator sandbox connection string defined in `.env`
- Produces `integration.trx` when CI mode is active

### Validation Checks

- Verifies project structure, configuration, and generated code health
- Ensures `.env` contains required keys and that generated artefacts compile
- Enforced by default unless `--no-validation` is passed

## Features

### Commands

```cmd
:: Execute all phases sequentially
spocr test

:: Validate generated code only (no unit/integration)
spocr test --validate

:: Skip validation phase when running the full suite
spocr test --no-validation

:: Limit execution to specific phases (CSV)
spocr test --only unit,integration

:: Produce CI artefacts (JSON + JUnit)
spocr test --ci --junit
```

### Test Types

#### 1. **Generated Code Validation**

- Syntax validation of generated C# classes
- Compilation testing with Roslyn
- Type safety verification
- Namespace and naming convention checks

#### 2. **Database Integration Tests**

- SQL Server schema validation using containerized instances (Testcontainers planned) or local developer databases
- Stored procedure metadata accuracy
- Runtime integration with generated DbContext extensions

#### 3. **Snapshot Testing**

- Generated code snapshot comparisons
- Schema change detection
- Breaking change alerts
- Version compatibility testing

#### 4. **Performance Benchmarks (Planned)**

- BenchmarkDotNet integration remains on the backlog (`--benchmark` currently prints a placeholder warning)

## Implementation Details

### Project Structure

```
src/
├── SpocR/                     # Main project
├── SpocR.Tests/               # Unit tests
│   ├── Managers/
│   ├── Services/
│   ├── CodeGenerators/
│   └── Extensions/
├── SpocR.IntegrationTests/    # Integration tests
│   ├── DatabaseTests/
│   ├── EndToEndTests/
│   └── PerformanceTests/
└── SpocR.TestFramework/       # Shared test infrastructure
    ├── Fixtures/
    ├── Helpers/
    └── Assertions/
```

### Dependencies

- **xUnit** – Primary test framework for unit and integration tests
- **Shouldly** – Human-readable assertions
- **Verify** – Snapshot testing for generated code
- **BenchmarkDotNet** – Planned for future benchmarking support

### CI/CD Integration

#### GitHub Actions

```yaml
name: SpocR Test Suite
on: [push, pull_request]
jobs:
  test:
    strategy:
      matrix:
        os: [ubuntu-latest, windows-latest]
        dotnet: ["8.0", "9.0"]
    runs-on: ${{ matrix.os }}
    services:
      sqlserver:
        image: mcr.microsoft.com/mssql/server:2022-latest
    steps:
      - name: Run Unit Tests
        run: dotnet test SpocR.Tests
      - name: Run Integration Tests
        run: dotnet test SpocR.IntegrationTests
      - name: Run SpocR Self-Tests
        run: dotnet run --project SpocR -- test --ci
```

#### Azure DevOps

- Pipeline integration with `dotnet test`
- Test result publishing
- Code coverage reports
- Performance trend tracking

## Recent Enhancements

| Area          | Feature                                     | Status | Notes                                                                              |
| ------------- | ------------------------------------------- | ------ | ---------------------------------------------------------------------------------- |
| Orchestration | Sequential phase execution                  | Done   | Removed race conditions for TRX parsing.                                           |
| Reporting     | JSON summary artifact (`test-summary.json`) | Done   | Provides aggregate + per-suite metrics (`tests.unit`, `tests.integration`).        |
| Reporting     | Failure details extraction                  | Done   | `failureDetails[]` with name + message. Stack traces remain on the backlog.        |
| CLI           | `--only` phase filter                       | Done   | Accepts CSV: unit,integration,validation (validation implied unless skipped).      |
| CLI           | `--no-validation` flag                      | Done   | Skips validation phase entirely.                                                   |
| CLI           | Granular exit subcodes 41/42/43             | Done   | Unit / Integration / Validation failure precedence.                                |
| CLI           | Console failure summary                     | Done   | Prints top (<=10) failing tests with suite origin.                                 |
| CLI           | JUnit output (single-suite)                 | Done   | `--junit` emits aggregate JUnit XML; multi-suite output is tracked as follow-up.   |
| Stability     | TRX parsing retries & logging               | Done   | Robust against transient file locks.                                               |
| Tooling       | Process cleanup script                      | Done   | `eng/kill-testhosts.ps1` terminates stale test hosts.                              |
| Metrics       | Durations & timestamps                      | Done   | `startedAtUtc`, `endedAtUtc`, per-phase millisecond fields.                        |
| Metrics       | Skipped test count capture                  | Done   | Aggregated plus per-suite `skipped` totals.                                        |

## Backlog & Triggers

| Rank | Item                                | Purpose                                         | Status   |
| ---- | ----------------------------------- | ----------------------------------------------- | -------- |
| 1    | Multi-suite JUnit XML               | Separate unit/integration visibility in CI      | Planned  |
| 2    | `--require-tests` flag              | Fail fast when selected phases discover 0 tests | Planned  |
| 3    | Stack traces in `failureDetails`    | Richer diagnostics in JSON / JUnit              | Planned  |
| 4    | Trait-based suite classification    | More robust than filename heuristics            | Planned  |
| 5    | Validation phase detailed reporting | Expose granular validation rule results         | Planned  |
| 6    | History log (`test-history.jsonl`)  | Longitudinal quality & performance tracking     | Proposed |
| 7    | Configurable failure list size      | Tune console verbosity (`--max-fail-list`)      | Proposed |

Backlog work kicks off when:

1. CI consumers request per-suite visualization (→ Multi-suite JUnit).
2. Teams observe false positives due to zero-test suites (→ `--require-tests`).
3. Engineers repeatedly dive into raw TRX for stack traces (→ stack trace capture).

Design guardrails:

1. JSON summary schema must evolve additively (no breaking key renames prior to a major release).
2. Exit codes stay stable; new codes occupy unused numeric ranges.
3. TRX parsing errors degrade gracefully into warnings rather than hard failures.
4. CLI flags remain composable (`--only`, `--no-validation`, `--junit`).

---

## Getting Started

### For Developers

```bash
# Clone and setup
git clone https://github.com/nuetzliches/spocr.git
cd spocr

# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### For CI/CD

```bash
# Integration with existing pipelines
dotnet tool install --global SpocR
spocr test --ci --output results.xml
```

### For KI-Agents

```bash
# Validate changes automatically
spocr test --validate --rollback
```

---

_The Testing Framework is designed to grow with SpocR's complexity while maintaining simplicity and reliability for all users - from individual developers to enterprise CI/CD systems and AI-driven development workflows._
