using Source.DataContext;
using System;

namespace Microsoft.Extensions.DependencyInjection
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