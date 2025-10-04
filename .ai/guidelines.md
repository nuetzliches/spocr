# AI Agent Guidelines for SpocR

This document provides standardized guidelines for AI agents working on the SpocR project to ensure consistency, quality, and adherence to project standards.

## 🔍 Pre-Development Checklist

### Package & Environment Verification

- [ ] **Check latest package versions** - Verify all Packages are up-to-date
- [ ] **Validate .NET SDK version** - Ensure target frameworks are current and supported
- [ ] **Review dependency compatibility** - Check for version conflicts and security vulnerabilities
- [ ] **Verify tooling versions** - Ensure MSBuild, analyzers, and development tools are current

### Style & Standards Verification

- [ ] **EditorConfig compliance** - Check for `.editorconfig` and follow defined styles
- [ ] **Coding standards** - Reference and apply project-specific coding guidelines
- [ ] **Naming conventions** - Follow established C# and project naming patterns
- [ ] **Documentation standards** - Ensure XML documentation and README compliance
- [ ] **Language** - All new / modified content (code comments, docs, commit messages) in English only

## 🛠️ Development Standards

### Code Quality Requirements

```csharp
// ✅ Good: Nullable reference types enabled
#nullable enable

// ✅ Good: Proper XML documentation
/// <summary>
/// Generates strongly typed C# classes for SQL Server stored procedures
/// </summary>
/// <param name="connectionString">Database connection string</param>
/// <returns>Generated code result</returns>
public async Task<GenerationResult> GenerateAsync(string connectionString)
{
    // Implementation
}

// ❌ Bad: No documentation, unclear naming
public async Task<object> DoStuff(string cs)
{
    // Implementation
}
```

### Testing Requirements

- [ ] **Unit tests** - All new functionality must have corresponding unit tests
- [ ] **Integration tests** - Database-related code requires integration test coverage
- [ ] **Test naming** - Use descriptive test method names: `Method_Scenario_ExpectedResult`
- [ ] **Self-validation** - Run `dotnet run --project src/SpocR.csproj -- test --validate` before commits

### Build & CI Compliance

- [ ] **Local build success** - `dotnet build src/SpocR.csproj` must pass
- [ ] **All tests pass** - `dotnet test tests/Tests.sln` must be green
- [ ] **No warnings** - Treat warnings as errors in development
- [ ] **Clean code analysis** - Address all analyzer suggestions

## 📦 Package Management

### NuGet Package Guidelines

```xml
<!-- ✅ Good: Explicit version ranges for stability -->
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.*" />

<!-- ✅ Good: Security-focused package selection -->
<PackageReference Include="Microsoft.Data.SqlClient" Version="5.1.*" />

<!-- ❌ Bad: Wildcard versions that can break builds -->
<PackageReference Include="SomePackage" Version="*" />
```

### Version Management

SpocR uses tag-driven semantic versioning via MinVer:

- Create annotated git tag `v<MAJOR>.<MINOR>.<PATCH>` to publish that version (CI workflow).
- Pre-release tags (e.g. `v5.0.0-alpha.1`) propagate to `AssemblyInformationalVersion`.
- Do NOT manually edit `<Version>` in project file; build derives it.
- Bump rules: MAJOR = breaking, MINOR = feature, PATCH = fixes/internal.

Release checklist (automation-ready):

1. Validate: `eng/quality-gates.ps1 -SkipCoverage` (or with coverage threshold).
2. Update docs / changelog if needed.
3. Tag: `git tag vX.Y.Z && git push origin vX.Y.Z`.
4. Draft Release (or rely on automated publish pipeline once enabled).

## 🔧 Architecture Guidelines

### Dependency Injection Pattern

```csharp
// ✅ Good: Constructor injection with interface
public class CodeGenerator
{
    private readonly IDbContextFactory _contextFactory;
    private readonly ILogger<CodeGenerator> _logger;

    public CodeGenerator(IDbContextFactory contextFactory, ILogger<CodeGenerator> logger)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
}
```

### Error Handling Standards

```csharp
// ✅ Good: Specific exceptions with context
throw new InvalidOperationException($"Unable to connect to database: {connectionString}");

// ✅ Good: Proper async error handling
try
{
    await ProcessAsync();
}
catch (SqlException ex) when (ex.Number == 2) // Timeout
{
    _logger.LogWarning("Database timeout occurred, retrying...");
    throw new TimeoutException("Database operation timed out", ex);
}
```

## 📝 Documentation Standards

### Code Documentation

- **Public APIs** - Must have XML documentation
- **Complex logic** - Inline comments explaining business rules
- **Configuration** - Document all configuration options
- **Examples** - Provide usage examples for new features

### Project Documentation (docs/)

- **After code changes** - Always update relevant documentation in `docs/content/`
- **Structure alignment** - Ensure docs reflect current codebase capabilities
- **Getting Started** - Update `docs/content/1.getting-started/` for new features
- **CLI Reference** - Update `docs/content/2.cli/` for command changes
- **API Reference** - Update `docs/content/3.reference/` for public API changes
- **Examples** - Add practical examples to documentation
- **Deployment** - Docs are built with Nuxt.js from `docs/` directory

### Documentation Update Checklist

- [ ] **Feature docs** - New features documented in appropriate section
- [ ] **CLI changes** - Command help and examples updated
- [ ] **Breaking changes** - Migration guides provided
- [ ] **Version alignment** - Documentation version matches code version
- [ ] **Local testing** - Run `npm run dev` in docs/ to verify changes

