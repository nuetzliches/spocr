using System.IO;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
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
            services.AddSingleton<OutputService>();
            services.AddSingleton<SchemaManager>();
            services.AddSingleton<SpocrManager>();
            services.AddSingleton<SpocrConfigManager>();
            services.AddSingleton<FileManager<GlobalConfigurationModel>>(new FileManager<GlobalConfigurationModel>(globalConfigurationFileName));
            services.AddSingleton<FileManager<ConfigurationModel>>(new FileManager<ConfigurationModel>(Configuration.ConfigurationFile, spocrService.GetDefaultConfiguration()));
            services.AddSingleton<Generator>();
            services.AddSingleton<IReportService>(new ReportService(new ColoredConsoleReporter(PhysicalConsole.Singleton, true, false)));

            return services;
        }
    }
}