using System.IO;
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
            services.AddSingleton<SpocrService>();
            services.AddSingleton<OutputService>();
            services.AddSingleton<SchemaManager>();
            services.AddSingleton<SpocrManager>();
            services.AddSingleton<SpocrConfigManager>();
            services.AddSingleton<FileManager<GlobalConfigurationModel>>(new FileManager<GlobalConfigurationModel>(globalConfigurationFileName));
            services.AddSingleton<FileManager<ConfigurationModel>>(new FileManager<ConfigurationModel>(Configuration.ConfigurationFile));
            services.AddSingleton<Generator>();
            services.AddSingleton<IReportService, ReportService>();

            return services;
        }
    }
}