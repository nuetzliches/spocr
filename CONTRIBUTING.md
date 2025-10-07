# Contributing Guide

Thank you for your interest in SpocR! This project welcomes issues and pull requests.

## Core Principles

- Small, focused changes are easier to review.
- Before starting a major feature: open an issue and discuss it first.
- No direct commits to `main` – work through branches.
- A protected `develop` branch aggregates all feature and fix work prior to release stabilization.

## Branch / Workflow Model

The repository follows a lightweight trunk+integration flow:

Branches:

| Branch                 | Purpose                                                                                                        |
| ---------------------- | -------------------------------------------------------------------------------------------------------------- |
| `main`                 | Always releasable. Only fast‑forward merges from `develop` (release readiness) and hotfix PRs.                 |
| `develop`              | Integration branch for completed feature / fix PRs. May be occasionally rebased / cleaned before release cut.  |
| `feature/*`            | New features or sizeable refactors. PR -> `develop`.                                                           |
| `fix/*`                | Bug fixes. PR -> `develop` (or hotfix -> `main` if urgent).                                                    |
| `docs/*`               | Documentation only changes. PR -> `develop` (or `main` if purely editorial).                                   |
| `release/*` (optional) | Short‑lived stabilization branch if a larger batch needs hardening; merges back to `main` then into `develop`. |

Rules:

1. Open an issue (or link an existing one) for any non‑trivial feature.
2. Branch from `develop` for normal work; branch from `main` only for emergency hotfixes.
3. Keep PRs small & focused; rebase onto latest `develop` before requesting review.
4. CI must be green (build + tests + quality gates) before merge.
5. Squash merge to keep history tidy (unless a release branch merge – then use merge commit to preserve context).
6. After a release: tag on `main`, then fast‑forward `develop` if diverged.

Protection Suggestions (configure in repository settings):

- `main`: require PR, status checks, linear history (optional), signed commits (optional).
- `develop`: require PR + status checks; allow fast‑forward only or squash merges.
- Disallow force pushes except for administrators (avoid rewriting public history).

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

AI / Automation:

Automated agents or scripts contributing code must also adhere to the English-only rule and SHOULD reference `.ai/guidelines.md` (if present) for guardrails (naming, idempotent changes, safety). If the file is missing, create one before large-scale automated refactors.

### Script Formatting & Generation Guidelines (Automation)

When generating or reviewing automation scripts (PowerShell or Windows batch) the following rules apply:

- Always target a single shell per snippet: either PowerShell (`powershell` code fence) or classic cmd (`cmd` fence); do not mix syntaxes.
- Use fenced code blocks with language hint and no inline commentary inside the block; explanations belong above/below.
- PowerShell style: 4 spaces indentation, no artificial line wraps; use backticks only for *intentional* logical line continuation.
- Batch (cmd) style: avoid PowerShell constructs; prefer simple `IF` one-liners or clearly grouped parentheses blocks; no `%ERRORLEVEL%` suppression unless justified.
- Avoid exotic Unicode whitespace; standard ASCII spaces only.
- Ensure idempotency: check existence before creating/removing files or directories.
- Emit explicit non-zero exit codes on errors (`throw` in PowerShell, `exit /b 1` in cmd) and fail fast.
- Keep variable naming consistent (PascalCase or camelCase in PowerShell; UPPER_SNAKE for env-like constants in cmd).
- Do not silently swallow errors; prefer `Set-StrictMode -Version Latest` (PowerShell) for complex scripts.
- Long-running scripts should log key phases with timestamps.
- Never embed secrets; reference environment variables or secure stores.

These conventions improve readability, reduce CI friction, and make AI-assisted changes safer.

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

## Local Procedure Metadata Cache

The schema load step maintains a lightweight local cache under `.spocr/cache/<fingerprint>.json` to avoid re-querying stored procedure definitions when they haven't changed.

What is cached:

- Procedure name, schema, last `ModifiedTicks`
- Input parameters (including table types & output flags)
- Parsed / synthesized result sets (JSON flags, columns)

Skip Conditions:

- If `ModifiedTicks` in SQL (`sys.objects.modify_date`) is unchanged, definition + inputs + result set shape are hydrated from cache (no definition / input queries executed).

When it does NOT skip:

- Procedure altered (ticks differ)
- `--no-cache` flag supplied
- Cache fingerprint changed (schema selection, project id, procedure count)

Force re-parse:

```
spocr build --path <config> --no-cache
```

Troubleshooting:

- Use `--verbose` to see `[proc-skip]` vs `[proc-loaded]` lines.
- Delete `.spocr/cache` if fingerprint collisions are suspected.

Planned Improvements:

- Incorporate database name/connection hash into fingerprint
- Optional size/time metrics per skip vs load cycle
