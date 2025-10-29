---
title: test (retired)
description: Status of the retired SpocR CLI test command and current workaround
---

# test (retired)

> ℹ️ The `spocr test` verb was removed from the v5 CLI on 2025-10-31 while the validation workflow is being redesigned. Use `dotnet test` to execute the suites until the consolidated command returns.

## Usage

```bash
dotnet test tests/Tests.sln
```

## Options

No CLI options are currently available. Historical switches (`--validate`, `--ci`, `--output`, `--benchmark`, `--rollback`) remain on the roadmap and will be reintroduced when the command returns.



## Examples

### Self-Validation (Quick Check)

```bash
dotnet test tests/SpocR.Tests
```

Validates the unit-test layer without waiting for integration fixtures.

### Full Test Suite

```bash
dotnet test tests/Tests.sln
```

Executes all available projects (unit + integration). Combine with `--filter` to target subsets.

### Planned CI/CD Output (Future)

Structured CI output (JUnit/XML) will return with the new CLI surface. For now, collect results from `dotnet test` (e.g., `/p:CollectCoverage=true` or `--logger trx`).

### Performance Benchmarking (Removed)

Benchmark support was removed from near-term scope to focus on stability and correctness first.

## Test Types

### Validation Tests

- **Project Structure** - Verifies critical files exist (SpocR.csproj, Program.cs, etc.)
- **Configuration** - Validates `.env` / `SPOCR_*` configuration and required keys
- **Generated Code** - Checks syntax and compilation of generated C# code

### Unit Tests

- Logic and service layer testing
- Extension method validation
- Utility function verification

### Integration Tests

- Database connectivity and schema reading
- End-to-end code generation workflows
- Generated code execution against test databases

## Context Detection

Context detection is part of the upcoming redesign. Today the workflow simply runs `dotnet test` from the repository root or the generated consumer project.

## Exit Codes

Standard `.NET` exit codes apply (`0` for success, non-zero for failures). Future iterations of the CLI verb will restore differentiated exit codes for the individual phases.

## Related Commands

- [`build`](./build) - Generate code before testing
- [`config`](./config) - Configure test settings
