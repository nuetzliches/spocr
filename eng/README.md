# Engineering Infrastructure (eng/)

This directory contains engineering (non-product) assets that support building, validating, and maintaining SpocR.

## Purpose

Keep the repository root clean and separate **product code** (`src/`, `tests/`) from **infrastructure and tooling**.

## Contents

| File / Area                    | Purpose                                                                                          |
| ------------------------------ | ------------------------------------------------------------------------------------------------ |
| `quality-gates.ps1`            | Local pre-commit / pre-push build + validate + test + coverage script (writes to `.artifacts/`). |
| `cleanup-legacy-artifacts.ps1` | Removes pre-migration artifact folders (`CoverageReport/`, `TestResults/`).                      |
| `README.md`                    | This document.                                                                                   |

## Transient Artifacts

All transient output (test results, coverage, generated reports) goes to the hidden folder `.artifacts/` which is gitignored (except for a `.gitkeep`).

## Conventions

- Add new engineering scripts here (release automation, analyzers setup, benchmarks harness, etc.).
- Prefer PowerShell for cross-platform (GitHub hosted runners support pwsh). For simple one-liners in CI, shell/batch is fine.
- Keep scripts idempotent and side-effect aware (fail fast, non-zero exit codes on errors).
- Script Formatting Guidelines:
	- Use dedicated fenced code blocks with language hints (`powershell`, `cmd`).
	- Do not mix PowerShell and cmd syntax in one snippet.
	- PowerShell: 4 spaces, no gratuitous line wraps, use backticks only when necessary.
	- Batch: keep logic explicit; avoid multi-line `IF` with PowerShell-like braces.
	- Always emit non-zero exit code on failure (`throw` or `exit /b 1`).
	- Idempotent operations: check for file/dir existence before create/delete.
	- Avoid hidden Unicode whitespace and BOM in scripts.
	- Prefer descriptive function names and minimal global state.

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

Manual release pipeline dry-run (no publish):

1. GitHub → Actions → `Publish NuGet`
2. Leave `dry-run=true`
3. (Optional) set `override-version` (e.g. `9.9.9-local`)

Artifacts produced under `.artifacts/nuget` and `.artifacts/sbom` (only for real release/publish or explicit pack step).

## Questions

If unsure whether something belongs here or in product code, ask: “Does this ship to the user?” If no → `eng/`.

## Test Timeouts & Diagnostics

Intermittent hangs or timeouts can originate from blocking waits, background threads, or external I/O. This repository includes tooling to surface and mitigate them.

### Configuration

- `tests/xunit.runner.json`: Enables `diagnosticMessages` and flags tests running longer than `longRunningTestSeconds` (adjust as needed).
- `SpocR.runsettings` (root): Sets `TestSessionTimeout` and enables the VSTest blame collector for crash/hang diagnostics.

### Recommended Commands

Detect long runners:
```
dotnet test tests/Tests.sln -c Release --settings SpocR.runsettings
```

Capture hang dump after 2 minutes:
```
dotnet test tests/Tests.sln --blame-hang --blame-hang-timeout 00:02:00
```

Single-threaded isolation (check for race/deadlock):
```
dotnet test tests/Tests.sln -m:1
```

### Coding Guidelines for Stable Tests

| Area | Guideline |
|------|-----------|
| Async | Prefer `await`; avoid `.Result` / `.Wait()` |
| Cancellation | Provide `CancellationToken` for long-running or I/O heavy operations |
| Polling | Always include delay/backoff: `await Task.Delay(25, ct)` |
| Parallelism | Use `[Collection]` or `-m:1` if accessing shared mutable state |
| File System | Use unique temp paths (`Path.GetRandomFileName()`) to avoid cross-test locks |
| External Resources | Mock or fake expensive services; keep integration tests explicit |
| Logging | Add minimal diagnostic logs around loops with potential indefinite wait |

### When a Hang Occurs
1. Re-run with `--blame-hang`.
2. Inspect generated dump / logs for blocking stack traces.
3. Look for synchronous waits on async code or contention on locks.
4. Add targeted timeouts (`Task.WhenAny`) around suspect operations.

### Escalation Pattern
1. Lower `longRunningTestSeconds` temporarily (e.g. to 3) to surface borderline tests.
2. Add structured timing logs (elapsed ms) around suspicious phases.
3. Quarantine flaky test by category attribute until fixed (avoid silent ignoring).

### Anti-Patterns
- Busy-wait loops without delay.
- Swallowing exceptions in background tasks (hides failure, test hangs waiting for signal).
- Global static mutable state mutated from multiple tests.

Keep this section concise—expand only if recurring classes of hangs appear.
