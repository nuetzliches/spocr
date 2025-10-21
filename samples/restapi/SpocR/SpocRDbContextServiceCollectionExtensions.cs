namespace RestApi.SpocR;

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class SpocRDbContextServiceCollectionExtensions
{
    /// <summary>Register generated SpocRDbContext and its options.
    /// Connection string precedence (runtime only):
    /// 1) options.ConnectionString (delegate provided)
    /// 2) IConfiguration.GetConnectionString("DefaultConnection")
    /// </summary>
    public static IServiceCollection AddSpocRDbContext(this IServiceCollection services, Action<SpocRDbContextOptions>? configure = null)
    {
        var explicitOptions = new SpocRDbContextOptions();
        configure?.Invoke(explicitOptions);

        services.AddSingleton(provider =>
        {
            var cfg = provider.GetService<IConfiguration>();
            var name = explicitOptions.ConnectionStringName ?? "DefaultConnection";
            var conn = explicitOptions.ConnectionString ?? cfg?.GetConnectionString(name);
            if (string.IsNullOrWhiteSpace(conn))
                throw new InvalidOperationException($"No connection string resolved for SpocRDbContext (options / IConfiguration:GetConnectionString('{name}')).");
            explicitOptions.ConnectionString = conn;
            if (explicitOptions.CommandTimeout is null or <= 0) explicitOptions.CommandTimeout = 30;
            if (explicitOptions.MaxOpenRetries is not null and < 0)
                throw new InvalidOperationException("MaxOpenRetries must be >= 0");
            if (explicitOptions.RetryDelayMs is not null and <= 0)
                throw new InvalidOperationException("RetryDelayMs must be > 0");
            if (explicitOptions.JsonSerializerOptions == null)
            {
                var jsonOpts = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                };
                // Converters können später ergänzt werden (tolerant numerics etc.)
                explicitOptions.JsonSerializerOptions = jsonOpts;
            }
            return explicitOptions;
        });

        services.AddScoped<ISpocRDbContext>(sp => new SpocRDbContext(sp.GetRequiredService<SpocRDbContextOptions>()));
        return services;
    }
}
