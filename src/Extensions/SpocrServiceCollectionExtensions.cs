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

namespace SpocR.Extensions
{
    /// <summary>
    /// Erweiterungsmethoden für die Registrierung der SpocR-Dienste in der DI-Container
    /// </summary>
    public static class SpocrServiceCollectionExtensions
    {
        /// <summary>
        /// Fügt alle SpocR-Dienste zur Service Collection hinzu
        /// </summary>
        public static IServiceCollection AddSpocR(this IServiceCollection services)
        {
            // Konfiguration für SpocR
            services.AddOptions<SpocROptions>()
                .Configure(options =>
                {
                    options.GlobalConfigPath = GetGlobalConfigPath();
                    options.LocalConfigPath = Constants.ConfigurationFile;
                });

            // Core Services
            services.AddSingleton(PhysicalConsole.Singleton);
            services.TryAddSingleton<CommandOptions>();

            // Konsolen-Service mit verbesserten Logging-Fähigkeiten
            services.AddSingleton<IConsoleService>(provider =>
                new ConsoleService(
                    provider.GetRequiredService<IConsole>(),
                    provider.GetRequiredService<CommandOptions>()));

            // SpocR-Kerndienste
            services.AddSingleton<SpocrService>();

            // Manager-Dienste mit optimiertem Lebenszyklus
            AddManagerServices(services);

            // Dateiverwaltungs-Dienste
            AddFileManagers(services);

            // Code-Generierungs-Dienste
            AddCodeGenerators(services);

            return services;
        }

        /// <summary>
        /// Registriert die Manager-Dienste in der Service Collection
        /// </summary>
        private static void AddManagerServices(IServiceCollection services)
        {
            services.AddSingleton<OutputService>();
            services.AddSingleton<SchemaManager>();
            services.AddSingleton<SpocrManager>();
            services.AddSingleton<SpocrProjectManager>();
            services.AddSingleton<SpocrSchemaManager>();
            services.AddSingleton<SpocrStoredProcdureManager>();
            services.AddSingleton<SpocrConfigManager>();
        }

        /// <summary>
        /// Registriert die Dateiverwaltungs-Dienste in der Service Collection
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
        /// Registriert die Code-Generierungs-Dienste in der Service Collection
        /// </summary>
        private static void AddCodeGenerators(IServiceCollection services)
        {
            // Template- und Generatoren-Dienste
            services.AddSingleton<TemplateManager>();
            services.AddSingleton<InputGenerator>();
            services.AddSingleton<OutputGenerator>();
            services.AddSingleton<ModelGenerator>();
            services.AddSingleton<TableTypeGenerator>();
            services.AddSingleton<StoredProcedureGenerator>();

            // Orchestrator als letzte Komponente registrieren
            services.AddSingleton<CodeGenerationOrchestrator>();
        }

        /// <summary>
        /// Ermittelt den Pfad zur globalen Konfigurationsdatei
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
    /// Optionen für die SpocR-Konfiguration
    /// </summary>
    public class SpocROptions
    {
        public string GlobalConfigPath { get; set; }
        public string LocalConfigPath { get; set; }
    }
}
