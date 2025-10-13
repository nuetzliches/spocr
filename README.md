# SpocR

[![NuGet](https://img.shields.io/nuget/v/SpocR.svg)](https://www.nuget.org/packages/SpocR)
[![NuGet Downloads](https://img.shields.io/nuget/dt/SpocR.svg)](https://www.nuget.org/packages/SpocR)
[![License](https://img.shields.io/github/license/nuetzliches/spocr.svg)](LICENSE)
[![Build Status](https://img.shields.io/github/actions/workflow/status/nuetzliches/spocr/test.yml?branch=main)](https://github.com/nuetzliches/spocr/actions)
[![Code Coverage](https://img.shields.io/badge/coverage-check%20actions-blue)](https://github.com/nuetzliches/spocr/actions)

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
# Initialize project
spocr create --project MyProject

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

## Documentation

For comprehensive documentation, examples, and advanced configuration:

**[Visit the SpocR Documentation](https://nuetzliches.github.io/spocr/)**

> Bridge Phase (v4.5 → v5)
>
> The project is currently in a transitional "Bridge Phase" (planned final minor before v5). A legacy DataContext generator is still present while the new `SpocRVNext` pipeline matures. Unless you explicitly opt in, behavior remains stable.
>
> Key points:
> - Legacy Freeze: Non-critical functional changes to the legacy generator are frozen (only security / stability fixes). 
> - Dual Generation: You can run both pipelines in parallel (`SPOCR_GENERATOR_MODE=dual` - DEFAULT in v4.5) to observe new output without impacting existing code.
> - Opt-In Flags: New CLI parser & strict modes are guarded behind environment variables / flags (see `samples/restapi/.env.example`).
> - Cutover Plan: v5 will remove the legacy DataContext and obsolete configuration properties. Migration notes will be published ahead of the release.
>
> Recommended now: inspect the future output, and open issues for gaps / blockers you discover.

### Major Version Bridge Policy (Upgrade Safety)

To reduce accidental skips over required transitional releases, SpocR enforces a *bridge policy* for major upgrades:

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

Configuration values are resolved using a clear, deterministic order:

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

Invalid modes (anything not `legacy|dual|next`) trigger an exception early with a clear message (tested in `EnvConfigurationTests`).

Benefits:

- Predictable overrides in CI pipelines
- Safe experimentation via ephemeral CLI flags
- Gradual deprecation path for `spocr.json` fields targeted for removal in v5

Referenced Tests: `EnvConfigurationTests` (precedence + invalid mode) and `BridgePolicyTests` (upgrade gating & override variable).

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
    "output": { "namespace": "My.App", "dataContext": { "path": "./DataContext" } }
  }
}
```

After:
```jsonc
{
  "project": {
    "output": { "namespace": "My.App", "dataContext": { "path": "./DataContext" } }
  }
}
```

Console Warning Policy:
* Until v5 a warning appears if `role.kind` is `Lib` or `Extension`.
* In v5 the enum and node will be removed entirely; legacy configs will be auto-normalized.

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

| Code | Category        | Meaning / Usage                            | Emitted Now                | Notes                                  |
| ---- | --------------- | ------------------------------------------ | -------------------------- | -------------------------------------- |
| 0    | Success         | Successful execution                       | Yes                        | Stable                                 |
| 10   | Validation      | Validation / user input failure            | Yes (validate path)        |                                        |
| 20   | Generation      | Code generation pipeline error             | No                         | Reserved                               |
| 30   | Dependency      | External system (DB/network) failure       | No                         | Reserved                               |
| 40   | Testing         | Test suite failure (aggregate)             | Yes                        | 41=Unit, 42=Integration, 43=Validation |
| 41   | Testing         | Unit test failure                          | Yes (unit failures)        | More specific than 40                  |
| 42   | Testing         | Integration test failure                   | Yes (integration failures) | Falls back to 40 if ambiguous          |
| 43   | Testing         | Validation test failure                    | Yes (validation failures)  | Structural / repository validation     |
| 50   | Benchmark       | Benchmark execution failure                | No                         | Reserved (flag present, impl pending)  |
| 60   | Rollback        | Rollback / recovery failed                 | No                         | Reserved                               |
| 70   | Configuration   | Config parsing/validation error            | No                         | Reserved                               |
| 80   | Internal        | Unexpected unhandled exception             | Yes (Program.cs catch)     | Critical – file issue/bug              |
| 99   | Future/Reserved | Experimental / feature-flag reserved space | No                         | Avoid relying on this                  |

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
