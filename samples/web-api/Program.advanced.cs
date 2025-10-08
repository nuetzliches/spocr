// using Microsoft.AspNetCore.Mvc;
// using SpocR.Extensions;
// using System.ComponentModel.DataAnnotations;

// var builder = WebApplication.CreateBuilder(args);

// // Add services
// builder.Services.AddOpenApi();
// builder.Services.AddProblemDetails();

// // SpocR with full configuration
// builder.Services.AddSpocR(builder.Configuration, options =>
// {
//     options.ConfigurationFile = "spocr.json";
//     options.EnableAutoUpdate = true;
//     options.CommandTimeout = TimeSpan.FromSeconds(45);
//     options.EnableHealthChecks = true;
//     options.EnableMetrics = true;
// });

// // Add generated DbContext
// builder.Services.AddSpocRDbContext("DefaultConnection", options =>
// {
//     options.CommandTimeout = 45;
//     options.JsonSerializerOptions = new JsonSerializerOptions
//     {
//         PropertyNameCaseInsensitive = true,
//         PropertyNamingPolicy = JsonNamingPolicy.CamelCase
//     };
// });

// // Add API versioning (modern .NET pattern)
// builder.Services.AddApiVersioning(options =>
// {
//     options.DefaultApiVersion = new ApiVersion(1, 0);
//     options.AssumeDefaultVersionWhenUnspecified = true;
//     options.ApiVersionReader = ApiVersionReader.Combine(
//         new UrlSegmentApiVersionReader(),
//         new HeaderApiVersionReader("X-Version"),
//         new QueryStringApiVersionReader("version")
//     );
// }).AddApiExplorer(setup =>
// {
//     setup.GroupNameFormat = "'v'VVV";
//     setup.SubstituteApiVersionInUrl = true;
// });

// // Add CORS
// builder.Services.AddCors(options =>
// {
//     options.AddPolicy("AllowAll", policy =>
//         policy.AllowAnyOrigin()
//               .AllowAnyMethod()
//               .AllowAnyHeader());
// });

// // Add Output Caching (new in .NET 7+)
// builder.Services.AddOutputCache(options =>
// {
//     options.AddBasePolicy(builder => builder.Expire(TimeSpan.FromMinutes(5)));
// });

// var app = builder.Build();

// // Configure pipeline
// app.UseExceptionHandler();
// app.UseCors("AllowAll");
// app.UseOutputCache();

// if (app.Environment.IsDevelopment())
// {
//     app.MapOpenApi();
//     app.UseDeveloperExceptionPage();
// }

// app.UseHttpsRedirection();

// // Health checks endpoint
// app.MapHealthChecks("/health", new HealthCheckOptions
// {
//     ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
// });

// // SpocR API endpoints
// var spocrApi = app.MapGroup("/api/v{version:apiVersion}/spocr")
//     .WithApiVersionSet()
//     .WithOpenApi();

// // Example: Get all users with caching
// spocrApi.MapGet("/users", async (
//     IAppDbContext db,
//     [FromQuery] int page = 1,
//     [FromQuery] int pageSize = 20,
//     CancellationToken cancellationToken = default) =>
// {
//     // Example of using generated SpocR stored procedures
//     // var users = await db.GetUsersPagedAsync(page, pageSize, cancellationToken);
    
//     return Results.Ok(new 
//     { 
//         Message = $"Getting users - Page {page}, Size {pageSize}",
//         Data = new[] { new { Id = 1, Name = "John Doe" } }
//     });
// })
// .WithName("GetUsers")
// .WithSummary("Get paginated list of users")
// .WithDescription("Retrieves a paginated list of users from the database")
// .CacheOutput(policy => policy.Expire(TimeSpan.FromMinutes(5)))
// .Produces<PaginatedResponse<UserDto>>(StatusCodes.Status200OK)
// .ProducesValidationProblem();

// class UserCreateRequest {
//     [Required, StringLength(100, MinimumLength = 2)]
//     public required string Name { get; init; }

//     [Required, EmailAddress, StringLength(255)]
//     public required string Email { get; init; }
// }

// // Example: Create user with validation
// spocrApi.MapPost("/users", async (
//     [FromBody] UserCreateRequest request,
//     IAppDbContext db,
//     CancellationToken cancellationToken = default) =>
// {
//     // Validate request
//     var validationResults = new List<ValidationResult>();
//     var validationContext = new ValidationContext(request);
    
//     if (!Validator.TryValidateObject(request, validationContext, validationResults, true))
//     {
//         return Results.ValidationProblem(validationResults.ToDictionary(
//             r => r.MemberNames.FirstOrDefault() ?? "Unknown",
//             r => new[] { r.ErrorMessage ?? "Validation error" }));
//     }

//     // Example of using generated SpocR stored procedures
//     // var userId = await db.CreateUserAsync(request.Name, request.Email, cancellationToken);
    
//     var newUser = new UserDto(1, request.Name, request.Email);
    
//     return Results.Created($"/api/v1/spocr/users/{newUser.Id}", newUser);
// })
// .WithName("CreateUser")
// .WithSummary("Create a new user")
// .WithDescription("Creates a new user in the database")
// .Accepts<CreateUserRequest>("application/json")
// .Produces<UserDto>(StatusCodes.Status201Created)
// .ProducesValidationProblem()
// .ProducesProblem(StatusCodes.Status500InternalServerError);

// // Example: Get user by ID
// spocrApi.MapGet("/users/{id:int}", async (
//     int id,
//     IAppDbContext db,
//     CancellationToken cancellationToken = default) =>
// {
//     if (id <= 0)
//         return Results.BadRequest("Invalid user ID");

//     // Example of using generated SpocR stored procedures
//     // var user = await db.GetUserByIdAsync(id, cancellationToken);
    
//     // Simulate user lookup
//     var user = id == 1 ? new UserDto(1, "John Doe", "john@example.com") : null;
    
//     return user is not null 
//         ? Results.Ok(user) 
//         : Results.NotFound($"User with ID {id} not found");
// })
// .WithName("GetUserById")
// .WithSummary("Get user by ID")
// .WithDescription("Retrieves a specific user by their ID")
// .CacheOutput(policy => policy.Expire(TimeSpan.FromMinutes(10)))
// .Produces<UserDto>(StatusCodes.Status200OK)
// .Produces(StatusCodes.Status404NotFound)
// .ProducesProblem(StatusCodes.Status500InternalServerError);

// app.Run();

// // DTOs for the API
// public record UserDto(int Id, string Name, string Email);

// public record CreateUserRequest
// {
//     [Required, StringLength(100, MinimumLength = 2)]
//     public required string Name { get; init; }
    
//     [Required, EmailAddress, StringLength(255)]
//     public required string Email { get; init; }
// }

// public record PaginatedResponse<T>(
//     IEnumerable<T> Data,
//     int Page,
//     int PageSize,
//     int TotalCount,
//     int TotalPages
// );

// // Extension for generated interface (would be in generated code)
// public interface IAppDbContext : IDisposable
// {
//     Task<UserDto[]> GetUsersPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default);
//     Task<int> CreateUserAsync(string name, string email, CancellationToken cancellationToken = default);
//     Task<UserDto?> GetUserByIdAsync(int id, CancellationToken cancellationToken = default);
// }