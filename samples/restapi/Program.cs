using System;
using System.Data.Common;
using System.Text.Json;
using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http.Metadata;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RestApi.SpocR; // generated DbContext extensions
using RestApi.SpocR.Samples; // generated sample stored procedure wrappers

var builder = WebApplication.CreateBuilder(args);

// Diagnostic: elevate ASP.NET Core logging for pipeline tracing (can be removed later)
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Debug);

// Add minimal OpenAPI support (only available in newer ASP.NET versions / net10+ APIs)
#if NET10_0_OR_GREATER
builder.Services.AddOpenApi();
#endif

// No direct SpocR library reference: generated code (if any) would live under Spocr/.
// This sample stays framework-agnostic regarding the generator implementation.

// Register generated lightweight DbContext with layered connection resolution:
builder.Services.AddSpocRDbContext(o =>
{
    var cs = Environment.GetEnvironmentVariable("SPOCR_SAMPLE_RESTAPI_DB");
    if (string.IsNullOrWhiteSpace(cs))
    {
        // Try configuration (appsettings) first
        cs = builder.Configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
        {
            // fallback chain: LocalDB (Windows) -> docker default SA (from samples/mssql/.env)
            cs = OperatingSystem.IsWindows()
                ? "Server=(localdb)\\MSSQLLocalDB;Database=SpocRSample;Trusted_Connection=True;"
                : "Server=localhost,1433;Database=SpocRSample;User ID=sa;Password=SpocR@12345;Encrypt=True;TrustServerCertificate=True;"; // docker sample default
        }
    }
    // Ensure a modest connection timeout to avoid long hangs (default often 15s) – explicit to surface early failures
    if (!cs.Contains("Connection Timeout=", StringComparison.OrdinalIgnoreCase) && !cs.Contains("Connect Timeout=", StringComparison.OrdinalIgnoreCase))
    {
        // Use a slightly higher timeout to avoid premature failures on container cold start
        cs = cs.TrimEnd(';') + ";Connection Timeout=10";
    }
    o.ConnectionString = cs;
});

var app = builder.Build();

// Background warm-up: attempt early DB connection to surface connectivity issues before first HTTP request
_ = Task.Run(async () =>
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetService<ISpocRDbContext>();
        if (db != null)
        {
            Console.WriteLine("[BOOT] Warmup: opening DB connection...");
            await using var conn = await db.OpenConnectionAsync();
            Console.WriteLine("[BOOT] Warmup: connection opened state=" + conn.State);
        }
        else
        {
            Console.WriteLine("[BOOT] Warmup: ISpocRDbContext not resolved (null)");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("[BOOT] Warmup: failed ex=" + ex.GetType().Name + " msg=" + ex.Message);
    }
});

// Startup diagnostic marker (helps detect stale processes in smoke script)
try
{
    var asm = Assembly.GetExecutingAssembly();
    var asmPath = asm.Location;
    var ts = System.IO.File.GetLastWriteTimeUtc(asmPath).ToString("O");
    Console.WriteLine($"[BOOT] RestApi Assembly={System.IO.Path.GetFileName(asmPath)} LastWriteUtc={ts} Version={asm.GetName().Version}");
}
catch (Exception ex)
{
    Console.WriteLine($"[BOOT] Failed to emit startup marker: {ex.GetType().Name} {ex.Message}");
}

// Global exception safety net (last resort) – surfaces unhandled pipeline exceptions as ProblemDetails
app.Use(async (ctx, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        try
        {
            var logger = ctx.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("GlobalException");
            logger?.LogError(ex, "[GLOBAL] Unhandled exception processing {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
            if (!ctx.Response.HasStarted)
            {
                ctx.Response.StatusCode = 500;
                ctx.Response.ContentType = "application/json";
                var payload = JsonSerializer.Serialize(new
                {
                    title = "Unhandled server error",
                    status = 500,
                    errorType = ex.GetType().FullName,
                    message = ex.Message
                });
                await ctx.Response.WriteAsync(payload);
            }
        }
        catch { /* swallow secondary failures */ }
    }
});

