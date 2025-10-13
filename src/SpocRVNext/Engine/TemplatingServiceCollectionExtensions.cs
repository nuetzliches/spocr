using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace SpocR.SpocRVNext.Engine;

/// <summary>
/// ServiceCollection extensions for SpocRVNext templating components.
/// </summary>
public static class TemplatingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the simple template engine and a file system loader.
    /// </summary>
    /// <param name="services">DI collection.</param>
    /// <param name="templateRoot">Directory containing *.spt template files.</param>
    public static IServiceCollection AddSpocRVNextTemplating(this IServiceCollection services, string templateRoot)
    {
        if (!Directory.Exists(templateRoot))
        {
            throw new DirectoryNotFoundException($"Template root not found: {templateRoot}");
        }
        services.AddSingleton<ITemplateRenderer, SimpleTemplateEngine>();
        services.AddSingleton<ITemplateLoader>(_ => new FileSystemTemplateLoader(templateRoot));
        return services;
    }
}
