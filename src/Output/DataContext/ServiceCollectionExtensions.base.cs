using Microsoft.Extensions.DependencyInjection;
using System;

namespace Source.DataContext
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAppDbContext(this IServiceCollection services, Action<AppDbContextOptions> options = null)
        {
            if (options != null)
            {
                services.Configure(options);
            }

            services.AddScoped<IAppDbContext, AppDbContext>();
            return services;
        }
    }
}
