# SpocR

[![NuGet](https://img.shields.io/nuget/v/SpocR.svg)](https://www.nuget.org/packages/SpocR)
[![NuGet Downloads](https://img.shields.io/nuget/dt/SpocR.svg)](https://www.nuget.org/packages/SpocR)
[![License](https://img.shields.io/github/license/nuetzliches/spocr.svg)](LICENSE)
[![Build Status](https://img.shields.io/github/actions/workflow/status/nuetzliches/spocr/test.yml?branch=main)](https://github.com/nuetzliches/spocr/actions)
[![Code Coverage](https://img.shields.io/badge/coverage-pending%20gate-lightgrey)](https://github.com/nuetzliches/spocr/actions)

**SpocR** is a powerful code generator for SQL Server stored procedures that creates strongly typed C# classes for inputs, models, and execution. Eliminate boilerplate data access code and increase type safety in your .NET applications.

## Features

- Type-Safe Code Generation: Strongly typed inputs, models & execution extensions
- Zero Boilerplate: Remove manual ADO.NET plumbing and mapping
- Fast Integration: Add to an existing solution in minutes
- Extensible Templates: Naming, output layout & model generation are customizable
- JSON Stored Procedures (Alpha): Raw + typed generation for first JSON result set (see section below)
- JSON Column Typing via UDTT Mapping: Deterministic typing using table types & base table provenance
- CI/CD Ready: Machine-readable test summaries, exit codes & (optional) JUnit XML
- Snapshot-Only Architecture: Fingerprinted schema snapshots decouple discovery from config
- Pull Cache & `--no-cache`: Fast iteration with explicit full re-parse when needed
- Ignored Schemas List: Concise `ignoredSchemas` replaces legacy per-schema status node

## Quick Start

### Installation

Install SpocR as a global .NET tool:

```bash
dotnet tool install --global SpocR
```

### Basic Usage

````bash
# Initialize generator configuration (bridge phase)
spocr init --namespace MyCompany.MyProject --mode dual --connection "Server=.;Database=MyDb;Trusted_Connection=True;" --schemas core,identity

```csharp
var command = new SqlCommand("EXEC GetUserById", connection);
command.Parameters.AddWithValue("@UserId", 123);
var reader = await command.ExecuteReaderAsync();
// ... manual mapping code
````

**With SpocR** (generated, type-safe):

```csharp
var context = new GeneratedDbContext(connectionString);
var result = await context.GetUserByIdAsync(new GetUserByIdInput {
		UserId = 123
});
```

### Migration Note: `spocr init` vs. legacy `spocr create`

During the v4.5 bridge phase a new initialization command `spocr init` is available and will fully replace `spocr create` in v5. Use `init` to create or update a `.env` file rather than generating a legacy `spocr.json` skeleton. The `.env` bootstrap merges a template (see `samples/restapi/.env.example`) with inferred values (schemas, optional namespace) and supports idempotent key upsert.

| Legacy (`create`)              | New (`init`) Flag / Behavior                                   |
| ------------------------------ | -------------------------------------------------------------- | ---- | ------------------------------- |
| `--project <Name>`             | Use `--namespace` to set root namespace (auto inference else)  |
| (writes `spocr.json`)          | Writes/updates `.env` (generator scope only)                   |
| (no schema allow-list merging) | `--schemas core,banking` populates `SPOCR_BUILD_SCHEMAS`       |
| (no explicit mode control)     | `--mode legacy                                                 | dual | next`sets`SPOCR_GENERATOR_MODE` |
| (connection via config field)  | `--connection <conn>` sets `SPOCR_GENERATOR_DB` (generator DB) |
| (no force overwrite flag)      | `--force` rewrites existing `.env` preserving comments         |

Deprecation Path:

- v4.5: `spocr create` still present but marked deprecated (warning to be added before v5).
- v5: `spocr create` removed; `.env` becomes authoritative; `spocr.json` ignored for namespace/mode/db unless running in strict legacy mode (legacy mode itself may be removed soon thereafter).

Benefits of switching now:

- Deterministic precedence (CLI > ENV > .env > legacy fallback) already active.
- Easier CI injection: set env vars or pass CLI flags without editing JSON.
- Reduced churn: `.env` upsert preserves comments and ordering (template driven).

Refer to documentation page "Env Bootstrap" for full details on template structure and precedence chain.

## Documentation

For comprehensive documentation, examples, and advanced configuration:

**[Visit the SpocR Documentation](https://nuetzliches.github.io/spocr/)**  
Key deep-dives: [ResultSet Naming](https://nuetzliches.github.io/spocr/3.reference/result-set-naming) · [Table Types](https://nuetzliches.github.io/spocr/3.reference/table-types)

> Bridge Phase (v4.5 → v5)
>
> The project is currently in a transitional "Bridge Phase" (planned final minor before v5). A legacy DataContext generator is still present while the new `SpocRVNext` pipeline matures. Unless you explicitly opt in, behavior remains stable.
>
> Key points:
>
> - Legacy Freeze: Non-critical functional changes to the legacy generator are frozen (only security / stability fixes).
> - Dual Generation: You can run both pipelines in parallel (`SPOCR_GENERATOR_MODE=dual` - DEFAULT in v4.5) to observe new output without impacting existing code.
> - Opt-In Flags: New CLI parser & strict modes are guarded behind environment variables / flags (see `samples/restapi/.env.example`).
> - Cutover Plan: v5 will remove the legacy DataContext and obsolete configuration properties. Migration notes will be published ahead of the release.
>
> Recommended now: inspect the future output, and open issues for gaps / blockers you discover.

### Major Version Bridge Policy (Upgrade Safety)

To reduce accidental skips over required transitional releases, SpocR enforces a _bridge policy_ for major upgrades:

1. When a newer **major** version is detected, direct upgrade offers are suppressed unless you are already on the latest minor of your current major.
2. The tool prints guidance to first move to the highest available minor ("bridge release") before crossing the major boundary.
3. You can explicitly override (NOT generally recommended) via environment variable:

```bash
# Linux/macOS
export SPOCR_ALLOW_DIRECT_MAJOR=1

# Windows (cmd)
set SPOCR_ALLOW_DIRECT_MAJOR=1

# Windows (PowerShell)
$env:SPOCR_ALLOW_DIRECT_MAJOR=1
```

Accepted truthy values: `1`, `true`, `yes`, `on` (case‑insensitive). The override is intended only for CI experiments, controlled integration test matrices, or when you have validated migration steps manually.

Rationale: Major releases may remove deprecated configuration or legacy generation paths (e.g. DataContext v4 → v5). A mandatory bridge minimizes silent breakage and gives you a well-documented cutover point.

Tracking: See CHANGELOG ("Bridge Policy") and `MIGRATION_SpocRVNext.md` for the detailed logic.

### Configuration Precedence

Configuration values (GENERATOR SCOPE – Codegenerierung) are resolved using a clear, deterministic order:

```
CLI arguments  >  Explicit --set / parser overrides  >  Environment variables  >  .env file  >  spocr.json (legacy fallback)  >  Internal defaults
```

Illustrated example (Namespace resolution):

1. `spocr generate --namespace My.Root` (highest) → wins
2. Else `SPOCR_NAMESPACE=EnvValue` → wins
3. Else `.env` line `SPOCR_NAMESPACE=DotEnvValue` → wins
4. Else `project.output.namespace` inside `spocr.json`
5. Else inferred from directory name (auto namespace feature)

Generator mode precedence is analogous (`--mode` / `SPOCR_GENERATOR_MODE` / `.env` / default = `dual` during v4.5 bridge).

Runtime note:

The `.env` file is used ONLY for generator / CLI configuration (e.g. mode, namespace override). Runtime database connections are NOT supplied via `SPOCR_*` env variables, but exclusively inside your host application via:

```csharp
builder.Services.AddSpocRDbContext(o =>
{
  o.ConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
});
```

This removes any need for a dedicated runtime DB environment variable. Secrets / connection strings stay in `appsettings.*.json`, secret stores or infrastructure-provided environment variables (not specially processed by SpocR).

Invalid modes (anything not `legacy|dual|next`) trigger an exception early with a clear message (tested in `EnvConfigurationTests`).

Benefits:

- Predictable overrides in CI pipelines
- Safe experimentation via ephemeral CLI flags
- Gradual deprecation path for `spocr.json` fields targeted for removal in v5

Referenced Tests: `EnvConfigurationTests` (precedence + invalid mode) and `BridgePolicyTests` (upgrade gating & override variable).

### Procedure Execution Interceptors (vNext)

SpocR vNext provides a global interceptor surface for stored procedure executions enabling lightweight logging and telemetry without modifying generated code.

Register an interceptor at application startup:

```csharp
using SpocR.SpocRVNext.Execution;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSpocRDbContext(o =>
{
  o.ConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
});

var app = builder.Build();
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SpocR.Proc");
ProcedureExecutor.SetInterceptor(new LoggingProcedureInterceptor(logger));

app.MapGet("/api/users", async (ISpocRDbContext db, CancellationToken ct) =>
{
  var result = await db.UserListAsync(ct).ConfigureAwait(false);
  return result.Success ? Results.Ok(result.Result1) : Results.Problem(result.Error);
});

app.Run();
```

Sample log output:

```
spocr.proc.executed samples.UserList duration_ms=12.34 params=0 success=True
spocr.proc.failed dbo.UserDetailsWithOrders duration_ms=18.77 params=2 success=False error="Timeout expired"
```

Guidelines:

- Keep interceptor work minimal (no blocking I/O). Exceptions in `OnAfterExecuteAsync` are swallowed.
- Global static registration keeps overhead negligible; DI-scoped interceptors may be added later for correlation IDs.
- For tracing (OpenTelemetry) create a custom interceptor starting/stopping spans around `ExecuteAsync`.

See `DEVELOPMENT.md` for full lifecycle and hook details.

### Namespace Derivation & Override

SpocR derives a default root namespace for generated code when you do not explicitly supply one. The resolution order follows the same precedence chain shown above and is tested (see `EnvConfigurationTests`):

```
CLI --namespace flag > SPOCR_NAMESPACE environment variable > .env line SPOCR_NAMESPACE=... > project.output.namespace in spocr.json > inferred from containing directory name
```

Inference Rule:

- If no explicit value is provided, the last segment of the project root directory is sanitized into a valid C# namespace identifier (non‑alphanumeric characters removed, leading digits prefixed with `_`).
- Example: directory `c:\work\my-company.service` ⇒ inferred namespace `mycompanyservice` (then PascalCase applied per generator style if required).

Override Examples:

1. CLI (highest):

```
spocr generate --namespace My.Company.Service
```

2. Environment variable (Windows cmd):

```
set SPOCR_NAMESPACE=My.Company.Service
spocr generate
```

3. .env file (checked into repo or provided in CI working directory):

```
SPOCR_NAMESPACE=My.Company.Service
```

4. Legacy `spocr.json` (bridge, slated for removal in v5):

```jsonc
{
  "project": {
    "output": {
      "namespace": "My.Company.Service"
    }
  }
}
```

Validation:

- Empty or whitespace values are ignored (next source considered).
- Values containing invalid characters are sanitized. If the sanitized result is empty or begins with an invalid start character, an exception is thrown early.
- Leading digits: `123Service` becomes `_123Service`.
- Disallowed characters: `My-Service.Core` becomes `MyServiceCore`.

Failure Modes (all produce clear messages):

- Providing a value with only invalid characters (e.g. `---`).
- Providing a value longer than typical namespace limits after sanitization (guard rails > 512 chars, rare case).

Recommended Practice:

- Prefer CLI override in ephemeral automation (release pipelines) for clarity.
- Use `.env` for local developer experimentation without committing changes to `spocr.json`.
- Migrate any remaining `spocr.json` namespace fields before v5; they will be ignored once the bridge period ends.

Next Steps (Roadmap):

- v5: Remove legacy `project.output.namespace` fallback; rely only on CLI / env / .env.
- Add analyzer hint when both CLI and env variable specify differing namespaces (warn about potential confusion).

Referenced Tests: `NamespaceResolverTests`, `EnvConfigurationTests`.

### ResultSet Naming Strategy

SpocR assigns human-friendly names to result sets returned by stored procedures in the vNext pipeline. The naming resolver is always on (bridge phase) and replaces generic names only when it can do so safely and deterministically.

#### Goals

- Produce stable, descriptive result set identifiers (improves generated record readability)
- Avoid misleading names for dynamic or opaque SQL bodies
- Guarantee uniqueness per procedure without introducing churn

#### Algorithm (Current v4.5 Bridge)

1. Pre‑Scan Dynamic SQL: If the procedure text contains typical dynamic patterns (`sp_executesql`, `EXEC(@`, `EXEC (@`, `execute(@`) the resolver **skips** suggesting names (keeps generic `ResultSetN`).
2. Parse First SELECT: Using the TSql parser, the resolver inspects the **first top‑level SELECT** statement.
3. Extract Base Table: The first named table reference (e.g. `dbo.Users`) becomes the base suggestion (`Users`). Aliases are ignored at this stage (future enhancement may leverage them).
4. Sanitize: Invalid characters are stripped; PascalCase preserved from source where applicable.
5. Collision Handling: If multiple result sets would share the same base name, suffixes are appended: `Users`, `Users1`, `Users2`, ... (first occurrence unsuffixed).
6. Fallback: If parsing fails, no named table is found, or dynamic SQL is detected, original generic names remain (`ResultSet1`, `ResultSet2`, ...).

The resolver never renames a result set to an empty or invalid identifier; it simply leaves the generic name in place in failure or ambiguity scenarios.

#### Examples

| SQL Pattern                                                   | Resolver Outcome            | Notes                                                    |
| ------------------------------------------------------------- | --------------------------- | -------------------------------------------------------- |
| `SELECT u.Id, u.Name FROM dbo.Users u`                        | `Users`                     | Base table extracted from first SELECT                   |
| Two SELECTs both from `dbo.Users`                             | `Users`, `Users1`           | Duplicate suffix applied starting at 1                   |
| Three SELECTs: `Users`, `Users`, `Users`                      | `Users`, `Users1`, `Users2` | Stable sequence; deterministic ordering tests cover this |
| Dynamic SQL: `EXEC(@sql)` or `sp_executesql N'...SELECT ...'` | `ResultSet1`, `ResultSet2`  | Skip naming – dynamic content could vary                 |
| Unparsable / exotic T‑SQL (parser error)                      | Generic names               | Safety first (no partial heuristics)                     |

#### Rationale for Suffix Strategy

Appending numeric suffixes starting from 1 (leaving the first occurrence unsuffixed) yields a concise, stable naming surface without implying ordering semantics beyond distinction. This mirrors common collection naming patterns while minimizing visual noise.

#### Determinism Guarantees

- Ordering tests (`UnifiedProcedureOrderingTests`) ensure record sections (Inputs → Outputs → ResultSets → Aggregate → Plan → Executor) appear predictably.
- Multi‑result naming tests (`MultiResultSetNamingTests`) assert suffix behavior.
- Dynamic SQL tests (`ResultSetNameResolverDynamicSqlTests`) validate protective skipping.

#### Deferred / Planned Enhancements

Planned improvements tracked in the checklist (may land post‑bridge):

- CTE Support: Derive the base table from the final query inside Common Table Expressions (deferred to v5.0).
- FOR JSON PATH Root Alias Extraction: Use explicit JSON root aliases when present as the name.
- Collision Edge Cases: Extended tests for procedures selecting from different tables with same sanitized name.
- Performance & Caching: Reuse a single parser instance (micro‑benchmark + reuse strategy).
- Expanded Snapshot SQL Field: Capture full original SQL text (`Sql`/`Definition`) to improve heuristics.

#### FAQ

Q: Why not analyze all SELECT statements for distinct names?
A: Focusing on the first SELECT avoids unstable naming when procedures branch conditionally or build dynamic segments later. Simplicity keeps determinism predictable.

Q: Will enabling CTE support rename existing sets?
A: Only procedures whose first SELECT is a CTE wrapper would gain a more accurate base name. Activation will be versioned & documented to manage any diff.

Q: Can I force generic names even for static SQL?
A: A disable flag is planned. For now, you can rely on generic names by introducing dynamic SQL patterns, but this is **not** recommended purely for naming suppression.

#### Troubleshooting

- Unexpected generic name? Confirm no dynamic SQL pattern exists early in the procedure text.
- Unwanted suffixes? Multiple identical base table references produce them by design; verify result set count.
- Parser failure warnings? (When verbose logging added) Treat as a signal to inspect complex T‑SQL (nested, batch constructs). Generic fallback is safe.

If you encounter misleading names or have edge cases (e.g. table variables, temp tables) open an issue with sample SQL – include whether dynamic patterns were present.

Referenced Tests: `MultiResultSetNamingTests`, `ResultSetNameResolverDynamicSqlTests`, `UnifiedProcedureOrderingTests`.

Roadmap cross‑links: See checklist section "ResultSetNameResolver Improvements" for real‑time status of planned enhancements.

### Migration Note: Removal of Legacy `Output`

Older snapshots exposed a root-level `Output` array for JSON-returning procedures. This was removed in favor of a unified `ResultSets` model. Update any tooling referencing `Output` to:

```csharp
var rs0 = snapshot.StoredProcedures[procName].ResultSets[0];
foreach (var col in rs0.Columns) { /* ... */ }
```

This change eliminates divergence between scalar and JSON procedures and reduces surface area.

## JSON Stored Procedures (Alpha)

SpocR identifies a stored procedure as JSON-capable when its first result set is recognized as JSON (heuristic & metadata driven). Historical naming patterns like `*AsJson` are treated only as hints; they are **not** required. Two method layers are generated:

| Method                     | Purpose                                              |
| -------------------------- | ---------------------------------------------------- |
| `UserListAsync` (raw)      | Returns the raw JSON payload as `string`             |
| `UserListDeserializeAsync` | Deserializes JSON into a strongly typed model / list |

Key design points:

1. Raw + Typed Separation: Choose raw JSON for pass-through or typed for domain logic.
2. Internal Helper: Deserialization uses an internal helper (subject to change while in alpha).
3. Empty Model Fallback: If inference fails (wildcards / dynamic SQL), an empty model with XML doc rationale is emitted.
4. Array vs Single Detection: Heuristics support `WITHOUT ARRAY WRAPPER`.
5. Planned: Serializer options overloads, streaming for large arrays, richer nested model inference.

CLI Listing:

```
spocr sp ls --schema dbo --json
```

Always returns a valid JSON array (e.g. `[]` or objects). Use `--json` to suppress human warnings for scripted consumption.

> Note: This feature is alpha. The helper layer, naming, and heuristics may evolve before a stable (non-alpha) release. Pin the tool version or review the changelog when upgrading.

## Testing & Quality

SpocR provides a layered quality & verification stack with machine-readable reporting for CI.

| Layer            | Purpose                                        | Command / Entry Point                      |
| ---------------- | ---------------------------------------------- | ------------------------------------------ |
| Validation       | Static / structural project checks (Roslyn)    | `spocr test --validate`                    |
| Unit Tests       | Generators, helpers, extensions, core logic    | `dotnet test tests/SpocR.Tests`            |
| Integration (\*) | DB & end-to-end stored procedure roundtrips    | `dotnet test tests/SpocR.IntegrationTests` |
| Full Suite       | Orchestrated validation + unit (+ integration) | `spocr test --ci`                          |

(\*) Integration suite currently deferred; orchestration scaffold in place.

### Core Commands

```bash
# Validation only (fast)
spocr test --validate

# Full suite (produces JSON + optional JUnit if requested)
spocr test --ci --junit

# Limit phases (comma-separated: unit,integration,validation)
spocr test --ci --only unit,validation

# Skip validation phase
spocr test --ci --no-validation
```

### Pull Caching & Forcing a Fresh Parse

`spocr pull` maintains an internal cache keyed by last modification ticks of each stored procedure to skip unchanged definitions. This keeps repeat pulls fast on large databases.

Use the flag:

```
spocr pull --no-cache
```

to deliberately bypass both loading and saving the cache. This ensures every stored procedure is fully re-fetched and re-parsed (useful directly after changing parsing heuristics or when validating JSON metadata like `WITHOUT ARRAY WRAPPER`).

Verbose output with `--verbose` will show `[proc-loaded]` lines for every procedure when `--no-cache` is active (no `[proc-skip]` entries appear) and a `[cache] Disabled (--no-cache)` banner.

### Snapshot Maintenance

Schema metadata used for generation is persisted as fingerprinted snapshot files under `.spocr/schema/`. Over time older snapshots can accumulate (each pull that detects changes writes a new file). Use:

```
spocr snapshot clean # keep latest 5 (default retention)
spocr snapshot clean --keep 10 # keep latest 10
spocr snapshot clean --all # delete all snapshot files
spocr snapshot clean -d # dry-run: show what would be deleted
```

Snapshots are small JSON documents (procedures, result sets, UDTTs, stats). Retaining a short history can help diffing parser outcomes; prune aggressively in CI.

### Ignoring Schemas (Snapshot-Only Mode)

Older SpocR versions persisted a full `schema` array inside `spocr.json` with per-schema status values (Build / Ignore). This has been replaced by:

```
{
"project": {
"defaultSchemaStatus": "Build",
"ignoredSchemas": [ "audit", "hangfire" ]
}
}
```

Rules:

1. Every discovered DB schema defaults to `defaultSchemaStatus`.
2. Any name present (case-insensitive) in `ignoredSchemas` is skipped entirely.
3. The legacy `schema` node is migrated automatically on the first `spocr pull` after upgrading (ignored entries become `ignoredSchemas`; others are discarded). The node is then removed.
4. Subsequent pulls never write the legacy node again; snapshots + `ignoredSchemas` are authoritative.

Rationale:

- Smaller, stable configuration surface
- Deterministic generator inputs (snapshot + explicit ignores)
- Faster config diffs and fewer merge conflicts

Migration Output Example (`--verbose`):

```
[migration] Collected 2 ignored schema name(s) into Project.IgnoredSchemas
[migration] Legacy 'schema' node removed; snapshot + IgnoredSchemas are now authoritative.
```

If you need to newly ignore a schema later, append it to the `ignoredSchemas` list and re-run `spocr pull` (a new snapshot fingerprint will be generated if affected procedures change).

### Selecting Build Schemas (Positive Allow-List)

In addition to excluding schemas via `ignoredSchemas`, you can positively target only specific schemas for generation using the environment variable / .env entry:

```
SPOCR_BUILD_SCHEMAS=core,app,identity
```

Rules:

1. When `SPOCR_BUILD_SCHEMAS` is set and non-empty, ONLY those schemas are included in generation (vNext pipeline). The ignored list is bypassed for those names; any other schema (even if not ignored) is skipped.
2. If `SPOCR_BUILD_SCHEMAS` is empty or not present, the generator falls back to normal behavior: include all discovered schemas except those in `ignoredSchemas` (or with legacy `Status=Ignore`).
3. Separator characters: comma `,` or semicolon `;` are both accepted (they can be mixed).
4. Validation: Names must match pattern `^[A-Za-z_][A-Za-z0-9_]*$` – invalid entries raise an early configuration exception.
5. Prefill: The `.env` bootstrap process auto-populates `SPOCR_BUILD_SCHEMAS` with any schemas marked `Status=Build` in a legacy `schema` array (bridge scenario). If none are present a commented placeholder is added.

Why use a positive list?

- Faster iteration when focusing on a subset of large database schemas
- Deterministic CI artifacts by locking to a known set of stable schemas
- Cleaner deprecation path: you can shrink the build surface before removing procedures physically

Example `.env` excerpt:

```env
SPOCR_GENERATOR_MODE=dual
SPOCR_NAMESPACE=MyCompany.Project.Data
# Only generate the stable v1 contract schemas while refactoring others
SPOCR_BUILD_SCHEMAS=core,soap,banking
```

To revert to full generation (minus ignores) simply remove or comment out the line.

### Deprecation: `Project.Role / RoleKindEnum`

`Project.Role.Kind` is deprecated and scheduled for removal in **v5**. The generator now always behaves as if `Kind = Default`.

Reasons:

1. Previous values (`Default`, `Lib`, `Extension`) created divergent generation branches with marginal value.
2. Reduced configuration surface leads to more predictable output.
3. Encourages explicit composition via DI (extension methods / service registrations) instead of implicit role flags.

Migration:
Remove the entire `role` node when it only contains `kind: "Default"` (and no `libNamespace`). Non-default values trigger a console warning and are ignored.

Before:

```jsonc
{
  "project": {
    "role": { "kind": "Default" },
    "output": {
      "namespace": "My.App",
      "dataContext": { "path": "./DataContext" }
    }
  }
}
```

After:

```jsonc
{
  "project": {
    "output": {
      "namespace": "My.App",
      "dataContext": { "path": "./DataContext" }
    }
  }
}
```

Console Warning Policy:

- Until v5 a warning appears if `role.kind` is `Lib` or `Extension`.
- In v5 the enum and node will be removed entirely; legacy configs will be auto-normalized.

Tracking: See CHANGELOG entry under the deprecation section for progress and final removal PR link once merged.

### JSON Summary Artifact

When run with `--ci`, a rich summary is written to `.artifacts/test-summary.json`.

Key fields (subset):

| Field                                                | Description                                                          |
| ---------------------------------------------------- | -------------------------------------------------------------------- |
| `mode`                                               | `full-suite` or `validation-only`                                    |
| `tests.total` / `tests.unit.total`                   | Aggregated & per-suite counts                                        |
| `tests.unit.failed` / `tests.integration.failed`     | Failure counts per suite                                             |
| `duration.unitMs` / `integrationMs` / `validationMs` | Phase durations (ms)                                                 |
| `failureDetails[]`                                   | Objects with `name` & `message` for failed tests                     |
| `startedAtUtc` / `endedAtUtc`                        | Wall clock boundaries                                                |
| `success`                                            | Overall success flag (all selected phases passed & tests discovered) |

Use this file for CI gating instead of scraping console output.

### JUnit XML (Experimental Single-Suite)

Add `--junit` to emit an aggregate JUnit-style XML (`.artifacts/junit-results.xml`).
Multi-suite XML (separate unit/integration `<testsuite>` elements) is planned; track progress in the roadmap.

### Exit Codes (Testing)

SpocR specializes test failures:

| Code | Meaning                  |
| ---- | ------------------------ |
| 41   | Unit test failure        |
| 42   | Integration test failure |
| 43   | Validation failure       |

If multiple fail: precedence is 41 > 42 > 43; otherwise aggregate 40 is used.

### Failure Summaries

Console output lists up to 10 failing tests (with suite tag). Stack trace inclusion is a planned enhancement.

### Quality Gates Script

```powershell
powershell -ExecutionPolicy Bypass -File eng/quality-gates.ps1 -CoverageThreshold 60
```

Artifacts (JSON summary, JUnit XML, coverage) live under `.artifacts/` (git-ignored).

Roadmap reference: see [Testing Framework](/roadmap/testing-framework) for remaining open enhancements.

### Coverage Policy

Bridge phase target coverage is being raised incrementally. Current enforced threshold (via `eng/quality-gates.ps1`) defaults to 80% line coverage for core logic; planned escalation path: 60% (initial) → 80% (current) → 85%+ (post cutover). A public coverage badge will switch from "pending gate" once stabilized.

SpocR enforces a line coverage quality gate in CI. We deliberately start with a modest threshold to allow incremental, sustainable improvement without blocking unrelated contributions.

Current policy:

| Item                       | Value                                         |
| -------------------------- | --------------------------------------------- |
| Initial enforced threshold | 30% (line coverage)                           |
| Target (medium-term)       | 50%                                           |
| Target (long-term)         | 60%+                                          |
| Gate location              | GitHub Action `test.yml` (`COVERAGE_MIN` env) |

Rationale:

1. Avoid “big bang” coverage pushes that add low‑value tests.
2. Encourage focused tests around generators, parsing, and validation logic (highest defect risk).
3. Provide transparent, reviewable increments (raise `COVERAGE_MIN` only after genuine improvements).

Raising the threshold:

1. Add meaningful tests (prefer logic / edge cases, avoid trivial property getters).
2. Run the coverage job locally: `dotnet test --collect:"XPlat Code Coverage"` and generate report with ReportGenerator.
3. Confirm new percentage in the coverage job artifact.
4. Update `COVERAGE_MIN` (e.g. from 30 to 35) in `.github/workflows/test.yml`.

Exclusions (future): We may introduce targeted exclusions for generated or template scaffolding code if it becomes a drag on achieving the threshold without improving confidence.

If the gate fails:

- Check `.artifacts/coverage/Summary.xml` or fallback Cobertura files in `.artifacts/test-results/`.
- The workflow step prints the derived coverage rate and which path was used.

To experiment locally without failing CI, you can temporarily export a lower threshold before running the workflow logic:

```bash
export COVERAGE_MIN=25
```

(Do not commit a lower threshold unless agreed in review.)

We track incremental increases in the CHANGELOG to make coverage progression transparent.

## Release & Publishing

Releases are published automatically to NuGet when a GitHub Release is created with a tag matching the pattern:

```
v<semantic-version>
```

Example: `v4.1.36` will publish package version `4.1.36` if not already present on NuGet.

Key safeguards:

- Tag/version match validation
- Skip if version already published
- Deterministic build flags (`ContinuousIntegrationBuild=true`, `Deterministic=true`)
- SBOM generation (CycloneDX) uploaded as artifact

### Dry Run (Manual Test of Pipeline)

You can test the release workflow without publishing:

1. GitHub > Actions > `Publish NuGet`
2. Run workflow (leave `dry-run=true`)
3. (Optional) Set `override-version` (e.g. `9.9.9-local`) to simulate a different output

The workflow builds, validates and tests but skips the publish step.

### Local Pre-Release Validation

```powershell
powershell -ExecutionPolicy Bypass -File eng/quality-gates.ps1 -SkipCoverage
```

Then create a tag & release once green:

```bash
git tag v4.1.36
git push origin v4.1.36
```

### Versioning

Semantic versions are derived from Git tags using [MinVer](https://github.com/adamralph/minver).

Tag format:

```
v<MAJOR>.<MINOR>.<PATCH>
```

Examples:

| Git Tag   | NuGet Package Version |
| --------- | --------------------- |
| `v4.1.36` | 4.1.36                |
| `v5.0.0`  | 5.0.0                 |

If you create a pre-release tag (e.g. `v4.2.0-alpha.1`), that version flows into the package.

Workflow:

1. Ensure tests & validation are green (`eng/quality-gates.ps1`).
2. Decide version bump (SemVer): MAJOR (breaking), MINOR (features), PATCH (fixes).
3. Create & push tag: `git tag vX.Y.Z && git push origin vX.Y.Z`.
4. Draft GitHub Release using that tag (or let automation publish on tag if configured in future).

The project file no longer auto-increments version numbers; builds are reproducible from tags.

## Exit Codes

SpocR uses categorized, spaced exit codes to allow future expansion without breaking CI consumers.

| Code | Category        | Meaning / Usage                                    | Emitted Now                | Notes                                                |
| ---- | --------------- | -------------------------------------------------- | -------------------------- | ---------------------------------------------------- |
| 0    | Success         | Successful execution                               | Yes                        | Stable                                               |
| 10   | Validation      | Validation / user input failure                    | Yes (validate path)        |                                                      |
| 20   | Generation      | Code generation pipeline error                     | No                         | Reserved                                             |
| 21   | Determinism     | Golden Hash diff violation (strict mode)           | No (informational only)    | Activates once strict mode enabled                   |
| 22   | Determinism     | Golden Hash integrity failure (manifest malformed) | No                         | Hard failure – indicates corruption                  |
| 23   | Determinism     | Diff allow-list violation (unexpected pattern)     | No                         | Enforced when strict mode + allow-list freeze active |
| 30   | Dependency      | External system (DB/network) failure               | No                         | Reserved                                             |
| 40   | Testing         | Test suite failure (aggregate)                     | Yes                        | 41=Unit, 42=Integration, 43=Validation               |
| 41   | Testing         | Unit test failure                                  | Yes (unit failures)        | More specific than 40                                |
| 42   | Testing         | Integration test failure                           | Yes (integration failures) | Falls back to 40 if ambiguous                        |
| 43   | Testing         | Validation test failure                            | Yes (validation failures)  | Structural / repository validation                   |
| 50   | Benchmark       | Benchmark execution failure                        | No                         | Reserved (flag present, impl pending)                |
| 60   | Rollback        | Rollback / recovery failed                         | No                         | Reserved                                             |
| 70   | Configuration   | Config parsing/validation error                    | No                         | Reserved                                             |
| 80   | Internal        | Unexpected unhandled exception                     | Yes (Program.cs catch)     | Critical – file issue/bug                            |
| 99   | Future/Reserved | Experimental / feature-flag reserved space         | No                         | Avoid relying on this                                |

Guidance:

- Treat any non-zero as failure if you do not need granularity.
- To react specifically: validation remediation (10), test failure investigation (40), file an issue for 80 (internal error).
- Future minor releases may add sub-codes inside the 40s without altering existing meanings.

### CI JSON Summary

When running with `--ci`, SpocR writes a machine-readable summary to `.artifacts/test-summary.json`:

```jsonc
{
  "mode": "full-suite", // or validation-only
  "timestampUtc": "2025-10-02T12:34:56Z",
  "startedAtUtc": "2025-10-02T12:34:50Z",
  "endedAtUtc": "2025-10-02T12:34:56Z",
  "validation": { "total": 3, "passed": 3, "failed": 0 },
  "tests": { "total": 27, "passed": 27, "failed": 0, "skipped": 0 },
  "duration": {
    "totalMs": 1234,
    "unitMs": 800,
    "integrationMs": 434,
    "validationMs": 52
  },
  "failedTestNames": [],
  "success": true
}
```

Notes:

- `failed` fields enable quick gating without recomputing.
- `skipped` summarizes ignored / filtered tests.
- `failedTestNames` (array) stays small (only failing tests) – empty on success.
- `startedAtUtc` / `endedAtUtc` allow deriving wall-clock span; `duration.totalMs` is an explicit metric.
- Fields may expand (non-breaking) in future (e.g. per-suite timing arrays).

You can consume this in CI to branch logic (e.g. fail early, annotate PRs, or feed dashboards) without parsing console output. Future enhancements will merge richer failure metadata for per-suite timing and failure details.

### JUnit / XML Test Output

SpocR can emit a basic JUnit-style XML for CI systems that natively ingest it:

```
spocr test --ci --junit
```

By default this writes `.artifacts/junit-results.xml`. Use `--output <path>` to choose a custom location (takes precedence over `--junit`).

### Phase Control & Skipping

- `--no-validation` skips repository/project validation when running the full suite.
- Validation time is still reported as `0` ms in JSON if skipped.

### Exit Code Precedence

If multiple phases fail the precedence applied is: Unit (41) > Integration (42) > Validation (43) > Aggregate (40).

### Process Cleanup

If you encounter repeated file lock build warnings (`SpocR.dll` / `testhost`), run:

```
powershell -ExecutionPolicy Bypass -File eng/kill-testhosts.ps1
```

This forcibly terminates stale test processes and stabilizes subsequent builds.

SpocR aims to provide native JUnit-style XML output for integration with CI platforms (GitHub Actions, Azure DevOps, GitLab, Jenkins).

Current status:

- Basic placeholder implementation writes a minimal JUnit XML file when `--output <file>` is used with `spocr test`.
- The structure currently contains a single aggregated testsuite with placeholder counts.
- Future versions will emit one `<testsuite>` per logical test category (unit, integration, validation) and optional `<system-out>` / `<properties>` metadata.

Planned enhancements:

1. Real test counting integrated with `dotnet test` results parsing.
2. Failure details mapped into `<failure>` nodes with message + stack trace.
3. Duration tracking (wall clock + per suite timings).
4. Optional attachment of generated artifacts summary.
5. Exit code specialization (e.g. distinguishing generation vs dependency vs validation failures) aligned with reserved codes (2,3).

Example (future target structure):

```xml
<testsuites tests="42" failures="2" time="3.421">
	<testsuite name="unit" tests="30" failures="1" time="1.2" />
	<testsuite name="integration" tests="8" failures="1" time="2.1" />
	<testsuite name="validation" tests="4" failures="0" time="0.121" />
</testsuites>
```

Usage (current minimal behavior):

```
spocr test --validate --output results.xml
```

If you rely on strict JUnit consumers today, treat this as experimental and validate the schema before ingest.

For now, rely on 0 vs non‑zero; begin adapting scripts to treat 1 as a generic failure boundary. Future enhancements will keep 0 backward compatible and only refine non‑zero granularity.

## Determinism & Golden Hash (Relaxed vs Strict Mode)

SpocR generation aims to be deterministic: identical inputs (database schema + configuration + templates + tool version) produce identical output byte-for-byte. We track this via a Golden Hash manifest and diff statistics that allow us to detect unintended churn.

### Concepts

- Golden Hash Manifest: A JSON file containing SHA256 hashes (and sizes) of key generated artifacts (e.g. unified procedure files, result set records, template expansions). Draft format:

  ```jsonc
  {
    "schemaVersion": 1,
    "createdUtc": "2025-10-18T12:34:56Z",
    "files": [
      {
        "path": "src/SpocRVNext/Generators/ProceduresGenerator.cs",
        "sha256": "...",
        "size": 4312
      },
      {
        "path": "src/SpocRVNext/Templates/Procedures/UnifiedProcedure.spt",
        "sha256": "...",
        "size": 1287
      }
    ]
  }
  ```

- Diff Stats: Supplemental report (`debug/diff-stats.json`) enumerating additions/removals/renames between current generation and last committed baseline.
- Allow-List: A glob file (planned: `.spocr-diff-allow`) containing patterns of files or directories temporarily exempt from strict diff enforcement.

### Modes

| Mode           | Behavior                                                   | Exit Codes Used | Typical Phase      |
| -------------- | ---------------------------------------------------------- | --------------- | ------------------ |
| Relaxed        | Diff reported (JSON & console) but build passes            | None (always 0) | Current (bridge)   |
| Soft Fail      | Diff triggers warning; CI may annotate but still returns 0 | None (0)        | Pre‑strict staging |
| Strict         | Unexpected diff fails CI                                   | 21 / 23         | Post coverage ≥60% |
| Integrity Fail | Manifest corruption / hash mismatch shape                  | 22              | Any (hard fail)    |

### Activation Criteria (Strict Mode)

Strict mode will only be enabled once ALL are true:

1. vNext code coverage ≥ 60% (scoped gate already measures this).
2. Golden Hash manifest stabilized (no unexplained churn for 5 consecutive generation runs).
3. Allow-list frozen (no new wildcard expansions for 2 weeks).
4. No outstanding template normalization TODOs (timestamp, machine-specific values removed).
5. Procedural ordering tests (single + multi-result) consistently green (ensures layout determinism).

### Reserved Determinism Exit Codes

- 21 – Diff Violation: Generated output deviates from committed Golden Hash without allow-list coverage.
- 22 – Manifest Integrity: Golden Hash manifest unreadable, malformed schemaVersion, or cryptographic hash set incomplete.
- 23 – Allow-List Violation: Pattern attempted to mask a diff on a protected file (explicit negative rule or forced inclusion).

These codes are currently defined but not emitted (table above shows “No”). CI consumers may prepare logic now; activation will be announced in CHANGELOG.

### Workflow to Update Golden Hash

1. Make intentional generator/template change.
2. Run local generation with upcoming flag (planned) `spocr generate --write-golden` OR manually copy new diff `debug/diff-stats.json` → update manifest file.
3. Inspect diff: ensure only expected files changed, and reason documented in PR description.
4. Adjust `.spocr-diff-allow` ONLY if legitimately broad churn (avoid long-lived wildcards).
5. Commit updated manifest + minimal allowed patterns; remove obsolete allow entries.
6. Push & let CI re-hash; expect relaxed mode pass until strict activated.

### Allow-List Example

```
# Temporary until v5 freeze
src/SpocRVNext/Templates/**
# Generated outputs (bridge period)
Output-v9-0/**
# Force inclusion: negative rule overrides broader wildcard
!src/SpocRVNext/NamePolicy.cs
```

Lines beginning with `!` negate previous globs (ensuring critical files cannot be masked by broad patterns).

### Troubleshooting Determinism Issues

| Symptom                     | Possible Cause                             | Mitigation                                                            |
| --------------------------- | ------------------------------------------ | --------------------------------------------------------------------- |
| Hashes change on every run  | Hidden timestamp / GUID injection          | Remove dynamic value; use deterministic seed or remove field entirely |
| Only whitespace hash diffs  | Template indentation normalization missing | Run formatter or adjust template to stable spacing                    |
| Locale-specific casing diff | Culture-sensitive operations               | Use `CultureInfo.InvariantCulture` or ordinal comparisons             |
| Path separator variation    | Windows vs Linux path normalization        | Normalize to forward slashes before hashing                           |

### Best Practices

- Avoid `DateTime.Now` or `Guid.NewGuid()` in any generated source.
- Keep template includes deterministic (no environment-specific absolute paths).
- Prefer sorted iterations (order by name/ordinal) before writing collections.
- Validate ordering via existing unit tests whenever adding new sections.

### Roadmap

Phase steps (tentative):

1. Informational (current) – produce diff stats only.
2. Soft Fail – annotate diffs, still exit 0 (opt-in via env `SPOCR_STRICT_PREVIEW=1`).
3. Strict – fail with 21/23; coverage gate escalated to ≥80%.
4. Integrity Hardening – cryptographic signature of manifest itself (future) to detect tampering.

Document updates will precede each escalation; CHANGELOG will note activation commits explicitly.

### Why Determinism Matters

Deterministic generation reduces review noise, prevents accidental drift, and allows hashing to become a high-signal quality gate. By enforcing a stable Golden Hash we can spot subtle template regressions early and keep migrations predictable.

## Requirements

- .NET SDK 6.0 or higher (8.0+ recommended)
- SQL Server (2016 or later)
- Access to SQL Server instance for metadata extraction

## Use Cases

- **Enterprise Applications**: Reduce data access layer complexity
- **API Development**: Generate type-safe database interactions
- **Legacy Modernization**: Safely wrap existing stored procedures
- **DevOps Integration**: Automate code generation in CI/CD pipelines

## Installation Options

### Global Tool (Recommended)

```bash
dotnet tool install --global SpocR
```

### Project-local Tool

```bash
dotnet new tool-manifest
dotnet tool install SpocR
dotnet tool run spocr --version
```

### Package Reference

```xml
<PackageReference Include="SpocR" Version="4.1.*" />
```

## Configuration

SpocR uses a `spocr.json` configuration file to customize generation behavior:

```json
{
  "project": {
    "name": "MyProject",
    "connectionString": "Server=.;Database=AppDb;Trusted_Connection=True;",
    "output": {
      "directory": "./Generated",
      "namespace": "MyProject.Data"
    }
  }
}
```

## Contributing

We welcome contributions! A lightweight contributor guide is available in `CONTRIBUTING.md` (Root).

Engineering infrastructure lives under `eng/` (e.g., `eng/quality-gates.ps1`). Transient test & coverage artifacts are written to the hidden directory `.artifacts/` to keep the repository root clean.

All code, comments, commit messages and documentation must be written in English (see Language Policy in `CONTRIBUTING.md`). Non-English identifiers or comments should be refactored during reviews.

- Bug Reports: [Create an issue](https://github.com/nuetzliches/spocr/issues/new?template=bug_report.md)
- Feature Requests: [Create an issue](https://github.com/nuetzliches/spocr/issues/new?template=feature_request.md)
- Pull Requests: See `CONTRIBUTING.md`
- AI Agents: See `.ai/guidelines.md` for automated contribution standards

## License

This project is licensed under the [MIT License](LICENSE).

## Acknowledgments

- Built with [Roslyn](https://github.com/dotnet/roslyn) for C# code generation
- Inspired by modern ORM and code generation tools
- Community feedback and contributions

---

**[Get Started →](https://nuetzliches.github.io/spocr/getting-started/installation)** | **[Documentation →](https://nuetzliches.github.io/spocr/)** | **[Examples →](samples/)**
