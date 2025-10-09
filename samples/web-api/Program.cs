using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SpocR.Samples.WebApi.Data;
using spocr.DataContext;
using spocr.DataContext.StoredProcedures.Samples;
using spocr.DataContext.Models.Samples;

var builder = WebApplication.CreateBuilder(args);

// Add minimal OpenAPI support
builder.Services.AddOpenApi();

builder.Services.AddSpocRDbContext(options =>
{
    // defaults
    // options.ConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    // options.CommandTimeout = 30;
    // options.JsonSerializerOptions = new JsonSerializerOptions
    // {
    //     PropertyNameCaseInsensitive = true,
    //     PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    //     DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    // };
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

// Sample endpoints using generated SpocR wrappers (JSON -> typed models)
app.MapGet("/api/samples/orders", async (IAppDbContext db, CancellationToken ct) =>
{
    var list = await db.OrderListAsJsonDeserializeAsync(ct);
    return Results.Ok(list ?? new List<OrderListAsJson>());
}).WithName("GetOrders")
  .WithSummary("Returns a list of orders (sample)")
  .WithDescription("Calls [samples].[OrderListAsJson] and deserializes JSON into typed models.");

app.MapGet("/api/samples/orders/by-user/{userId:int}", async (IAppDbContext db, int userId, CancellationToken ct) =>
{
    var model = await db.OrderListByUserAsJsonDeserializeAsync(new spocr.DataContext.Inputs.Samples.OrderListByUserAsJsonInput { UserId = userId }, ct);
    return Results.Ok(model);
}).WithName("GetOrdersByUser")
  .WithSummary("Returns orders by user (sample)")
  .WithDescription("Calls [samples].[OrderListByUserAsJson] using generated input and typed output.");

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
