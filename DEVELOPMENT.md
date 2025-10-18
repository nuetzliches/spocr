# Restapi Development

```bash
dotnet run --project src/SpocR.csproj -- rebuild  -p samples/restapi/spocr.json --no-auto-update
dotnet build samples/restapi/RestApi.csproj -c Debug
dotnet run --project samples/restapi/RestApi.csproj -c Debug
```

## Smoke & Automation Scripts

| Script                                   | Purpose                                                                                                  | Fast? | Default Exit Codes                                      |
| ---------------------------------------- | -------------------------------------------------------------------------------------------------------- | ----- | ------------------------------------------------------- |
| `samples/restapi/scripts/smoke-test.ps1` | Minimal API reachability + core endpoints (`/`, `GET/POST /api/users`).                                  | Yes   | 0 success / 1 failure                                   |
| `samples/restapi/scripts/test-db.ps1`    | Stand‑alone SQL connectivity check (`SELECT 1`). Uses `SPOCR_SAMPLE_RESTAPI_DB` or appsettings fallback. | Yes   | 0 ok / 2 connect fail / 3 query fail / 4 config missing |

Quick runs from repo root (PowerShell):

```powershell
pwsh -ExecutionPolicy Bypass -File samples/restapi/scripts/smoke-test.ps1
pwsh -ExecutionPolicy Bypass -File samples/restapi/scripts/test-db.ps1 -Verbose
```

Guidelines:

1. Keep `smoke-test.ps1` lean – no heavy DB diagnostics.
2. Add new endpoints only when stable and universally available.
3. Deterministic file hashing ("Golden") belongs in a separate determinism workflow (planned).
4. Use `test-db.ps1` before extending smoke coverage if connectivity issues arise.
5. For CI, prefer running smoke first; only if green run deeper tests.

## vNext Namespace-Regel

Ab vNext gilt für generierten Code das konsistente Muster:

<RootNamespace>.SpocR.<SchemaPascalCase>

Konfiguration:

- `.env` im Projektroot (z.B. `samples/restapi/.env`) enthält `SPOCR_NAMESPACE=RestApi` (nur Root, ohne `.SpocR`).
- `SPOCR_OUTPUT_DIR` Standard ist `SpocR` und wird einmalig an den RootNamespace angehängt.
- Jeder Generator hängt ausschließlich das Schema (PascalCase) an. Keine zusätzlichen Segmente wie `.Inputs`, `.Outputs`, `.Results`, `.Procedures` mehr im Namespace.

Beispiele:

```
RestApi.SpocR.Samples.CreateUserWithOutputInput
RestApi.SpocR.Samples.OrderListAsJsonResult
RestApi.SpocR.Dbo.UserContactSyncInput
```

Rationale:

- Eindeutige Zuordnung pro Schema; Artefakttyp ergibt sich aus Typnamen-Suffix (Input, Result, Aggregate, Output, Plan, Procedure).
- Verhindert inflationäre Namespace-Tiefe und verringert Merge-Konflikte.
- Konsistente Ableitung erleichtert Refactoring & tooling.

Override (optional):

Wer einen anderen Root verwenden möchte, setzt `SPOCR_NAMESPACE=MyCompany.Project`. Das System ergänzt weiterhin `.SpocR` + Schema.

Hinweis: Legacy-Generator bleibt unverändert; vNext läuft in `dual` Mode parallel zur Validierung.

## Unified Result Modell (vNext)

Alle prozedurbezogenen Artefakte werden in möglichst wenige Dateien verdichtet:

1. Input: `<Proc>NameInput.cs`
2. Output (nur falls OUTPUT Parameter): `<Proc>NameOutput.cs`
3. Unified Result + ResultSets + Plan + Wrapper: `<Proc>NameResult.cs`

Struktur von `<Proc>NameResult.cs`:

```
public sealed class <Proc>NameResult {
   public bool Success { get; init; }
   public string? Error { get; init; }
   public <Proc>NameOutput? Output { get; init; }          // optional
   public IReadOnlyList<<Proc>NameResultSet1Result> Result1 { get; init; } = ...; // erster ResultSet
   public IReadOnlyList<<Proc>Name<CustomName>Result> Result2 { get; init; } = ...; // usw.
}

public readonly record struct <Proc>NameResultSet1Result(...);
// Weitere ResultSet-Record-Typen

internal static partial class <Proc>NameProcedurePlan { /* ExecutionPlan + Binder */ }
public static class <Proc>NameProcedure { /* ExecuteAsync wrapper */ }
```