// Development-time OpenAPI endpoint (serves /openapi/v1.json) only on frameworks that provide MapOpenApi
#if NET10_0_OR_GREATER
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
#endif

// Temporarily disable HTTPS redirection for diagnostic clarity in smoke tests
// app.UseHttpsRedirection();

// Pipeline request logging middleware (diagnostic) – place BEFORE endpoint mappings
app.Use(async (ctx, next) =>
{
    Console.WriteLine($"[DIAG-Pipeline] -> {ctx.Request.Method} {ctx.Request.Path}");
    await next();
    Console.WriteLine($"[DIAG-Pipeline] <- {ctx.Request.Method} {ctx.Request.Path} {ctx.Response?.StatusCode}");
});

app.MapGet("/", () => "Hello World!")
    .WithName("Root")
    .WithSummary("Returns a simple greeting.")
    .WithDescription("Minimal root endpoint to verify the API is running.");

// Lightweight ping (no DB / DI) for early smoke validation
app.MapGet("/api/ping", () => new { utc = DateTime.UtcNow, status = "ok" })
    .WithName("Ping")
    .WithSummary("Ping endpoint (no dependencies)")
    .WithDescription("Returns a simple object to verify the process is responsive before hitting DB-backed endpoints.");

// DB ping: open connection & SELECT 1
app.MapGet("/api/dbping", async (ISpocRDbContext db, CancellationToken ct) =>
{
    try
    {
        Console.WriteLine("[DIAG-DbPing] opening-connection ts=" + DateTime.UtcNow.ToString("O"));
        // Emit masked connection string diagnostic
        try
        {
            var csField = db.GetType().GetField("_options", BindingFlags.NonPublic | BindingFlags.Instance);
            var options = csField?.GetValue(db);
            var csProp = options?.GetType().GetProperty("ConnectionString", BindingFlags.Public | BindingFlags.Instance);
            var rawCs = csProp?.GetValue(options) as string;
            if (!string.IsNullOrWhiteSpace(rawCs))
            {
                string Mask(string c)
                {
                    try
                    {
                        var parts = c.Split(';', StringSplitOptions.RemoveEmptyEntries);
                        string server = parts.FirstOrDefault(p => p.StartsWith("Server=", StringComparison.OrdinalIgnoreCase) || p.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase)) ?? "Server=?";
                        string dbName = parts.FirstOrDefault(p => p.StartsWith("Database=", StringComparison.OrdinalIgnoreCase) || p.StartsWith("Initial Catalog=", StringComparison.OrdinalIgnoreCase)) ?? "Database=?";
                        return server + ";" + dbName + ";...(masked)";
                    }
                    catch { return "<mask-failed>"; }
                }
                Console.WriteLine("[DIAG-DbPing] connection-string=" + Mask(rawCs));
            }
        }
        catch (Exception exMask)
        {
            Console.WriteLine("[DIAG-DbPing] mask-failed " + exMask.GetType().Name + " " + exMask.Message);
        }
        // Direct raw attempt (bypass context) to differentiate DI vs driver issues
        try
        {
            var directCs = Environment.GetEnvironmentVariable("SPOCR_SAMPLE_RESTAPI_DB");
            if (!string.IsNullOrWhiteSpace(directCs))
            {
                Console.WriteLine("[DIAG-DbPing] direct-open-attempt start ts=" + DateTime.UtcNow.ToString("O"));
                await using var direct = new Microsoft.Data.SqlClient.SqlConnection(directCs);
                await direct.OpenAsync(ct).ConfigureAwait(false);
                Console.WriteLine("[DIAG-DbPing] direct-open-success state=" + direct.State + " ts=" + DateTime.UtcNow.ToString("O"));
            }
            else
            {
                Console.WriteLine("[DIAG-DbPing] no direct connection string env present (SPOCR_SAMPLE_RESTAPI_DB)");
            }
        }
        catch (Exception exDirect)
        {
            Console.WriteLine("[DIAG-DbPing] direct-open-failed ex=" + exDirect.GetType().Name + " msg=" + exDirect.Message);
        }
        await using var conn = await db.OpenConnectionAsync(ct).ConfigureAwait(false);
        Console.WriteLine("[DIAG-DbPing] connection-state=" + conn.State + " ts=" + DateTime.UtcNow.ToString("O"));
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        Console.WriteLine("[DIAG-DbPing] executing-scalar ts=" + DateTime.UtcNow.ToString("O"));
        var scalar = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        Console.WriteLine("[DIAG-DbPing] success value=" + scalar + " ts=" + DateTime.UtcNow.ToString("O"));
        return Results.Ok(new { ok = true, value = scalar });
    }
    catch (Exception ex)
    {
        Console.WriteLine("[DIAG-DbPing] failure ex=" + ex.GetType().Name + " message=" + ex.Message + " ts=" + DateTime.UtcNow.ToString("O"));
        return Results.Problem("DB ping failed", statusCode: 500, extensions: new Dictionary<string, object?>
        {
            ["errorType"] = ex.GetType().FullName,
            ["exception"] = ex.ToString()
        });
    }
})
   .WithName("DbPing")
   .WithSummary("Database connectivity ping")
   .WithDescription("Opens a DB connection and executes SELECT 1 to validate connectivity independent of stored procedures.");

