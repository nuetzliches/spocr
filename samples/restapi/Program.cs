using System;
using System.Data.Common;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RestApi.SpocR; // generated DbContext extensions
using RestApi.SpocR.Samples; // generated sample stored procedure wrappers

var builder = WebApplication.CreateBuilder(args);

// Add minimal OpenAPI support
builder.Services.AddOpenApi();

// No direct SpocR library reference: generated code (if any) would live under Spocr/.
// This sample stays framework-agnostic regarding the generator implementation.

// Register generated lightweight DbContext with layered connection resolution:
builder.Services.AddSpocRDbContext(o =>
{
    var cs = Environment.GetEnvironmentVariable("SPOCR_SAMPLE_RESTAPI_DB");
    if (string.IsNullOrWhiteSpace(cs))
    {
        // fallback chain: LocalDB (Windows) -> localhost:1433
        cs = OperatingSystem.IsWindows()
            ? "Server=(localdb)\\MSSQLLocalDB;Database=SpocRSample;Trusted_Connection=True;"
            : "Server=localhost,1433;Database=SpocRSample;User ID=sa;Password=YourStrong!Passw0rd;Encrypt=True;TrustServerCertificate=True;"; // dev placeholder
    }
    o.ConnectionString = cs;
});

var app = builder.Build();

// Development-time OpenAPI endpoint (serves /openapi/v1.json)
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/", () => "Hello World!")
    .WithName("Root")
    .WithSummary("Returns a simple greeting.")
    .WithDescription("Minimal root endpoint to verify the API is running.");

// Map generated health endpoint (GET /spocr/health/db)
app.MapSpocRDbContextEndpoints();

// --- SpocR vNext Sample Procedure Endpoints ---
// Simple list query (no input params)
app.MapGet("/api/users", async (ISpocRDbContext db, ILoggerFactory lf, CancellationToken ct) =>
{
    var log = lf.CreateLogger("UserListEndpoint");
    string stage = "init";
    try
    {
        stage = "open-connection";
        log.LogInformation("Opening DB connection...");
        await using var conn = await db.OpenConnectionAsync(ct).ConfigureAwait(false);
        log.LogInformation("Connection opened. State={State}", conn.State);
        stage = "execute-procedure";
        var result = await UserListProcedure.ExecuteAsync(conn, ct).ConfigureAwait(false);
        log.LogInformation("Procedure executed. Rows={Count}", result.Result1.Count);
        stage = "serialize";
        var payload = new { count = result.Result1.Count, items = result.Result1 };
        log.LogInformation("Returning payload");
        return Results.Ok(payload);
    }
    catch (SqlException sqlEx) when (sqlEx.Number == 2812 || sqlEx.Message.Contains("Could not find")) // could not find stored procedure
    {
        log.LogError(sqlEx, "Stored procedure samples.UserList not found or not deployed");
        return Results.Problem("Procedure samples.UserList not found", statusCode: 500, extensions: new Dictionary<string, object?> { ["errorType"] = "ProcedureMissing" });
    }
    catch (IndexOutOfRangeException idxEx)
    {
        log.LogError(idxEx, "Column mapping mismatch for samples.UserList result set");
        return Results.Problem("Column mapping mismatch (check snapshot vs. database schema)", statusCode: 500, extensions: new Dictionary<string, object?> { ["errorType"] = "ColumnMappingMismatch" });
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Failure executing samples.UserList at stage {Stage}", stage);
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