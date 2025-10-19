using RestApi.SpocR;           // generated DbContext extensions
using RestApi.SpocR.Samples;   // generated procedure wrappers

var builder = WebApplication.CreateBuilder(args);

// Optional OpenAPI (only available on net10+)
#if NET10_0_OR_GREATER
builder.Services.AddOpenApi();
#endif

builder.Services.AddSpocRDbContext(o =>
{
    o.ConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
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
app.MapGet("/api/users", async (ISpocRDbContext db, CancellationToken ct) =>
{
    var users = await db.UserListAsync(ct).ConfigureAwait(false);
    return users;
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