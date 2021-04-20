using Microsoft.Extensions.DependencyInjection;
using System;

namespace Source.DataContext
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAppDbContext(this IServiceCollection services, Action<AppDbContextOptions> options = null)
        {
            services.AddScoped<IAppDbContext, AppDbContext>();
            return services;
        }
    }

    public class AppDbContextOptions
    {

    }
}
