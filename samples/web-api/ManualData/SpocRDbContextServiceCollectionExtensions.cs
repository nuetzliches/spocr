// ---------------------------------------------------------------------------------------------------------------
// This is a manual DI extension for the temporary sample DbContext (see SpocRDbContext.cs).
// It is intentionally simple and will be removed once the generated modern context provides equivalent services.
// ---------------------------------------------------------------------------------------------------------------

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpocR.Samples.WebApi.ManualData;

namespace SpocR.Samples.WebApi.ManualData;

public static class SpocRDbContextServiceCollectionExtensions
{
    public static IServiceCollection AddSpocRDbContext(this IServiceCollection services, Action<SpocRDbContextOptions>? configure = null)
    {
        var options = new SpocRDbContextOptions();
        services.AddSingleton(options); // Simplified for the sample; lifetime may change when replaced by generated context
        configure?.Invoke(options);
        services.TryAddScoped<ISpocRDbContext, SpocRDbContext>();
        services.AddScoped<SpocRDbContext>();
        return services;
    }
}