// Raw DB ping without DI (uses environment variable directly) for isolation
app.MapGet("/api/dbping/raw", async (CancellationToken ct) =>
{
    var directCs = Environment.GetEnvironmentVariable("SPOCR_SAMPLE_RESTAPI_DB");
    if (string.IsNullOrWhiteSpace(directCs)) return Results.Problem("SPOCR_SAMPLE_RESTAPI_DB not set", statusCode: 500);
    try
    {
        Console.WriteLine("[DIAG-DbPingRaw] opening ts=" + DateTime.UtcNow.ToString("O"));
        await using var conn = new SqlConnection(directCs);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        var val = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        Console.WriteLine("[DIAG-DbPingRaw] success ts=" + DateTime.UtcNow.ToString("O"));
        return Results.Ok(new { ok = true, value = val });
    }
    catch (Exception ex)
    {
        Console.WriteLine("[DIAG-DbPingRaw] failure " + ex.GetType().Name + " " + ex.Message);
        return Results.Problem("Raw DB ping failed", statusCode: 500, extensions: new Dictionary<string, object?>
        {
            ["errorType"] = ex.GetType().FullName,
            ["exception"] = ex.ToString()
        });
    }
})
   .WithName("DbPingRaw")
   .WithSummary("Direct env-based database ping")
   .WithDescription("Bypasses DI to differentiate connection string resolution vs driver / networking problems.");

// Map generated health endpoint (GET /spocr/health/db)
app.MapSpocRDbContextEndpoints();

// --- Debug / Diagnostics Endpoints ---
app.MapGet("/_debug/endpoints", (EndpointDataSource ds) =>
{
    var list = ds.Endpoints
        .OfType<RouteEndpoint>()
        .Select(e =>
        {
            var methodMeta = e.Metadata.OfType<IHttpMethodMetadata>().FirstOrDefault();
            return new
            {
                route = e.RoutePattern.RawText,
                order = e.Order,
                methods = methodMeta?.HttpMethods,
                displayName = e.DisplayName
            };
        })
        .OrderBy(e => e.route)
        .ToList();
    return Results.Ok(new { count = list.Count, list });
})
    .WithName("DebugEndpoints")
    .WithSummary("Lists all registered route endpoints")
    .WithDescription("Returns an array of all route patterns to diagnose stale builds / missing mappings.");

