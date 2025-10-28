---
title: test
description: Run SpocR tests and validations
---

# test

Runs SpocR tests and validations to ensure code quality and proper functionality.

## Usage

```bash
spocr test [options]
```

## Options

| Option       | Description                                                   |
| ------------ | ------------------------------------------------------------- |
| `--validate` | Only validate generated code without running full test suite  |
| `--filter`   | (Reserved) Filter tests by name pattern (not yet implemented) |

Removed / Planned (not yet implemented – previously documented):

- `--ci` (structured CI output)
- `--output` (JUnit/XML file)
- `--benchmark` (performance benchmarks)
- `--rollback` (rollback changes)

These will return once fully implemented. See the Roadmap for status.

## Examples

### Self-Validation (Quick Check)

```bash
spocr test --validate
```

Perfect for pre-commit checks. Validates project structure, configuration, and generated code syntax.

### Full Test Suite

```bash
spocr test
```

Runs all available tests including validation, unit tests, and integration tests.

### Planned CI/CD Output (Future)

Structured CI output (JUnit/XML) is planned to enable native test reporting in platforms like GitHub Actions and Azure DevOps. For now, integrate by running validation + dotnet test separately.

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

The `test` command automatically detects the execution context:

- **SpocR Repository** (contains `src/SpocR.csproj`) → Validates repository structure
- **Generated Project** (contains `SpocR.csproj` in root) → Validates project structure

## Exit Codes

| Code | Description                            |
| ---- | -------------------------------------- |
| 0    | Validation (and future tests) passed   |
| 1    | Validation failed / future test errors |

## Related Commands

- [`build`](./build) - Generate code before testing
- [`config`](./config) - Configure test settings
