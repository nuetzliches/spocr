using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SpocR.Commands;
using SpocR.Infrastructure;
using SpocR.Runtime;
using SpocR.Schema;
using SpocR.Models;
using SpocR.Services;
using SpocR.Utils;
using SpocR.SpocRVNext.SnapshotBuilder;

namespace SpocR.Extensions
{
    /// <summary>
    /// Extension methods for registering SpocR services in the DI container
    /// </summary>
    public static class SpocrServiceCollectionExtensions
    {
        /// <summary>
        /// Adds all SpocR services to the service collection
        /// </summary>
        public static IServiceCollection AddSpocR(this IServiceCollection services)
        {
            // SpocR configuration
            services.AddOptions<SpocROptions>()
                .Configure(options =>
                {
                    options.GlobalConfigPath = GetGlobalConfigPath();
                    options.LocalConfigPath = Constants.ConfigurationFile;
                });

            // Core Services
            services.TryAddSingleton<CommandOptions>();

            // Console service with enhanced logging capabilities
            services.AddSingleton<IConsoleService>(provider =>
                new ConsoleService(
                    provider.GetRequiredService<CommandOptions>()));

            // Core SpocR services
            services.AddSingleton<SpocrService>();

            // Manager services with optimized lifecycle
            AddManagerServices(services);

            // File management services
            AddFileManagers(services);

            services.AddSingleton<SpocR.SpocRVNext.Generators.DbContextGenerator>();
            services.AddSnapshotBuilder();

            return services;
        }

        /// <summary>
        /// Registers manager services in the service collection
        /// </summary>
        private static void AddManagerServices(IServiceCollection services)
        {
            services.AddSingleton<OutputService>();
            services.AddSingleton<Services.SchemaSnapshotFileLayoutService>();
            services.AddSingleton<SchemaManager>();
            services.AddSingleton<SpocrCliRuntime>();
            // Local metadata cache (stored procedure modify_date snapshot)
            services.AddSingleton<ILocalCacheService, LocalCacheService>();
            services.AddSingleton<ISchemaSnapshotService, SchemaSnapshotService>();
            services.AddSingleton<ISchemaMetadataProvider, SnapshotSchemaMetadataProvider>();
        }

        /// <summary>
        /// Registers file management services in the service collection
        /// </summary>
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
        /// <summary>
        /// Determines the path to the global configuration file
        /// </summary>
        private static string GetGlobalConfigPath()
        {
#if DEBUG
            return Path.Combine(DirectoryUtils.GetWorkingDirectory(), Constants.GlobalConfigurationFile);
#else
            return Path.Combine(DirectoryUtils.GetAppDataDirectory(), Constants.GlobalConfigurationFile);
#endif
        }
    }

    /// <summary>
    /// Options for the SpocR configuration
    /// </summary>
    public class SpocROptions
    {
        public string GlobalConfigPath { get; set; }
        public string LocalConfigPath { get; set; }
    }
}
