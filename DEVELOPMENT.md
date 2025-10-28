# Development Sandbox (`debug/`)

All active development and comparison workflows run inside the `debug/` sandbox. Generator runs, snapshot comparisons, and temporary artifacts stay isolated in that directory.

```bash
# Full rebuild targeting debug/.env
dotnet run --project src/SpocR.csproj -- rebuild -p debug --no-cache --verbose

# Optional follow-up rebuild using the cache
dotnet run --project src/SpocR.csproj -- rebuild -p debug --verbose

# Quality gates (tests, analyzers, uses debug/ artifacts)
pwsh -File eng/quality-gates.ps1
```

Artifacts appear under `debug/SpocR/`, additional snapshot data under `debug/.spocr/`. Comparison tooling lives in `debug/README.md` (`debug/model-diff-report.md`, and related scripts).

# RestApi Sample (optional)

The sample project consumes generated outputs from `samples/restapi/SpocR/`. Keep the sample focused on the current artifact layout.

```bash
dotnet build samples/restapi/RestApi.csproj -c Debug
dotnet run   samples/restapi/RestApi.csproj -c Debug
```

## Smoke & Automation Scripts

| Script                                   | Purpose                                                                                                  | Fast? | Default Exit Codes                                      |
| ---------------------------------------- | -------------------------------------------------------------------------------------------------------- | ----- | ------------------------------------------------------- |
| `samples/restapi/scripts/smoke-test.ps1` | Minimal API reachability plus core endpoints (`/`, `GET/POST /api/users`).                               | Yes   | 0 success / 1 failure                                   |
| `samples/restapi/scripts/test-db.ps1`    | Stand-alone SQL connectivity check (`SELECT 1`). Uses `SPOCR_SAMPLE_RESTAPI_DB` or the appsettings      | Yes   | 0 ok / 2 connect fail / 3 query fail / 4 config missing |

Quick runs from the repository root (PowerShell):

```powershell
pwsh -ExecutionPolicy Bypass -File samples/restapi/scripts/smoke-test.ps1
pwsh -ExecutionPolicy Bypass -File samples/restapi/scripts/test-db.ps1 -Verbose
```

Guidelines:

1. Keep `smoke-test.ps1` lightweight—no heavy database diagnostics.
2. Add new endpoints only when they are stable and consistently available.
3. Deterministic file hashing (“Golden”) belongs to a dedicated determinism workflow.
4. Use `test-db.ps1` before extended smoke runs when connectivity is uncertain.
5. CI runs smoke tests first and deeper suites only after green smoke results.

## Namespace Convention

Generated code follows the convention:

```
<RootNamespace>.SpocR.<SchemaPascalCase>
```

Configuration notes:

- `.env` files (for example `samples/restapi/.env`) provide `SPOCR_NAMESPACE=RestApi` (root only, no `.SpocR` suffix).
- `SPOCR_OUTPUT_DIR` defaults to `SpocR` and is appended one time to the root namespace.
- Each generator adds only the schema (PascalCase). No extra segments such as `.Inputs`, `.Outputs`, `.Results`, or `.Procedures`.

Examples:

```
RestApi.SpocR.Samples.CreateUserWithOutputInput
RestApi.SpocR.Samples.OrderListAsJsonResult
RestApi.SpocR.Dbo.UserContactSyncInput
```

Rationale:

- One namespace per schema; the artifact type is visible through the type name suffix (Input, Result, Aggregate, Output, Plan, Procedure).
- Keeps namespaces shallow and reduces merge conflicts.
- Consistent derivation simplifies refactoring and tooling support.

Overrides are possible by setting `SPOCR_NAMESPACE=MyCompany.Project`. The system still appends `.SpocR` plus the schema.

## Unified Result Model

Procedure artifacts are consolidated into three files at most:

1. Input: `<Proc>NameInput.cs`
2. Output (when OUTPUT parameters exist): `<Proc>NameOutput.cs`
3. Unified Result, result sets, plan, and wrapper: `<Proc>NameResult.cs`

Structure of `<Proc>NameResult.cs`:

```
public sealed class <Proc>NameResult {
   public bool Success { get; init; }
   public string? Error { get; init; }
   public <Proc>NameOutput? Output { get; init; }
   public IReadOnlyList<<Proc>Name<ResultSetOrResolvedName>ResultSet> Result { get; init; } = ...;
   public IReadOnlyList<<Proc>Name<NextResolvedName>ResultSet1> Result1 { get; init; } = ...;
   public IReadOnlyList<<Proc>Name<NextResolvedName>ResultSet2> Result2 { get; init; } = ...;
}

public readonly record struct <Proc>Name<ResultSetOrResolvedName>ResultSet(...);
public readonly record struct <Proc>Name<NextResolvedName>ResultSet1(...);
// Additional result-set record types (...ResultSet2, ...ResultSet3, ...)
 
internal static partial class <Proc>NameProcedurePlan { /* Execution plan + binder */ }
public static class <Proc>NameProcedure { /* ExecuteAsync wrapper */ }
```

Naming rules:

