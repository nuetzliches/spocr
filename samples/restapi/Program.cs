using RestApi.SpocR;           // generated DbContext extensions
using RestApi.SpocR.Samples;   // generated procedure wrappers
using Microsoft.Data.SqlClient; // for granular SqlException handling
using Microsoft.AspNetCore.Http; // for Results

var builder = WebApplication.CreateBuilder(args);

// Optional OpenAPI (only available on net10+)
#if NET10_0_OR_GREATER
builder.Services.AddOpenApi();
#endif

builder.Services.AddSpocRDbContext(o =>
{
    // Pull from configuration (appsettings + overrides). If null an exception will surface early during build.
    o.ConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    // Stabilization tweaks for sample: a few lightweight retries for cold container startup
    o.MaxOpenRetries = 3;          // retry failed opens (e.g. SQL container not yet accepting connections)
    o.RetryDelayMs = 300;          // small backoff (total worst-case ~900ms + connection attempts)
    o.EnableDiagnostics = true;    // emit basic timing/debug info to Debug output for local troubleshooting
    o.CommandTimeout = 30;         // explicit for clarity (matches default)
});

var app = builder.Build();

// Dev-time OpenAPI endpoint (/openapi/v1.json)
#if NET10_0_OR_GREATER
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
#endif

app.UseHttpsRedirection();

app.MapGet("/", () => new { utc = DateTime.UtcNow, status = "ok" })
    .WithName("Ping");

// Simple list query
app.MapGet("/api/users", async (ISpocRDbContext db, ILoggerFactory lf, CancellationToken ct) =>
{
    var log = lf.CreateLogger("UserListEndpoint");
    try
    {
        var agg = await db.UserListAsync(ct).ConfigureAwait(false);
        // Normalize payload for client friendliness (explicit items + count) while still exposing raw aggregate if needed later.
        return Results.Ok(new { count = agg.Result?.Count ?? 0, items = agg.Result });
    }
    catch (SqlException sqlEx)
    {
        // Typical transient: DB not up yet or network issue → 503 instead of opaque 500
        log.LogError(sqlEx, "UserList database access failure");
        return Results.Problem("Database unavailable", statusCode: 503, extensions: new Dictionary<string, object?>
        {
            ["errorType"] = sqlEx.GetType().Name,
            ["sqlState"] = sqlEx.State,
            ["number"] = sqlEx.Number
        });
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Unhandled error executing UserList");
        return Results.Problem("Internal error", statusCode: 500, extensions: new Dictionary<string, object?>
        {
            ["errorType"] = ex.GetType().Name
        });
    }
})
.WithName("UserList");

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
            resultSets = new { result = result.Result }
        });
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Failure executing samples.CreateUserWithOutput");
        return Results.Problem("User creation failed", statusCode: 500, extensions: new Dictionary<string, object?> { ["errorType"] = ex.GetType().Name });
    }
})
.WithName("CreateUserWithOutput");

app.Run();