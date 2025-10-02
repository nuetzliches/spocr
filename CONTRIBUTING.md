# Contributing Guide

Thank you for your interest in SpocR! This project welcomes issues and pull requests.

## Core Principles

- Small, focused changes are easier to review.
- Before starting a major feature: open an issue and discuss it first.
- No direct commits to `main` – work through branches.

## Branch Naming Convention

```
feature/<short-description>
fix/<bug-id-or-short-description>
docs/<topic>
refactor/<area>
```

## Development Setup

Prerequisites:

- .NET 8 SDK (9 optional for main project multi-targeting)

Restore & Build:

```bash
dotnet restore
dotnet build src/SpocR.csproj
```

Quick quality check (Self-Validation):

```bash
# From repository root (recommended)
dotnet run --project src/SpocR.csproj -- test --validate

# Or if you have SpocR installed globally
spocr test --validate

# Full quality gates (build, test, coverage)
powershell -ExecutionPolicy Bypass -File eng/quality-gates.ps1
```

Run unit tests:

```bash
# Run specific test projects
dotnet test tests/SpocR.Tests
dotnet test tests/SpocR.IntegrationTests

# Run all tests via solution
dotnet test tests/Tests.sln
```

(Integration tests are now reactivated under `tests/SpocR.IntegrationTests`.)

## Pull Request Checklist

- [ ] Build successful (`dotnet build src/SpocR.csproj`)
- [ ] Self-validation passes (`dotnet run --project src/SpocR.csproj -- test --validate`)
- [ ] All tests pass (`dotnet test tests/Tests.sln`)
- [ ] Quality gates pass (`powershell -ExecutionPolicy Bypass -File eng/quality-gates.ps1`)
- [ ] If new feature: README / relevant documentation updated
- [ ] No unnecessary debug output / Console.WriteLine
- [ ] No dead files / unused usings
- [ ] Release docs unaffected or updated (if changing packaging/version logic)

## Language Policy

All source code (identifiers, comments, XML documentation), commit messages, pull request descriptions, issues, and project documentation MUST be written in clear, professional English.

Rationale:

- Enables international collaboration
- Simplifies automated analysis, search and AI assistance
- Avoids mixed-language technical ambiguity

Exceptions:

- Third‑party legal notices or license text
- Test data or domain samples that require original language
- Historic changelog / commit history (not retroactively rewritten)

If you encounter leftover German (or other language) fragments, submit a small cleanup PR.

## Code Style

- C# `latest` features allowed, but use pragmatically.
- Nullability enabled: take warnings seriously.
- Meaningful naming – no abbreviations except widely known (`db`, `sql`).

## Commit Messages

Recommended pattern (imperative):

```
feat: add simple integration test skeleton
fix: resolve NullReference in SchemaManager
refactor: simplify StoredProcedure query logic
docs: add testing section
chore: update dependencies
```

## Versioning & Release Process

Semantic Versioning (SemVer) is used: MAJOR (breaking), MINOR (features), PATCH (fixes/refinements). The effective version is derived from Git tags using **MinVer**. The `<Version>` element in the project file is no longer edited manually for normal releases.

### Tag Format

```
v<MAJOR>.<MINOR>.<PATCH>
```

Pre‑releases may use suffixes: `v4.2.0-alpha.1` etc.

### Release Steps

1. Ensure quality gates pass locally:
   ```powershell
   powershell -ExecutionPolicy Bypass -File eng/quality-gates.ps1 -SkipCoverage
   ```
2. Decide next version bump (SemVer logic). No file edit required.
3. Create & push tag:
   ```bash
   git tag v4.1.36
   git push origin v4.1.36
   ```
4. (Optional) Create a GitHub Release for changelog visibility. Publish workflow validates and pushes package if not dry-run and not already published.

### Dry Run (No Publish)

Use workflow dispatch with `dry-run=true`. You may set `override-version` to simulate packaging metadata (never published in dry run or when override provided).

### Safeguards

- Tag presence + fetch-depth=0 ensures MinVer resolves version
- Skips publish if version already on NuGet
- Deterministic build flags (`ContinuousIntegrationBuild=true`, `Deterministic=true`)
- SBOM (CycloneDX) generation

### Internals & Testing

`InternalsVisibleTo` exposes internals to `SpocR.Tests` for focused extension & helper testing.

## Security / Secrets

No credentials in commits. For local tests: use `.env` or User Secrets (not in repo).

## Contact / Discussion

Use GitHub Issues or Discussions. For major architectural changes, please create an RFC issue.

Thanks for your contribution! 🙌
