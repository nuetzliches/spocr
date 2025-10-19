# Minimal Web API Project

This project is a minimal API built using .NET 8. It serves as a starting point for developing web applications with a focus on simplicity and performance.

## Project Structure

The project contains the following files:

- **Program.cs**: The entry point of the application. It configures the minimal API, defines endpoints, and starts the web server.
- **RestApi.csproj**: The project file for the C# project. It includes information about dependencies, the target version of the .NET framework, and other project settings.
- **Properties/launchSettings.json**: Contains configurations for the application's startup behavior, including environment variables and profiles for different launch options.
- **appsettings.json**: General configuration settings for the application, such as connection strings and other configuration elements.
- **appsettings.Development.json**: Contains specific configuration settings for the development environment, which can override settings in `appsettings.json`.
- **.gitignore**: Lists files and directories that should be ignored by Git, such as build artifacts and temporary files.
- **.editorconfig**: Defines code formatting rules for the project to ensure consistent formatting across the team.

## Getting Started

To get started with this project, follow these steps:

1. Clone the repository:

   ```
   git clone <repository-url>
   ```

2. Navigate to the project directory:

   ```
   cd samples/restapi
   ```

3. Restore the dependencies:

   ```
   dotnet restore
   ```

4. Run the application:
   ```
   dotnet run
   ```

## SpocR Integration (Bridge Phase)

This sample participates in the SpocR v4.5 → v5 bridge phase. You can experiment with the next generation generator (`SpocRVNext`) without breaking existing flow.

### Environment Configuration

Environment precedence (highest wins):

CLI > Environment Variables > `.env` file > `spocr.json` (legacy fallback – will be removed in v5).

Copy the example file and adjust values:

```
cp .env.example .env
```

Relevant keys:

| Variable                 | Purpose                               | Typical Value                        |
| ------------------------ | ------------------------------------- | ------------------------------------ |
| `SPOCR_GENERATOR_MODE`   | Controls generation pipeline          | `legacy` / `dual` (DEFAULT) / `next` |
| `SPOCR_EXPERIMENTAL_CLI` | Enables new System.CommandLine parser | `1` to enable                        |
| `SPOCR_STRICT_NULLABLE`  | Escalate nullable warnings            | `1` optional                         |
| `SPOCR_STRICT_DIFF`      | Activate strict diff policy (future)  | `1` optional                         |

Recommended for local exploration:

```
SPOCR_GENERATOR_MODE=dual
```

This produces legacy + next output side-by-side (comparison / diff tooling can consume hash manifests).

### Generating Code

From repository root (ensures tool available):

```
dotnet tool restore
dotnet tool run spocr pull
dotnet tool run spocr generate --mode dual
```

Or using environment variable only:

```
set SPOCR_GENERATOR_MODE=dual & dotnet tool run spocr generate
```

### Switching Modes

| Mode     | Effect                                         |
| -------- | ---------------------------------------------- |
| `legacy` | Only legacy DataContext generated (status quo) |
| `dual`   | Legacy + next output (observation mode)        |
| `next`   | Only new pipeline output (preview)             |

Mode defaults to `dual` in the bridge phase for internal test environments (subject to change before v5 release).

### Activating vNext Output (Overview)

Enable the new generator path via `.env` or a direct environment variable:

```
SPOCR_GENERATOR_MODE=next
```

During the bridge phase `dual` is recommended so you can compare (Golden Hash / diff reports). `next` produces only the new consolidated output under `samples/restapi/SpocR/<SchemaPascalCase>`.

Minimal `.env` excerpt:

```
# SpocR Bridge Phase
SPOCR_GENERATOR_MODE=dual
SPOCR_EXPERIMENTAL_CLI=1
# Optional explicit namespace override
# SPOCR_NAMESPACE=RestApi.SpocR
```

### First Stored Procedure Call (DbContext Extension vs. Low-Level Wrapper)

Generated code offers two access styles:

1. DbContext extension (high-level, automatic connection lifecycle):

```csharp
var agg = await db.UserListAsync(ct);
if (!agg.Success) { /* handle error */ }
foreach (var row in agg.Result1) { Console.WriteLine(row.DisplayName); }
```

2. Low-level wrapper (manual connection scope, granular for interceptor / diagnostics):

```csharp
await using var conn = await db.OpenConnectionAsync(ct);
var agg = await UserListProcedure.ExecuteAsync(conn, ct);
```

Both variants internally use `ProcedureExecutor` and return the unified aggregate (`UserListResult`). Even for a single result set the aggregate wrapper remains (consistent model enabling future streaming / JSON dual mode extensions).

### Aggregate Convention (Always Unified Aggregate)

SpocR vNext deliberately returns a unified aggregate record for every stored procedure invocation, even when the procedure yields just a single result set or only output parameters. This design choice provides:

1. Structural Stability: Call signatures never change if additional result sets are added later; existing consumers keep working (new sets appear as additional properties with safe defaults).
2. Extensibility: Upcoming features (row streaming, JSON dual mode, interceptor hooks for deserialization) can attach to a single envelope type.
3. Determinism & Hashing: The aggregate acts as a stable root for hashing (Golden Hash) — field order and presence are normalized during generation.
4. Diagnostics: Common metadata (Success, Error, Duration, DbVersion, ProcedureName) live in one place; logging/interceptors don't need overload proliferation.

