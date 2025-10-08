using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SpocR.Samples.WebApi.Data;

var builder = WebApplication.CreateBuilder(args);

// Add minimal OpenAPI support
builder.Services.AddOpenApi();

builder.Services.AddSpocRDbContext(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Server=localhost;Database=SpocRSample;User Id=sa;Password=CHANGE_ME;TrustServerCertificate=True";
    options.CommandTimeout = 45;
    options.JsonSerializerOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/", () => "Hello World!")
    .WithName("Root")
    .WithSummary("Returns a simple greeting.")
    .WithDescription("Minimal root endpoint to verify the API is running.");

// Debug Endpoint: einfache Roundtrip-Prüfung (SELECT 1)
app.MapGet("/api/ping/db", async (ISpocRDbContext db, CancellationToken ct) =>
{
    var value = await db.ExecuteScalarAsync<int>("SELECT 1", cancellationToken: ct);
    return Results.Ok(new { ok = true, value });
}).WithSummary("Checks DB connectivity via SpocRDbContext")
  .WithDescription("Executes a lightweight SELECT 1 using the modern SpocRDbContext abstraction.");

// Beispiel für Stored Procedure ohne Parameter (falls vorhanden):
// app.MapGet("/api/user/count", async (ISpocRDbContext db, CancellationToken ct) =>
// {
//     var count = await db.ExecuteScalarAsync<int>("[dbo].[UserCount]", cancellationToken: ct);
//     return Results.Ok(new { count });
// });

app.Run();