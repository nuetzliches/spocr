using System.IO;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using SpocR.AutoUpdater;
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
            var globalConfigurationFileName = Path.Combine(DirectoryUtils.GetWorkingDirectory(), Configuration.GlobalConfigurationFile);
#else
            var globalConfigurationFileName = Path.Combine(DirectoryUtils.GetApplicationRoot(), Configuration.GlobalConfigurationFile);
#endif

            var spocrService = new SpocrService();

            services.AddSingleton<SpocrService>(spocrService);
            services.AddSingleton<IPackageManager, NugetService>();
            services.AddSingleton<AutoUpdaterService>();
            services.AddSingleton<OutputService>();
            services.AddSingleton<SchemaManager>();
            services.AddSingleton<SpocrManager>();
            services.AddSingleton<SpocrProjectManager>();
            services.AddSingleton<SpocrConfigManager>();
            services.AddSingleton<FileManager<GlobalConfigurationModel>>(new FileManager<GlobalConfigurationModel>(spocrService, globalConfigurationFileName, spocrService.GetGlobalDefaultConfiguration()));
            services.AddSingleton<FileManager<ConfigurationModel>>(new FileManager<ConfigurationModel>(spocrService, Configuration.ConfigurationFile, spocrService.GetDefaultConfiguration()));
            services.AddSingleton<Generator>();
            services.AddSingleton<IReportService>(new ReportService(new ColoredConsoleReporter(PhysicalConsole.Singleton, true, false)));

            return services;
        }
    }
}