using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SpocR.TestFramework;

/// <summary>
/// Base class for SpocR tests providing common setup and utilities
/// </summary>
public abstract class SpocrTestBase : IDisposable
{
    protected IServiceProvider ServiceProvider { get; private set; }
    protected IConfiguration Configuration { get; private set; }
    protected ILogger Logger { get; private set; }

    protected SpocrTestBase()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();
        
        Configuration = ServiceProvider.GetRequiredService<IConfiguration>();
        Logger = ServiceProvider.GetRequiredService<ILogger<SpocrTestBase>>();
    }

    protected virtual void ConfigureServices(IServiceCollection services)
    {
        // Add basic configuration
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(GetDefaultConfiguration())
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        
        // Add SpocR services
        services.AddSpocR();
    }

    protected virtual Dictionary<string, string?> GetDefaultConfiguration()
    {
        return new Dictionary<string, string?>
        {
            ["TestMode"] = "true",
            ["Environment"] = "Test"
        };
    }

    protected T GetService<T>() where T : notnull
        => ServiceProvider.GetRequiredService<T>();

    protected T? GetOptionalService<T>() where T : class
        => ServiceProvider.GetService<T>();

    public virtual void Dispose()
    {
        if (ServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}