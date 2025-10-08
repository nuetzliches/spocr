using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpocR.Models;
using SpocR.Services;

namespace SpocR.Extensions;

/// <summary>
/// Modern SpocR configuration options for .NET 10
/// </summary>
public class SpocRConfigurationOptions
{
    public string ConfigurationFile { get; set; } = "spocr.json";
    public bool EnableAutoUpdate { get; set; } = true;
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public bool EnableHealthChecks { get; set; } = true;
    public bool EnableMetrics { get; set; } = true;
    public LogLevel LogLevel { get; set; } = LogLevel.Information;
}

/// <summary>
/// Modern extension methods for registering SpocR services in .NET 10
/// </summary>
public static class ModernSpocrServiceCollectionExtensions
{
    /// <summary>
    /// Adds SpocR services with modern .NET 10 configuration patterns
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The application configuration</param>
    /// <param name="configureOptions">Optional configuration options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddSpocR(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<SpocRConfigurationOptions>? configureOptions = null)
    {
        // Configure options with validation
        services.AddOptions<SpocRConfigurationOptions>()
            .Configure(options =>
            {
                // Bind from configuration
                configuration.GetSection("SpocR").Bind(options);
                
                // Apply custom configuration
                configureOptions?.Invoke(options);
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Add core SpocR services
        services.AddSpocRCore();
        
        // Add health checks if enabled
        var tempProvider = services.BuildServiceProvider();
        var options = tempProvider.GetRequiredService<IOptions<SpocRConfigurationOptions>>().Value;
        
        if (options.EnableHealthChecks)
        {
            services.AddSpocRHealthChecks();
        }

        if (options.EnableMetrics)
        {
            services.AddSpocRMetrics();
        }

        return services;
    }

    /// <summary>
    /// Adds the generated SpocR DbContext with modern configuration
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="connectionName">Name of the connection string</param>
    /// <param name="configureOptions">Optional DbContext configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddSpocRDbContext(
        this IServiceCollection services,
        string connectionName = "DefaultConnection",
        Action<AppDbContextOptions>? configureOptions = null)
    {
        services.AddScoped<IAppDbContext>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString(connectionName)
                ?? throw new InvalidOperationException($"Connection string '{connectionName}' not found.");

            var options = new AppDbContextOptions();
            configureOptions?.Invoke(options);

            return new AppDbContext(configuration, Options.Create(options));
        });

        return services;
    }

    /// <summary>
    /// Adds SpocR with Keyed Services (new in .NET 8+)
    /// </summary>
    public static IServiceCollection AddSpocRWithKey(
        this IServiceCollection services,
        string serviceKey,
        IConfiguration configuration,
        Action<SpocRConfigurationOptions>? configureOptions = null)
    {
        services.AddKeyedSingleton<ISpocrService>(serviceKey, (provider, key) =>
            new SpocrService(/* dependencies */));

        return services.AddSpocR(configuration, configureOptions);
    }

    /// <summary>
    /// Add health checks for SpocR
    /// </summary>
    private static IServiceCollection AddSpocRHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<SpocRHealthCheck>("spocr");

        return services;
    }

    /// <summary>
    /// Add metrics for SpocR
    /// </summary>
    private static IServiceCollection AddSpocRMetrics(this IServiceCollection services)
    {
        // Add meters and metrics
        services.AddSingleton<SpocRMetrics>();
        
        return services;
    }

    /// <summary>
    /// Core SpocR services registration
    /// </summary>
    private static IServiceCollection AddSpocRCore(this IServiceCollection services)
    {
        // Register all existing SpocR services from the original implementation
        return services.AddSpocR(); // Delegate to existing implementation
    }
}

/// <summary>
/// Interface for SpocR service (for better testability)
/// </summary>
public interface ISpocrService
{
    Task<string> GetVersionAsync();
    // Add other methods as needed
}

/// <summary>
/// Health check for SpocR services
/// </summary>
public class SpocRHealthCheck : IHealthCheck
{
    private readonly ISpocrService _spocrService;

    public SpocRHealthCheck(ISpocrService spocrService)
    {
        _spocrService = spocrService;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var version = await _spocrService.GetVersionAsync();
            return HealthCheckResult.Healthy($"SpocR is healthy. Version: {version}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SpocR is not responding", ex);
        }
    }
}

/// <summary>
/// Metrics for SpocR operations
/// </summary>
public class SpocRMetrics
{
    private readonly Meter _meter;
    private readonly Counter<int> _operationsCounter;
    private readonly Histogram<double> _operationDuration;

    public SpocRMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create("SpocR");
        _operationsCounter = _meter.CreateCounter<int>("spocr_operations_total", "operations", "Total number of SpocR operations");
        _operationDuration = _meter.CreateHistogram<double>("spocr_operation_duration", "seconds", "Duration of SpocR operations");
    }

    public void RecordOperation(string operationType, double duration)
    {
        _operationsCounter.Add(1, new("operation_type", operationType));
        _operationDuration.Record(duration, new("operation_type", operationType));
    }
}