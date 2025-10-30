using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SpocR.SpocRVNext.Models;
using SpocR.SpocRVNext.Runtime;
using SpocR.SpocRVNext.Schema;
using SpocR.SpocRVNext.Services;
using SpocR.SpocRVNext.Cli;
using SpocR.SpocRVNext.Engine;
using SpocR.SpocRVNext.Generators;
using SpocR.SpocRVNext.SnapshotBuilder;
using SpocR.SpocRVNext.Configuration;
using SpocR.SpocRVNext.Infrastructure;

namespace SpocR.SpocRVNext.Extensions;

/// <summary>
/// Extension methods for registering SpocR services in the DI container.
/// </summary>
public static class SpocrServiceCollectionExtensions
{
    public static IServiceCollection AddSpocR(this IServiceCollection services)
    {
        services.AddOptions<SpocROptions>()
            .Configure(options =>
            {
                options.LocalConfigPath = Constants.ConfigurationFile;
            });

        services.TryAddSingleton<CommandOptions>();

        services.AddSingleton<IConsoleService>(provider =>
            new ConsoleService(provider.GetRequiredService<CommandOptions>()));

        services.AddSingleton<SpocrService>();

        AddManagerServices(services);
        AddFileManagers(services);

        services.AddSingleton<DbContextGenerator>();
        services.AddSnapshotBuilder();

        return services;
    }

    private static void AddManagerServices(IServiceCollection services)
    {
        services.AddSingleton<OutputService>();
        services.AddSingleton<Services.SchemaSnapshotFileLayoutService>();
        services.AddSingleton<SchemaManager>();
        services.AddSingleton<SpocrCliRuntime>();
        services.AddSingleton<ILocalCacheService, LocalCacheService>();
        services.AddSingleton<ISchemaSnapshotService, SchemaSnapshotService>();
        services.AddSingleton<ISchemaMetadataProvider, SnapshotSchemaMetadataProvider>();
    }

    private static void AddFileManagers(IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<SpocROptions>>();
            var spocrService = provider.GetRequiredService<SpocrService>();
            return new FileManager<ConfigurationModel>(
                spocrService,
                options.Value.LocalConfigPath,
                spocrService.GetDefaultConfiguration());
        });
    }
}

public class SpocROptions
{
    public string LocalConfigPath { get; set; } = string.Empty;
}
