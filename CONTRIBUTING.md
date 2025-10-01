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
spocr test --validate
```

Run unit tests:

```bash
dotnet test tests/SpocR.Tests
```

(Integration tests will be reactivated later under `tests/SpocR.IntegrationTests`.)

## Pull Request Checklist

- [ ] Build successful (`dotnet build`)
- [ ] `spocr test --validate` passes without errors
- [ ] If new feature: README / relevant documentation updated
- [ ] No unnecessary debug output / Console.WriteLine
- [ ] No dead files / unused usings

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

## Versioning

Patch version is automatically incremented during build (MSBuild target). For major version changes, please mention in the PR.

## Security / Secrets

No credentials in commits. For local tests: use `.env` or User Secrets (not in repo).

## Contact / Discussion

Use GitHub Issues or Discussions. For major architectural changes, please create an RFC issue.

Thanks for your contribution! 🙌