Excerpt from the conceptual design (see `DeveloperBranchUseOnly-API-CONCEPT.md`): a procedure with one result set still generates an aggregate `UserListResult` with `Result1` collection rather than returning the collection directly.

Example (single result set):

```csharp
var users = await db.UserListAsync(ct);
if (!users.Success) { /* handle error */ }
// Access first (and only) result set via Result1
foreach (var row in users.Result1)
{
   Console.WriteLine(row.DisplayName);
}
```

If later a second result set is added (e.g., statistics):

```csharp
var users = await db.UserListAsync(ct);
// Existing code above still functions.
// New consumers can inspect users.Result2 safely (null or empty if not produced).
```

Low-level execution mirrors the same convention:

```csharp
await using var conn = await db.OpenConnectionAsync(ct);
var agg = await UserListProcedure.ExecuteAsync(conn, ct);
// agg.Result1, agg.Result2, agg.Output, agg.Success, agg.Error
```

This uniform aggregate pattern avoids conditional return types and simplifies generic tooling (diffing, deterministic hashing, potential future pagination adapters). Treat the aggregate as the canonical envelope — collections inside may evolve, the envelope remains.

Design Principle Summary:

- Never return a bare list; always an aggregate record.
- Additive evolution (new result sets) is non-breaking.
- Envelope hosts cross-cutting telemetry & future streaming handles.

For migration from legacy output, the bridge phase allows side-by-side comparison: legacy returns direct sets, vNext returns aggregates — consumers can adapt incrementally.

### Health Endpoint Example

The sample exposes a minimal API mapping for a database health check (framework-gated for future target frameworks):

```
GET /spocr/health/db -> 200 OK { "healthy": true }
```

### Interceptor Logging Activation

For structured measurement (duration, success, error) register the logging interceptor during startup:

```csharp
using Microsoft.Extensions.Logging;
using SpocR.SpocRVNext.Execution; // Namespace containing ProcedureExecutor & LoggingProcedureInterceptor

var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("spocr.exec");
ProcedureExecutor.SetInterceptor(new LoggingProcedureInterceptor(logger));
```

Sample output:

```
[spocr.exec] samples.UserList duration=34ms success=True
```

### Golden Hash Usage

The new output produces hash manifests (determinism proof). Workflow:

1. Initial write (after stable state):

```
dotnet tool run spocr write-golden
```

2. Verify in subsequent builds (relaxed mode):

```
dotnet tool run spocr verify-golden
```

Currently differences are informational only (exit codes 21–23 reserved for future strict activation).

### Bridge Policy Note

Direct major version jumps are guarded by a bridge policy. To override intentionally:

```
SPOCR_ALLOW_DIRECT_MAJOR=1
```

Only set if the documented migration path is understood.

### Preview: Streaming & JSON Dual Mode

Planned additions (v5 timeframe):

- `JsonRawAsync` / `JsonDeserializeAsync<T>` / `JsonElementsAsync<T>` / `JsonStreamAsync`
- Row and JSON streaming via `IAsyncEnumerable<T>`
- Interceptor hooks for JSON deserialization.

Currently not generated – placeholders are documented in `DeveloperBranchUseOnly-API-CONCEPT.md`.

### Example: CreateUser Stored Procedure (Input/Output)

A typical procedure with input & output parameters (simplified excerpt):

```csharp
var input = new CreateUserInput("alice@example.com", "Alice", "Bio...");
var result = await db.CreateUserAsync(input, ct);
if (result.Output is { } output)
{
   Console.WriteLine($"New UserId={output.UserId}");
}
```

Low-level:

```csharp
await using var conn = await db.OpenConnectionAsync(ct);
var result = await CreateUserProcedure.ExecuteAsync(conn, input, ct);
```

### Troubleshooting Quick Reference

| Problem                     | Hint                                                                                        |
| --------------------------- | ------------------------------------------------------------------------------------------- |
| No output generated         | Check `SPOCR_GENERATOR_MODE` (default dual), ensure `.spocr/schema` exists.                 |
| Namespace incorrect         | Set `SPOCR_NAMESPACE` in `.env` or env var then regenerate.                                 |
| DB timeout                  | Increase `SpocRDbContextOptions.CommandTimeout` or inspect DB/container startup latency.    |
| No interceptor logs         | Was `ProcedureExecutor.SetInterceptor(...)` invoked after building the ServiceProvider?     |
| Unexpected Golden Hash diff | Update intentionally via `write-golden` after review; check allow-list `.spocr-diff-allow`. |
| Missing JSON methods        | v5 preview feature – not yet generated.                                                     |

---

For feedback on vNext please create issues with label `vnext`.

### Reporting Issues

If the next pipeline output differs unexpectedly or is missing artifacts, open an issue and include:

1. Generator mode (`legacy|dual|next`)
2. Hash manifest diff snippet (if available)
3. Simplified reproduction (stored procedure signature)

This accelerates stabilization of `SpocRVNext` before the v5 cutover.

## Endpoints

The API defines several endpoints that can be accessed once the application is running. Refer to the `Program.cs` file for details on the available endpoints.

## Contributing

Contributions are welcome! Please feel free to submit a pull request or open an issue for any suggestions or improvements.
