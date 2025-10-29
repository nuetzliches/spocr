# Tests

This folder aggregates all test-related assets of SpocR.

## Structure

```
tests/
  SpocR.Tests/            # Active unit tests (net8)
  SpocR.IntegrationTests/ # (Planned) integration / DB tests
  SpocR.TestFramework/    # Shared test helpers & validators
  docs/                   # Test documentation & status
```

## Quick Start

```bash
# Run the complete solution tests (preferred during the CLI test hiatus)
dotnet test tests/Tests.sln

# Focus on unit tests only
dotnet test tests/SpocR.Tests
```

> The historical `spocr test` shortcut was removed from the v5 CLI. The suite now runs directly through `dotnet test` until the replacement workflow is finalized.

## Goals

1. Fast feedback (self-validation) before each commit
2. Expand unit tests → then integration layer
3. Future expansion: coverage, rollback safety, JUnit/XML output

## Roadmap Snapshot

- [x] Migration to /tests
- [x] Minimal green unit test
- [ ] Reactivate original unit test set
- [ ] LocalDB / simplified DB fixture
- [ ] First integration test
- [ ] JUnit/XML output for CI (planned)
- [ ] Enable coverage report

More details: `docs/TESTING.md`

---

Note: This document was translated from German on 2025-10-02 to comply with the English-only language policy.
