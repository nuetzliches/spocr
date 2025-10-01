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

| Option        | Description                                                  |
| ------------- | ------------------------------------------------------------ |
| `--validate`  | Only validate generated code without running full test suite |
| `--benchmark` | Run performance benchmarks                                   |
| `--rollback`  | Rollback changes if tests fail                               |
| `--ci`        | CI-friendly mode with structured output                      |
| `--output`    | Output file path for test results (JUnit XML format)         |
| `--filter`    | Filter tests by name pattern                                 |

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

### CI/CD Integration

```bash
spocr test --ci --output test-results.xml
```

Produces machine-readable output suitable for CI/CD pipelines.

### Performance Benchmarking

```bash
spocr test --benchmark
```

Runs performance benchmarks to measure code generation speed and memory usage.

## Test Types

### Validation Tests

- **Project Structure** - Verifies critical files exist (SpocR.csproj, Program.cs, etc.)
- **Configuration** - Validates spocr.json schema and settings
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

| Code | Description        |
| ---- | ------------------ |
| 0    | All tests passed   |
| 1    | One or more failed |

## Related Commands

- [`build`](./build) - Generate code before testing
- [`config`](./config) - Configure test settings