app.MapGet("/_debug/info", () =>
{
    var asm = Assembly.GetExecutingAssembly();
    var path = asm.Location;
    return Results.Ok(new
    {
        assembly = System.IO.Path.GetFileName(path),
        version = asm.GetName().Version?.ToString(),
        lastWriteUtc = System.IO.File.GetLastWriteTimeUtc(path),
        env = new
        {
            SPOCR_SAMPLE_RESTAPI_DB = Environment.GetEnvironmentVariable("SPOCR_SAMPLE_RESTAPI_DB") != null,
            SPOCR_SAMPLE_USERLIST_LIMIT = Environment.GetEnvironmentVariable("SPOCR_SAMPLE_USERLIST_LIMIT")
        }
    });
})
    .WithName("DebugInfo")
    .WithSummary("Returns assembly + environment diagnostic info")
    .WithDescription("Helps smoke script verify a fresh process (timestamps, version, env flags).");

// DI-only endpoint: validates that ISpocRDbContext can be injected without opening a DB connection
app.MapGet("/api/dbcontext/di", (ISpocRDbContext ctx) =>
{
    // We intentionally do NOT call OpenConnectionAsync here.
    // Provide minimal metadata (type name + hash of options object if accessible via reflection later).
    var typeName = ctx.GetType().FullName;
    return Results.Ok(new { ok = true, type = typeName });
})
    .WithName("DbContextDI")
    .WithSummary("Verifies DI wiring of the generated DbContext")
    .WithDescription("Injects ISpocRDbContext and returns its concrete type without touching the database.");
// --- End Debug / Diagnostics Endpoints ---

// Emit boot-ready marker after endpoint configuration & middleware registration
Console.WriteLine("[BOOT-READY] Pipeline configured.");


// --- SpocR vNext Sample Procedure Endpoints ---
// Simple list query (no input params)
app.MapGet("/api/users", async (ISpocRDbContext db, ILoggerFactory lf, CancellationToken ct) =>
{
    var log = lf.CreateLogger("UserListEndpoint");
    string stage = "init";
    void Stage(string s) { stage = s; Console.WriteLine($"[DIAG-UserList] stage={s} ts={DateTime.UtcNow:O}"); }
    try
    {
        Stage("open-connection");
        await using var conn = await db.OpenConnectionAsync(ct).ConfigureAwait(false);
        Console.WriteLine($"[DIAG-UserList] connection-state={conn.State}");
        Stage("execute-procedure");
        var swExec = System.Diagnostics.Stopwatch.StartNew();
        var result = await UserListProcedure.ExecuteAsync(conn, ct).ConfigureAwait(false);
        swExec.Stop();
        var fullCount = result.Result1.Count;
        Console.WriteLine($"[DIAG-UserList] rows={fullCount} execMs={swExec.ElapsedMilliseconds}");
        Stage("transform");
        int limit = 0;
        var limitEnv = Environment.GetEnvironmentVariable("SPOCR_SAMPLE_USERLIST_LIMIT");
        if (!string.IsNullOrWhiteSpace(limitEnv) && int.TryParse(limitEnv, out var parsed) && parsed > 0) limit = parsed;
        var items = (limit > 0 && fullCount > limit) ? result.Result1.Take(limit).ToList() : result.Result1;
        bool truncated = limit > 0 && fullCount > limit;
        Console.WriteLine($"[DIAG-UserList] limit={limit} truncated={truncated} returnedCount={items.Count}");
        Stage("serialize-test");
        // First serialize minimal payload to verify serializer works
        _ = JsonSerializer.Serialize(new { count = fullCount });
        Stage("serialize-full");
        string json;
        try
        {
            var payload = new { count = fullCount, truncated, items };
            json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
        }
        catch (Exception serEx)
        {
            Console.WriteLine($"[DIAG-UserList] serialization-ex {serEx.GetType().Name} {serEx.Message}");
            return Results.Problem("Serialization failed", statusCode: 500, extensions: new Dictionary<string, object?>
            {
                ["errorType"] = serEx.GetType().FullName,
                ["exception"] = serEx.ToString(),
                ["stage"] = stage
            });
        }
        Stage("return");
        Console.WriteLine($"[DIAG-UserList] jsonLength={json.Length}");
        return Results.Text(json, "application/json");
    }
    catch (SqlException sqlEx) when (sqlEx.Number == 2812 || sqlEx.Message.Contains("Could not find"))
    {
        Console.WriteLine($"[DIAG-UserList] sql-missing {sqlEx.Message}");
        return Results.Problem("Procedure samples.UserList not found", statusCode: 500, extensions: new Dictionary<string, object?> { ["errorType"] = "ProcedureMissing", ["stage"] = stage });
    }
    catch (IndexOutOfRangeException idxEx)
    {
        Console.WriteLine($"[DIAG-UserList] column-mismatch {idxEx.Message}");
        return Results.Problem("Column mapping mismatch (check snapshot vs. database schema)", statusCode: 500, extensions: new Dictionary<string, object?> { ["errorType"] = "ColumnMappingMismatch", ["stage"] = stage });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DIAG-UserList] unexpected-ex stage={stage} {ex.GetType().Name} {ex.Message}\n{ex}");
        return Results.Problem("User list failed", statusCode: 500, extensions: new Dictionary<string, object?>
        {
            ["errorType"] = ex.GetType().FullName,
            ["exception"] = ex.ToString(),
            ["stage"] = stage
        });
    }
})
    .WithName("UserList")
    .WithSummary("Returns a list of users (demo via stored procedure)")
    .WithDescription("Executes the generated stored procedure samples.UserList using the vNext generator.");