1. Aggregate properties (0-based display without leading zero):
   - First result set (index 0) → property `Result`
   - Second result set (index 1) → property `Result1`
   - Third result set (index 2) → property `Result2`, and so on.
2. Result-set record types:
   - First result-set type: `<Proc>Name<ResolvedBaseName>ResultSet`
   - Subsequent types: `<Proc>Name<ResolvedBaseName>ResultSet1`, `<Proc>Name<ResolvedBaseName>ResultSet2`, etc. (numbering starts at 1 for the second set)
   - Fallback base names use `ResultSet`, `ResultSet1`, `ResultSet2`, and follow the same pattern.
3. Property names and record type names are intentionally offset (`Result` vs `<…>ResultSet`) to reduce redundancy.
4. Custom or resolver base names (for example `Users`) are embedded: `<Proc>NameUsersResultSet`, `<Proc>NameUsersResultSet1`, etc. Resolver collisions may append numbers within the base name (e.g., `<Proc>NameUsers1ResultSet`).

Current stance:

- Plan, aggregate, and individual result-set rows stay within the consolidated file.
- Output records remain external and are referenced when present.
- The consistent `ResultSet` suffix enables straightforward tooling filters (e.g., `EndsWith("ResultSet")`).

Rationale:

- Eliminates double wording in type names and aligns frequent access with `result.Result`.
- Keeps numbering minimal while still disambiguating multiple sets.
- Supports stable naming for refactors, tests, and scripts.

Future opportunities (backlog):

- Shorter type names for result-set records.
- Helper extensions for single-result workflows.
- Optional source generator integration instead of file I/O.

## Nullable Reference Types – Stepwise Escalation (Phase 1)

Global `<Nullable>enable</Nullable>` is active. Escalation happens in controlled phases to keep warning noise manageable.

Phases:

1. Baseline (current): nullable warnings remain warnings, fix opportunistically.
2. Focus warnings (local or CI opt-in):
   - CS8602 Dereference of a possibly null reference
   - CS8603 Possible null reference return
3. Expansion to additional warnings (e.g., CS8618) once hotspots decline.
4. CI hard gate: set `SPOCR_STRICT_NULLABLE=1` permanently.

Local experiment (do not commit):

```ini
# .editorconfig (local only)
[*.cs]
dotnet_diagnostic.CS8602.severity = error
dotnet_diagnostic.CS8603.severity = error
```

CI escalation example:

```powershell
# Windows local (optional)
setx SPOCR_STRICT_NULLABLE 1
```

```yaml
# GitHub Actions
env:
  SPOCR_STRICT_NULLABLE: 1
```

Always remove temporary `.editorconfig` tweaks before committing.

## Procedure Execution Layer

The stored procedure pipeline uses a two-phase design: generation emits a `ProcedureExecutionPlan`, and a thin runtime executor interprets the plan.

Core types (namespace `SpocR.SpocRVNext.Execution`):

1. `ProcedureExecutionPlan`
   - `ProcedureName`: fully-qualified name (schema + procedure)
   - `Parameters`: array of `ProcedureParameter` (name, DbType, size, output flag, nullability)
   - `ResultSets`: array of `ResultSetMapping` (name + async materializer delegate)
   - `OutputFactory`: optional delegate mapping collected output parameters to a strongly typed record
   - `InputBinder`: optional delegate binding an input record to `DbCommand` parameters
   - `AggregateFactory`: delegate assembling the final aggregate result (success, error, output, result sets)

2. `ProcedureExecutor`
   - Generic `ExecuteAsync<TAggregate>(DbConnection, ProcedureExecutionPlan, object? state, CancellationToken)`
   - Opens connections, builds parameters, invokes binders, reads result sets sequentially, captures output values, and wraps errors.

Generated artifacts per procedure (under `<Namespace>.Procedures`):

- Row record structs per result set (`<Operation><ResultSetName>Row`)
- Optional output record (`<Operation>Output`)
- Aggregate result class (`<Operation>ResultAggregate`)
- Execution plan static partial class (`<Operation>ProcedurePlan`) exposing a cached `Instance`
- Wrapper static class with an `ExecuteAsync` facade that delegates to the executor

Performance considerations:

- Deterministic ordering of parameters and result sets for stable hashing
- Ordinal caching ensures materializers fetch field ordinals once
- Minimal allocations: materializers push rows into a list once before the aggregate factory projects them

Determinism guarantees:

- No timestamps or random values in generated source
- Stable ordering enforced by the schema provider
- Plans are structural data; runtime outcomes never drive later generation

Extensibility roadmap:

- Nullable strictness escalation via richer metadata
- Pluggable materializer strategies (e.g., streaming `IAsyncEnumerable<T>`)
- Precision and scale support within `ProcedureParameter`
- Optional telemetry hooks via partial methods or executor wrappers
- Strict diff mode to block incompatible plan changes

Testing strategy:

- Metadata provider parsing and determinism hash tests
- Executor tests using in-memory ADO fakes for output parameters and error paths
- DbType mapping tests covering reflection-driven mapping tables

Developer notes:

