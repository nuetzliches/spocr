var builder = WebApplication.CreateBuilder(args);

// Add minimal OpenAPI support
builder.Services.AddOpenApi();

builder.Services.AddSpocRDbContext(options =>
{
    // optionale Konfiguration, presets in der Implementierung
    options.ConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.CommandTimeout = 45;
    options.JsonSerializerOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    // TODO wie werden einzelne Stored Procedures konfiguriert?
    // erst beim Aufruf der Stored Procedure innerhalb der API-Methode?
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