// Simple users namespace ping to confirm routing works
app.MapGet("/api/users/ping", () => Results.Ok(new { ok = true, utc = DateTime.UtcNow }))
    .WithName("UserListNamespacePing")
    .WithSummary("Ping under /api/users")
    .WithDescription("Helps diagnose whether the /api/users route group is reachable before executing the heavy list endpoint.");

// Diagnostic: just count users without serializing full list
app.MapGet("/api/users/count", async (ISpocRDbContext db, CancellationToken ct) =>
{
    try
    {
        await using var conn = await db.OpenConnectionAsync(ct).ConfigureAwait(false);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await UserListProcedure.ExecuteAsync(conn, ct).ConfigureAwait(false);
        sw.Stop();
        return Results.Ok(new { count = result.Result1.Count, execMs = sw.ElapsedMilliseconds });
    }
    catch (Exception ex)
    {
        return Results.Problem("Count failed", statusCode: 500, extensions: new Dictionary<string, object?>
        {
            ["errorType"] = ex.GetType().FullName,
            ["exception"] = ex.ToString()
        });
    }
})
    .WithName("UserListCount")
    .WithSummary("Returns only the user count for diagnostics")
    .WithDescription("Executes samples.UserList and returns only the row count + execution ms to isolate serialization issues.");

// Create user (demo of input + output + resultset)
app.MapPost("/api/users", async (CreateUserWithOutputInput body, ISpocRDbContext db, ILoggerFactory lf, CancellationToken ct) =>
{
    var log = lf.CreateLogger("CreateUserEndpoint");
    if (string.IsNullOrWhiteSpace(body.DisplayName) || string.IsNullOrWhiteSpace(body.Email))
        return Results.BadRequest(new { error = "DisplayName and Email are required" });
    try
    {
        await using var conn = await db.OpenConnectionAsync(ct).ConfigureAwait(false);
        var result = await CreateUserWithOutputProcedure.ExecuteAsync(conn, body, ct).ConfigureAwait(false);
        return Results.Created($"/api/users/{result.Output?.UserId}", new
        {
            createdUserId = result.Output?.UserId,
            echo = new { body.DisplayName, body.Email },
            resultSets = new { result1 = result.Result1 }
        });
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Failure executing samples.CreateUserWithOutput");
        return Results.Problem("User creation failed", statusCode: 500, extensions: new Dictionary<string, object?> { ["errorType"] = ex.GetType().Name });
    }
})
.WithName("CreateUserWithOutput")
.WithSummary("Creates a user (demo) and returns generated id + resultset")
.WithDescription("Executes samples.CreateUserWithOutput with input parameters and returns output parameter & first result set.");
// --- End SpocR vNext Sample Procedure Endpoints ---

app.Run();