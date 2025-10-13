using System.Data.Common;

var builder = WebApplication.CreateBuilder(args);

// Add minimal OpenAPI support
builder.Services.AddOpenApi();

builder.Services.AddSpocRDbContext(options =>
{
    // etwa so?
    // wenn die Connection gesetzt wird, muss AddSpocRDbContext einen Service providen ISpocRDbConnection
    // in den alten Role.Kind = "Lib" soll dann dieser Service injected werden (bzw. generell als optionaler Default)
    options.Connection = Connection.FromConnectionString("DefaultConnection");
    // oder ?
    options.ConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.CommandTimeout = 45;
    options.JsonSerializerOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // wir benötigen konfigurierbare AutoMapper für Input TableTypes
    // z.B. soll ein [sample].[PrincipalTableType] in mit dem Namen @Principal gefüllt werden können
    // z.B. UserId aus dem BearerToken extrahieren und zuweisen
    // die Herausforderung: daraufhin sollen die InputModels (DTOs) dieses Feld nicht mehr im Konstruktor haben

    // Wie handhaben wir z.B. ein AutoTrim für Strings?
    // Wie handhaben wir z.B. DateTimeKind (z.B. UTC)?
    // options.InputConverter = ...

    // zu planen, wie wir einzelne Stored Procedures konfigurieren können
    // options.StoredProcedures.UserFind.Timeout = 60; // Override specific stored procedure timeout
    // options.StoredProcedures.UserFind.JsonSerializerOptions = new JsonSerializerOptions
    // {
    //     PropertyNameCaseInsensitive = true,
    //     PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    // };
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

app.Run();