Benennung:

- Fallback ResultSet Namen (`ResultSet1`, `ResultSet2`, ...) werden als Properties zu `Result1`, `Result2`, ... gekürzt.
- Die zugrundeliegenden Record-Typen behalten aktuell das Muster `<Proc>NameResultSet1Result` (deterministisch & eindeutig). Optional kann später auf `<Proc>NameResult1` verkürzt werden.
- Es gibt keine separaten Dateien für Plan, Aggregate oder einzelne ResultSet Rows mehr.
- Inline Output Record wurde entfernt (Duplikat), externer Output Record wird wiederverwendet.

Rationale:

- Reduktion der Dateianzahl -> bessere Navigierbarkeit.
- Konsistente API: Immer eine zentrale Result-Datei je Stored Procedure.
- Stabilität der Typnamen für spätere Refactors / Migrationsskripte.

Mögliche zukünftige Optimierungen (Backlog):

- Kürzere Typnamen für Ergebnis-Records der ResultSets (`Result1` statt `ResultSet1Result`).
- Generische Helper / LINQ Extensions für Single-Result Verfahren.
- Optionale Source Generator Integration statt File-IO.

---

## Nullable Reference Types – Stepwise Escalation (Phase 1)

Global `<Nullable>enable</Nullable>` ist aktiv. Eskalation erfolgt in kontrollierten Phasen, um Warnungsrauschen gering zu halten.

Phasenplan:

1. Baseline (aktuell): Alle Nullable-Warnungen bleiben Warnungen. Opportunistisches Fixing.
2. Fokus-Warnungen (lokal oder CI opt-in):
   - CS8602 Dereference of a possibly null reference
   - CS8603 Possible null reference return
3. Erweiterung: Weitere relevante Warnungen (z.B. CS8618) nach Reduktion der Hotspots.
4. CI Hard Gate: `SPOCR_STRICT_NULLABLE=1` dauerhaft setzen.

Lokaler Test (temporär, nicht committen):

```ini
# .editorconfig (lokal)
[*.cs]
dotnet_diagnostic.CS8602.severity = error
dotnet_diagnostic.CS8603.severity = error
```

CI Eskalation Beispiel:

```powershell
# Windows lokal (optional)
setx SPOCR_STRICT_NULLABLE 1
```

```yaml
# GitHub Actions Beispiel
env:
	SPOCR_STRICT_NULLABLE: 1
```

Rationale:

Aufräumhinweis: Vor Commits lokale experimentelle .editorconfig Anpassungen entfernen.

### Procedure Execution Layer (vNext)

The vNext stored procedure pipeline introduces a two-phase approach: a compile-time generation phase that emits a `ProcedureExecutionPlan` and a thin runtime executor that interprets the plan.

Core types (namespace `SpocR.SpocRVNext.Execution`):

1. `ProcedureExecutionPlan`

   - `ProcedureName`: Fully-qualified name (schema + proc)
   - `Parameters`: Array of `ProcedureParameter` (name, DbType, size, output flag, nullability)
   - `ResultSets`: Array of `ResultSetMapping` (name + async materializer delegate)
   - `OutputFactory`: Optional delegate mapping collected output parameter dictionary to a strongly-typed output record
   - `InputBinder`: Optional delegate binding an input record instance onto DbCommand parameters before execution
   - `AggregateFactory`: Delegate assembling the final aggregate result (success + error + output + result sets)

2. `ProcedureExecutor`
   - Single generic method `ExecuteAsync<TAggregate>(DbConnection, ProcedureExecutionPlan, object? state, CancellationToken)`
   - Handles connection opening, parameter creation, invoking binder, sequential async reading of result sets, capture of output parameter values, and error wrapping.

Generated Artifacts per Procedure (in `<Namespace>.Procedures`):