### Commit Message Format

```
feat: add stored procedure parameter validation
fix: resolve connection timeout in SqlDbHelper
docs: update README with new CLI commands
test: add integration tests for schema generation
chore: update NuGet packages to latest versions
refactor: reorganize exit code constants
ci: consume JSON test summary in pipeline
```

## 🚦 Quality Gates

Additions:

- Ensure `.artifacts/test-summary.json` is produced in CI mode test runs (`spocr test --validate --ci`).
- Treat any non-zero exit code per categorized mapping (see Exit Codes section below).
- Mark long-running build/rebuild/version reflection tests with `[Trait("Category","Slow")]`.

### Before Code Changes

1. **Research phase** - Understand existing patterns and conventions
2. **Planning phase** - Document approach and breaking changes
3. **Implementation phase** - Follow established patterns
4. **Validation phase** - Test thoroughly and validate against standards

### Before Commits

1. **Self-validation** - `dotnet run --project src/SpocR.csproj -- test --validate`
2. **Test execution** - `dotnet test tests/Tests.sln`
3. **Build verification** - `dotnet build src/SpocR.csproj`
4. **Documentation update** - Update relevant `docs/content/` files and verify with `npm run dev`
5. **(If releasing)** - Ensure tag will match `<Version>` in `src/SpocR.csproj` and optionally dry-run `Publish NuGet` workflow with `dry-run=true` before creating the release.

### Exit Codes (Reference)

| Code | Category      | Meaning                                             |
| ---- | ------------- | --------------------------------------------------- |
| 0    | Success       | All operations succeeded                            |
| 10   | Validation    | Structural/semantic validation failure              |
| 20   | Generation    | Code generation pipeline error (reserved)           |
| 30   | Dependency    | External dependency (DB/network) failure (reserved) |
| 40   | Testing       | Aggregate test failure (full suite)                 |
| 50   | Benchmark     | Benchmark execution failure (reserved)              |
| 60   | Rollback      | Rollback/recovery failure (reserved)                |
| 70   | Configuration | Configuration parsing/validation error (reserved)   |
| 80   | Internal      | Unhandled/internal exception                        |
| 99   | Reserved      | Future experimental use                             |

Future subcodes inside 40s may differentiate unit/integration/validation failures—avoid hard-coding those until documented.

### JSON Test Summary Artifact

When run with `--ci`, the test command writes `.artifacts/test-summary.json`:

```jsonc
{
  "mode": "validation-only",
  "timestampUtc": "<ISO8601 UTC>",
  "validation": { "total": 3, "passed": 3, "failed": 0 },
  "tests": { "total": 0, "passed": 0, "failed": 0 },
  "duration": { "totalMs": 120, "unitMs": 0, "integrationMs": 0 },
  "success": true
}
```

Added fields:

- `failed` counts for faster CI branching
- `duration` (overall + per suite) for performance baselining

Future roadmap: failure details array, suite timing breakdown, trend annotation.

### Documentation Development

Docs site uses Bun + Nuxt:

```bash
cd docs
bun install
bun run dev
```

Replace any lingering `npm run dev` references when updating docs contributions.

## 🔗 Resources

### Essential Documentation

- [CONTRIBUTING.md](../CONTRIBUTING.md) - Development setup and contribution guidelines
- [tests/docs/TESTING.md](../tests/docs/TESTING.md) - Testing framework documentation
- [docs/content/](../docs/content/) - Project documentation (Nuxt.js-based)
- [README.md](../README.md) - Project overview and quick start
- [.editorconfig](../.editorconfig) - Code formatting rules (if exists)

### External Standards

- [Microsoft C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- [.NET API Design Guidelines](https://docs.microsoft.com/en-us/dotnet/standard/design-guidelines/)
- [NuGet Package Guidelines](https://docs.microsoft.com/en-us/nuget/create-packages/package-authoring-best-practices)

---

## Snapshot & Cache Model (Project-Specific)

| Aspect             | Rule                                                                                                              |
| ------------------ | ----------------------------------------------------------------------------------------------------------------- |
| Snapshot Content   | Procedures, inputs, result sets, UDTT definitions (with column signatures) – no schema status persisted           |
| Cache Scope        | Only stored procedure definition & parse skip (per procedure ModifiedTicks); type metadata always refreshed       |
| Ignore Lists       | `ignoredSchemas` (whole schema), `ignoredProcedures` (single procedure) – do not suppress type metadata refresh   |
| Typing Pipeline    | Parser builds JSON column provenance → Stage1 UDTT columns → Stage2 base table columns → fallback `nvarchar(max)` |
| Parser Versioning  | Bump when snapshot structure or enrichment semantics materially change (affects fingerprint)                      |
| Cross-Schema Types | Always load all UDTT + table column metadata irrespective of ignore lists                                         |

### Fallback Upgrade (Planned v4)

Implement opportunistic replacement of fallback `nvarchar(max)` JSON column types when concrete types become determinable without forcing a full `--no-cache` pull.

### Contribution Implications

- Never reintroduce schema status persistence into snapshots.
- New enrichment stages must be idempotent and safe to run on hydrated (skipped) procedures if migration is required.
- Fingerprint changes must remain stable (only parser version and core counts should affect it beyond server/db/schema set).

**Last Updated:** October 5, 2025  
**Guideline Version:** 1.2  
**Applies to:** SpocR v4.1.x and later
