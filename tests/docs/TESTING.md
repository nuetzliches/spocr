# SpocR Testing Framework

🧪 **Comprehensive testing infrastructure for automated validation and AI agent integration**

## Quick Start

### Running Tests

```bash
# Run all tests (solution wide)
dotnet test tests/Tests.sln

# Focus on unit tests only
dotnet test tests/SpocR.Tests

# (Planned) Structured CI output & JUnit XML (not yet implemented)
```

> The legacy `spocr test` verb was removed from the vNext CLI on 2025-10-31. Until the replacement workflow ships, invoke `dotnet test` directly.

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
dotnet test tests/Tests.sln
dotnet test tests/SpocR.Tests
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
