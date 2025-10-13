using System;
using System.Data.Common;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using RestApi.SpocR; // generated DbContext extensions

var builder = WebApplication.CreateBuilder(args);

// Add minimal OpenAPI support
builder.Services.AddOpenApi();

// No direct SpocR library reference: generated code (if any) would live under Spocr/.
// This sample stays framework-agnostic regarding the generator implementation.

// Register generated lightweight DbContext (explicit connection override to ensure startup succeeds)
builder.Services.AddSpocRDbContext(o =>
{
    // Local dev fallback: adjust to your SQL Server or LocalDB instance
    o.ConnectionString = "Server=localhost;Database=SpocRSample;Trusted_Connection=True;TrustServerCertificate=True;";
    // o.ValidateOnBuild = true; // enable if you want fast-fail on unreachable DB
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

app.Run();