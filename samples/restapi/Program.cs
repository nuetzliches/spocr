using System;
using System.Data.Common;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Add minimal OpenAPI support
builder.Services.AddOpenApi();

// No direct SpocR library reference: generated code (if any) would live under Spocr/.
// This sample stays framework-agnostic regarding the generator implementation.

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

// Simple health check using raw ADO.NET (keine Library-Abhängigkeit)
app.MapGet("/health/db", (IConfiguration config) =>
{
    var cs = config.GetConnectionString("DefaultConnection") ?? Environment.GetEnvironmentVariable("SPOCR_DB_DEFAULT");
    if (string.IsNullOrWhiteSpace(cs))
        return Results.Problem(title: "Missing connection string", detail: "Provide DefaultConnection or SPOCR_DB_DEFAULT.");
    try
    {
        using var conn = new SqlConnection(cs);
        conn.Open();
        return Results.Ok(new { status = "ok", db = conn.State.ToString() });
    }
    catch (Exception ex)
    {
        return Results.Problem(title: "Database connection failed", detail: ex.Message);
    }
}).WithSummary("Database connectivity health check").WithDescription("Opens a SQL connection using configured connection string.");

app.Run();