- To add a new SQL type mapping adjust `MapDbType` in `ProceduresGenerator` and keep ordering explicit.
- Any change to plan structure requires generator updates, executor updates (if applicable), and refreshed determinism tests.

## Generator Activation and .env Bootstrap

Modern artifacts (inputs, outputs, results, procedures, table types, DbContext) are produced once the generator runs with a valid `.env` – generation always targets the next pipeline now.

Initial setup for a fresh repository or sample (`samples/restapi`):

2. Rebuild mit Schema Snapshot:
   ```cmd
   dotnet run --project src/SpocR.csproj -- rebuild -p samples/restapi/spocr.json --no-auto-update
   ```
3. Falls keine `.env` existiert:
   - Der `EnvBootstrapper` fragt interaktiv: "Create new .env from example now? [Y/n]:"
   - Mit "Y" wird aus `samples/restapi/.env.example` oder einem Fallback-Template eine `.env` im Repo Root erstellt.
   - Mit "n" oder Fehler wird der Lauf abgebrochen (vNext erfordert eine `.env`).
4. Nach erfolgreichem Lauf erscheinen vNext Dateien unter `samples/restapi/SpocR` (z.B. `Inputs`, `Outputs`, `Procedures`, `Results`).

Beispiel `.env` Minimal:

```dotenv
# SpocR vNext
# Optional Namespace überschreiben
# SPOCR_NAMESPACE=RestApi
SPOCR_OUTPUT_DIR=SpocR
```

Troubleshooting:

- Keine neuen Ordner? Prüfen: Wurde `.env` erstellt und enthält mindestens eine `SPOCR_` Zeile?
- Lauf abgebrochen? `.env` Erstellung abgelehnt oder fehlgeschlagen – Bootstrapper beendet sich ohne Legacy-Fallback.
- Non-interaktives Umfeld (CI): `.env` vorab einchecken oder zur Laufzeit generieren – andernfalls beendet der Bootstrapper den Prozess.

CLI Roadmap:

- Geplanter Befehl `spocr vnext init-env` zum nicht-interaktiven Schreiben einer `.env` aus Template.
- Geplanter Befehl `spocr vnext generate` ersetzt durch Standard-`spocr generate` (nächste Pipeline ist Standard).

### Interceptors (Procedure Execution)

The vNext executor exposes a single global interceptor hook surface via `ProcedureExecutor.SetInterceptor(ISpocRProcedureInterceptor)`. Interceptors allow lightweight instrumentation without altering generated code.

Contract (`ISpocRProcedureInterceptor`):

```
Task<object?> OnBeforeExecuteAsync(string procedureName, DbCommand command, object? state, CancellationToken ct);
Task OnAfterExecuteAsync(string procedureName, DbCommand command, bool success, string? error, TimeSpan duration, object? beforeState, object? aggregate, CancellationToken ct);
```

Lifecycle:

1. Executor builds & binds command parameters.
2. `OnBeforeExecuteAsync` called (state = input record supplied by wrapper). Return value propagated into `OnAfterExecuteAsync` as `beforeState` (e.g. start timestamp, correlation id).
3. Core execution (ADO.NET + result set materialization).
4. `OnAfterExecuteAsync` invoked with success/error + aggregate result instance.

Global vs Scoped:

- CURRENT: A single static interceptor instance (simple and allocation-free). Suitable for most apps.
- POSSIBLE FUTURE: DI-scoped interceptors enabling per-request correlation. Would require refactoring `ProcedureExecutor` signature to accept an interceptor instance or context.

Registration (startup):

```csharp
// using SpocR.SpocRVNext.Execution;
// Assume ILoggerFactory lf is available
var logger = lf.CreateLogger("SpocR.Proc");
ProcedureExecutor.SetInterceptor(new LoggingProcedureInterceptor(logger));
```

Best Practices:

- Avoid heavy work (no large allocations, no blocking I/O).
- Swallow internal exceptions: default implementation and logging interceptor never throw.
- Use structured logging (key=value pairs) for easier querying.
- Do not mutate `DbCommand.Parameters` after binding unless you fully understand generator expectations.
- Keep execution path side-effect free; interceptor is observational.

Error Handling:

- Executor catches all interceptor exceptions during `OnAfterExecuteAsync` to protect the core flow.
- If the interceptor throws in `OnBeforeExecuteAsync`, execution aborts (normal exception propagation). Interceptors should avoid throwing.

Telemetry Ideas (Future):

- Correlation id injection via beforeState.
- Metrics counters (success count, error count, average duration).
- Extended hooks for streaming phases (deferred to v5 streaming work).

Testing:

- Unit tests provide fake interceptor verifying success & failure paths (see `ProcedureInterceptorTests`).
- Logging interceptor kept minimal; tests can assert presence of expected log message templates if required.

When to Implement a Custom Interceptor:

- Need centralized timing metrics.
- Need audit of procedure invocations.
- Integrate with tracing systems (e.g. OpenTelemetry) by starting/ending spans.

Open Questions:

- Scoped vs global trade-off (documented; decision: keep global for v5 initial release).
- Additional hooks (streaming, JSON deserialization) deferred until streaming feature stabilized.
