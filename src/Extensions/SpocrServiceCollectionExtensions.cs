using Microsoft.Extensions.DependencyInjection;
using SpocR.Internal.Common;
using SpocR.Internal.Managers;
using SpocR.Managers;
using SpocR.Services;

namespace SpocR.Extensions
{
    public static class SpocrServiceCollectionExtensions
    {
        public static IServiceCollection AddSpocR(this IServiceCollection services)
        {
            services.AddSingleton<SpocrService>();
            services.AddSingleton<SchemaManager>();
            services.AddSingleton<SpocrManager>();
            services.AddSingleton<ConfigFileManager>();
            services.AddSingleton<Engine>();
            return services;
        }
    }
}