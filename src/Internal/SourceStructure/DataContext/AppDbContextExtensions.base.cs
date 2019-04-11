using Microsoft.Extensions.DependencyInjection;
using System;

namespace Source.DataContext
{
    public static class AppDbContextExtensions
    {
        public static IServiceCollection AddAppDbContext(this IServiceCollection services, Action<AppDbContexOptions> configureOptions = null)
        {
            services.AddScoped<IAppDbContext, AppDbContext>();
            return services;
        }
    }

    public class AppDbContexOptions
    {

    }
}