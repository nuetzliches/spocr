using Microsoft.Extensions.DependencyInjection;
using SpocR.Managers;
using SpocR.Services;

namespace SpocR.Extensions
{
    public static class SpocrServiceCollectionExtensions
    {
        public static IServiceCollection AddSpocR(this IServiceCollection services)
        {
            services.AddSingleton<SpocrService>();
            services.AddSingleton<OutputService>();
            services.AddSingleton<SchemaManager>();
            services.AddSingleton<SpocrManager>();
            services.AddSingleton<ConfigFileManager>();
            services.AddSingleton<Generator>();
            return services;
        }
    }
}