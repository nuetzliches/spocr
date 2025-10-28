[![NuGet](https://img.shields.io/nuget/v/SpocR.svg)](https://www.nuget.org/packages/SpocR)
[![NuGet Downloads](https://img.shields.io/nuget/dt/SpocR.svg)](https://www.nuget.org/packages/SpocR)
[![License](https://img.shields.io/github/license/nuetzliches/spocr.svg)](LICENSE)
[![Build Status](https://img.shields.io/github/actions/workflow/status/nuetzliches/spocr/test.yml?branch=main)](https://github.com/nuetzliches/spocr/actions)

# SpocR CLI

SpocR is a SQL Server stored procedure code generator that produces strongly typed C# models, executors, and DbContext surfaces. The CLI streamlines setup through an `.env` bootstrap, deterministic schema snapshots, and first-class CI automation.

## Why SpocR

- Replace ad-hoc ADO.NET plumbing with generated async methods.
- Ship confident refactors using fingerprinted snapshots and diff tooling.
- Keep configuration discoverable and automation-friendly through environment-first settings.
- Validate pipelines with machine-readable summaries usable in CI dashboards.

## Quick Start

```cmd
:: Install the CLI as a global dotnet tool
dotnet tool install --global SpocR

:: Bootstrap a project-scoped .env (idempotent)
spocr init --namespace MyCompany.MyService --connection "Server=.;Database=AppDb;Trusted_Connection=True;" --schemas core,identity

:: Fetch schema metadata into snapshots
spocr pull

:: Generate code into the configured output folders
spocr build

:: Run validation + unit tests with CI formatting
spocr test --ci --junit
```

`spocr init` merges inferred settings with an `.env` template. Re-running the command safely updates keys without disrupting comments. The `.env` file is committed locally; CI can override any value via environment variables or CLI flags.

## Core CLI Commands

| Command                | Purpose                                                                                          |
| ---------------------- | ------------------------------------------------------------------------------------------------ |
| `spocr init`           | Creates or updates `.env` with namespace, schema allow-list, and connection hints.               |
| `spocr pull`           | Reads database metadata and writes versioned snapshots under `debug/` (or the selected profile). |
| `spocr build`          | Generates the `SpocRDbContext`, inputs, models, result sets, and executor helpers.               |
| `spocr test`           | Runs validation, unit, and (optional) integration suites with JSON / JUnit output.               |
| `spocr snapshot clean` | Prunes historical snapshot files while keeping a configurable retention window.                  |

Use `spocr --help` to discover all verbs and shared options (profiles, verbosity, dry-runs, etc.).

## Configuration via `.env`

SpocR resolves configuration in a strict precedence chain:

```
CLI flags > Environment variables > `.env` entries > internal defaults
```

A minimal `.env` produced by `spocr init`:

```
SPOCR_NAMESPACE=MyCompany.MyService
SPOCR_GENERATOR_DB=Server=.;Database=AppDb;Trusted_Connection=True;
SPOCR_BUILD_SCHEMAS=core,identity
```

Guidance:

- `SPOCR_BUILD_SCHEMAS` is an allow-list. Remove the line to generate every schema discovered during a pull.
- Runtime connection strings stay in your host application (see `debug/DataContext/AppDbContext.cs`). The CLI env variable only affects metadata pulls and generators.

## Snapshots & Determinism

Snapshots live under profile folders (default `debug/`). Each pull writes `*.json` artifacts that capture stored procedures, user-defined table types, statistics, and diff reports. Commit snapshots for review, or use the `write-golden` / `verify-golden` helpers to enforce deterministic output when adjusting generator heuristics.

Useful maintenance commands:

```cmd
:: Remove older snapshots but retain the latest five
spocr snapshot clean

:: Show deletions without touching disk
spocr snapshot clean --dry-run
```

The `debug/README.md` file documents every artifact produced during a pull/build/test cycle.

## Validation Loop

Follow the guardrails from `CHECKLIST.md` whenever generator-touching work is done:

1. `spocr pull`
2. `spocr build`
3. `spocr test --ci`
4. `eng/quality-gates.ps1` (runs analyzers, coverage, and style checks)
5. Refresh golden snapshots if `debug/*` output changes (`write-golden` / `verify-golden`)

Capture any discrepancies or new findings under the "Review-Findings" section of `CHECKLIST.md`.

## Documentation

- `src/SpocRVNext/SnapshotBuilder/README.md` – explains the snapshot pipeline and validation strategy.
- `.ai/README.md` – guardrails for AI-assisted changes and checklist synchronization.
- Public docs: https://nuetzliches.github.io/spocr/

## Contributing

Contributions are welcome as the CLI continues to evolve. Please review `CONTRIBUTING.md`, align your work with the guardrails in `.github/instructions/spocr-v5-instructions.instructions.md`, and update the relevant checklists alongside any functional or documentation changes.
