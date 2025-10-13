using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SpocR.SpocRVNext.Data;

public static class SpocRDbContextServiceCollectionExtensions
{
    /// <summary>
    /// Registers the minimal SpocRDbContext. Connection string precedence:
    ///  1) options.ConnectionString
    ///  2) configuration.GetConnectionString("DefaultConnection")
    ///  3) environment variable SPOCR_DB_DEFAULT
    /// </summary>
    public static IServiceCollection AddSpocRDbContext(this IServiceCollection services, Action<SpocRDbContextOptions>? configure = null)
    {
        var opt = new SpocRDbContextOptions();
        configure?.Invoke(opt);

        services.AddSingleton(provider =>
        {
            if (string.IsNullOrWhiteSpace(opt.ConnectionString))
            {
                var cfg = provider.GetService<IConfiguration>();
                var fromConfig = cfg?.GetConnectionString("DefaultConnection");
                var fromEnv = Environment.GetEnvironmentVariable("SPOCR_DB_DEFAULT");
                opt.ConnectionString = fromConfig ?? fromEnv ?? throw new InvalidOperationException("No connection string resolved for SpocRDbContext (provide via options, config DefaultConnection or SPOCR_DB_DEFAULT).");
            }
            return new SpocRDbContext(opt.ConnectionString!);
        });

        return services;
    }
}
