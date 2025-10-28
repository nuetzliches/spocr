using System.IO;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SpocR.CodeGenerators;
using SpocR.CodeGenerators.Models;
using SpocR.CodeGenerators.Utils;
using SpocR.Commands;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;
using SpocR.Utils;
using SpocRVNext.Configuration;
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
            services.AddSingleton(PhysicalConsole.Singleton);
            services.TryAddSingleton<CommandOptions>();

            // Console service with enhanced logging capabilities
            services.AddSingleton<IConsoleService>(provider =>
                new ConsoleService(
                    provider.GetRequiredService<IConsole>(),
                    provider.GetRequiredService<CommandOptions>()));

            // Core SpocR services
            services.AddSingleton<SpocrService>();

            // Manager services with optimized lifecycle
            AddManagerServices(services);

            // File management services
            AddFileManagers(services);

            // Code generation services
            AddCodeGenerators(services);

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
            services.AddSingleton<SpocrManager>();
            services.AddSingleton<SpocrProjectManager>();
            services.AddSingleton<SpocrSchemaManager>();
            services.AddSingleton<SpocrConfigManager>();
            // Local metadata cache (stored procedure modify_date snapshot)
            services.AddSingleton<ILocalCacheService, LocalCacheService>();
            services.AddSingleton<ISchemaSnapshotService, SchemaSnapshotService>();
            services.AddSingleton<ISchemaMetadataProvider, SnapshotSchemaMetadataProvider>();
            services.AddSingleton<SnapshotMaintenanceManager>();
            // vNext mode provider
            services.AddSingleton<IGeneratorModeProvider, EnvGeneratorModeProvider>();
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
        /// Registers code generation services in the service collection
        /// </summary>
        private static void AddCodeGenerators(IServiceCollection services)
        {
            // Template and generator services
            services.AddSingleton<TemplateManager>();
            services.AddSingleton<InputGenerator>();
            services.AddSingleton<ModelGenerator>();
            services.AddSingleton<OutputGenerator>();
            services.AddSingleton<TableTypeGenerator>();
            services.AddSingleton<StoredProcedureGenerator>();
            services.AddSingleton<CrudResultGenerator>();
            // vNext Generators (feature gated)
            services.AddSingleton<SpocR.SpocRVNext.Generators.DbContextGenerator>();

            // Register the orchestrator as the final component
            services.AddSingleton<CodeGenerationOrchestrator>();
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
