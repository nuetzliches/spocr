using System.IO;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using SpocR.AutoUpdater;
using SpocR.Commands;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;
using SpocR.Utils;

namespace SpocR.Extensions
{
    public static class SpocrServiceCollectionExtensions
    {
        public static IServiceCollection AddSpocR(this IServiceCollection services)
        {
#if DEBUG
            var globalConfigurationFileName = Path.Combine(DirectoryUtils.GetWorkingDirectory(), Constants.GlobalConfigurationFile);
#else
            var globalConfigurationFileName = Path.Combine(DirectoryUtils.GetAppDataDirectory(), Constants.GlobalConfigurationFile);
#endif

            var spocrService = new SpocrService();
            var commandOptions = new CommandOptions(null);

            services.AddSingleton<SpocrService>(spocrService);
            services.AddSingleton<IPackageManager, NugetService>();
            services.AddSingleton<AutoUpdaterService>();
            services.AddSingleton<OutputService>();
            services.AddSingleton<SchemaManager>();
            services.AddSingleton<SpocrManager>();
            services.AddSingleton<SpocrProjectManager>();
            services.AddSingleton<SpocrSchemaManager>();
            services.AddSingleton<SpocrStoredProcdureManager>();
            services.AddSingleton<SpocrConfigManager>();
            services.AddSingleton<FileManager<GlobalConfigurationModel>>(new FileManager<GlobalConfigurationModel>(spocrService, globalConfigurationFileName, spocrService.GetGlobalDefaultConfiguration()));
            services.AddSingleton<FileManager<ConfigurationModel>>(new FileManager<ConfigurationModel>(spocrService, Constants.ConfigurationFile, spocrService.GetDefaultConfiguration()));
            services.AddSingleton<Generator>();
            services.AddSingleton<IReportService>(new ReportService(new ColoredConsoleReporter(PhysicalConsole.Singleton, true, false), commandOptions));

            return services;
        }
    }
}