- Row record structs per result set (`<Operation><ResultSetName>Row`)
- Optional output record (`<Operation>Output`)
- Aggregate result class (`<Operation>ResultAggregate`) capturing success/error/output/result sets
- Execution plan static partial class (`<Operation>ProcedurePlan`) exposing cached singleton `Instance`
- Wrapper static class with `ExecuteAsync` facade calling the executor and passing input state.

Performance Considerations:

- Deterministic ordering of parameters/result sets for stable hashing
- Ordinal caching: generated materializers prefetch all field ordinals once, avoiding per-row `GetOrdinal` calls
- Minimal allocations: materializers add row structs directly to a `List<object>`; conversion to strongly-typed list happens once in the aggregate factory.

Determinism Guarantees:

- No timestamps / random values in generated source
- Parameter & field ordering stable (schema provider sorts)
- Plan is pure structural data; runtime outcomes (row counts, values) do not influence subsequent generation.

Extensibility Points / Future Roadmap:

- Nullable strictness escalation (planned) via enhanced metadata
- Pluggable materializer strategies (streaming `IAsyncEnumerable<T>` for large result sets)
- Precision/scale support in `ProcedureParameter` (currently only size captured)
- Optional telemetry hooks (before/after execute) by wrapping `ProcedureExecutor` or generating partial methods
- Strict diff mode to block incompatible plan changes (deferred to 5.0 roadmap)

Testing Strategy:

- Unit tests for metadata provider parsing & determinism hash
- Executor tests using in-memory fake ADO primitives for output parameters & error path
- DbType mapping tests (reflection invoked) ensure mapping table stability.

Developer Notes:

- To add a new SQL type mapping adjust `MapDbType` inside `ProceduresGenerator`. Keep ordering explicit to avoid incidental matches.
- Any change to plan shape should include: generator update, executor update (if needed), determinism test re-run.

### Aktivierung vNext Codegenerierung & .env Bootstrap

vNext Artefakte (Inputs, Outputs, Results, Procedures, TableTypes, DbContext) werden nur in den Modi `dual` oder `next` erzeugt.

Schritte für ein frisches Repository / Sample (`samples/restapi`):

1. Modus wählen
   - Default (keine Variable): Fällt im Legacy-Pfad auf "dual" zurück, benötigt aber für vNext effektive Erzeugung eine `.env`.
   - Explicit setzen (CMD):
     ```cmd
     set SPOCR_GENERATOR_MODE=dual
     ```
2. Rebuild mit Schema Snapshot:
   ```cmd
   dotnet run --project src/SpocR.csproj -- rebuild -p samples/restapi/spocr.json --no-auto-update
   ```
3. Falls keine `.env` existiert:
   - Der `EnvBootstrapper` fragt interaktiv: "Create new .env from example now? [Y/n]:"
   - Mit "Y" wird aus `samples/restapi/.env.example` oder einem Fallback-Template eine `.env` im Repo Root erstellt.
   - Mit "n" oder Fehler: Fallback auf `legacy` (vNext Ausgabe entfällt in diesem Lauf).
4. Nach erfolgreichem Lauf erscheinen vNext Dateien unter `samples/restapi/SpocR` (z.B. `Inputs`, `Outputs`, `Procedures`, `Results`).

Beispiel `.env` Minimal:

```dotenv
# SpocR vNext
SPOCR_GENERATOR_MODE=dual
# Optional Namespace überschreiben
# SPOCR_NAMESPACE=RestApi
SPOCR_OUTPUT_DIR=SpocR
```

Troubleshooting:

- Keine neuen Ordner? Prüfen: Wurde `.env` erstellt und enthält mindestens eine `SPOCR_` Zeile?
- Fallback auf legacy passiert still? `echo %SPOCR_GENERATOR_MODE%` (Windows) prüfen – ggf. erneuter Lauf nach Erstellung der `.env`.
- Non-interaktives Umfeld (CI): `.env` vorab einchecken oder zur Laufzeit generieren – andernfalls erzwingt der Bootstrapper den Wechsel zu legacy.

CLI Roadmap:

- Geplanter Befehl `spocr vnext init-env` zum nicht-interaktiven Schreiben einer `.env` aus Template.
- Geplanter Befehl `spocr vnext generate` als expliziter Wrapper für reinen vNext Lauf (ohne Legacy Generatoren).

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
