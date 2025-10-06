# SpocR Testing Framework

🧪 **Comprehensive testing infrastructure for automated validation and AI agent integration**

## Quick Start

### Running Tests

```bash
# Run all tests
spocr test

# Validate generated code only
spocr test --validate

# (Planned) Structured CI output & JUnit XML (not yet implemented)
# (Removed) Performance benchmark shortcut (de-scoped for now)
```

### Test Structure

```
tests/
├── SpocR.Tests/               # Unit tests (net8)
├── SpocR.IntegrationTests/    # (planned) Integration tests
├── SpocR.TestFramework/       # Shared test infrastructure
└── docs/                      # Test documentation (this file)
```

Production code remains in `src/`.

## Features

### ✅ **Unit Testing**

- Manager & service tests
- Extension method coverage
- Configuration validation
- Dependency injection setup verification

### 🔗 **Integration Testing** (reactivation planned)

- SQL Server / LocalDB scenarios
- Schema validation
- End-to-end code generation

### 🔍 **Self-Validation Framework**

- Generated C# syntax validation (Roslyn)
- Compilation check
- Quality hooks
- (Planned) Breaking change detection

### 📊 **CI/CD Integration**

- GitHub Actions workflow
- (Planned) JUnit XML output
- Coverage (active in workflow)
- Benchmarks (removed from near-term scope)

## Architecture Layers

```
🔄 Self-Validation
🧪 Integration Tests (later)
🏗️ Unit Tests (active)
```

## Current Focus (2025-10-01)

- Minimal green unit test baseline established
- Integration tests deferred until simplified fixture design
- Testcontainers removed (reduced complexity)
- Goal: Expand unit layer → reintroduce integration gradually

## Example: Developer Workflow

```bash
dotnet test tests/SpocR.Tests
spocr test --validate
```

## Roadmap (condensed)

1. Expand unit test coverage
2. Add lightweight DB fixture (LocalDB)
3. Simple integration test (connection + basic query)
4. Implement JUnit/XML export (planned)
5. Strengthen coverage gates
6. Optional performance benchmarking (long-term)

---

Updated after migration of test artifacts from `src/` → `tests/`.
