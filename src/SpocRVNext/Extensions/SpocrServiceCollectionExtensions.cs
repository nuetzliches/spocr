using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SpocR.SpocRVNext.Models;
using SpocR.SpocRVNext.Runtime;
using SpocR.Schema;
using SpocR.Services;
using SpocR.SpocRVNext.Cli;
using SpocR.SpocRVNext.Engine;
using SpocR.SpocRVNext.Generators;
using SpocR.SpocRVNext.SnapshotBuilder;
using SpocR.Utils;
using SpocRVNext.Configuration;
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
                options.GlobalConfigPath = GetGlobalConfigPath();
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
            return new FileManager<GlobalConfigurationModel>(
                spocrService,
                options.Value.GlobalConfigPath,
                spocrService.GetGlobalDefaultConfiguration());
        });

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

    private static string GetGlobalConfigPath()
    {
#if DEBUG
        return Path.Combine(DirectoryUtils.GetWorkingDirectory(), Constants.GlobalConfigurationFile);
#else
        return Path.Combine(DirectoryUtils.GetAppDataDirectory(), Constants.GlobalConfigurationFile);
#endif
    }
}

public class SpocROptions
{
    public string GlobalConfigPath { get; set; }
    public string LocalConfigPath { get; set; }
}
