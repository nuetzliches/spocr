# Engineering Infrastructure (eng/)

This directory contains engineering (non-product) assets that support building, validating, and maintaining SpocR.

## Purpose

Keep the repository root clean and separate **product code** (`src/`, `tests/`) from **infrastructure and tooling**.

## Contents

| File / Area         | Purpose                                                                                          |
| ------------------- | ------------------------------------------------------------------------------------------------ |
| `quality-gates.ps1` | Local pre-commit / pre-push build + validate + test + coverage script (writes to `.artifacts/`). |
| `README.md`         | This document.                                                                                   |

## Transient Artifacts

All transient output (test results, coverage, generated reports) goes to the hidden folder `.artifacts/` which is gitignored (except for a `.gitkeep`).

## Conventions

- Add new engineering scripts here (release automation, analyzers setup, benchmarks harness, etc.).
- Prefer PowerShell for cross-platform (GitHub hosted runners support pwsh). For simple one-liners in CI, shell/batch is fine.
- Keep scripts idempotent and side-effect aware (fail fast, non-zero exit codes on errors).

## Future Candidates

- `eng/benchmarks/` (BenchmarkDotNet harness)
- `eng/analyzers/` (custom rules configuration)
- `eng/release/` (semantic version helpers)
- `eng/templates/` (code generation templates or scaffolds)

## Decommissioned `scripts/` Folder

The legacy `scripts/` folder is retained temporarily only for historical reference and will be removed after branches still referencing it are merged or rebased. New additions should go into `eng/` exclusively.

## Quick Usage

Run quality gates:

```
powershell -ExecutionPolicy Bypass -File eng/quality-gates.ps1 -CoverageThreshold 60
```

## Questions

If unsure whether something belongs here or in product code, ask: “Does this ship to the user?” If no → `eng/`.
