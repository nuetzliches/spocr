using System;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpocR.Commands.Project;
using SpocR.Commands.Schema;
using SpocR.Commands.Spocr;
using SpocR.DataContext;
using SpocR.Extensions;
using SpocR.AutoUpdater;
using System.IO;
using SpocR.Services;
using SpocR.Infrastructure;

namespace SpocR;

[Command(UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.Throw)]
[Subcommand(typeof(CreateCommand))]
[Subcommand(typeof(PullCommand))]
[Subcommand(typeof(BuildCommand))]
[Subcommand(typeof(RebuildCommand))]
[Subcommand(typeof(RemoveCommand))]
[Subcommand(typeof(VersionCommand))]
[Subcommand(typeof(ConfigCommand))]
[Subcommand(typeof(ProjectCommand))]
[Subcommand(typeof(SchemaCommand))]
[HelpOption("-?|-h|--help")]
public class Program
{
    /// <summary>
    /// In-process entry point for the SpocR CLI used by the test suite and potential host integrations.
    /// Rationale:
    ///  - Allows meta / integration tests to invoke the CLI without spawning an external process (faster & fewer race conditions).
    ///  - Eliminates Windows file locking issues observed with repeated <c>dotnet run</c> / apphost executions (MSB3026/MSB3027 during rebuilds).
    ///  - Provides a single place to construct DI + command conventions while keeping <c>Main</c> minimal.
    ///  - Enables future programmatic embedding (e.g., other tools calling SpocR as a library) without reflection hacks.
    ///
    /// Notes for maintainers:
    ///  - Tests call this method directly; removing or changing the signature will break in-process meta tests.
    ///  - Keep side-effects (env var reads, working directory assumptions) confined here to mirror real CLI startup.
    ///  - If additional global setup is added, prefer extending this method rather than duplicating logic in tests.
    /// </summary>
    public static async Task<int> RunCliAsync(string[] args)
    {
        // Determine environment from environment variables
        string environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
                             Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ??
                             "Production";

        // Build configuration using the standard Microsoft.Extensions.Configuration APIs
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false)
            .Build();

        // Create the ServiceCollection for dependency injection
        var services = new ServiceCollection();

        // Register configuration as a service
        services.AddSingleton<IConfiguration>(configuration);

        // Register SpocR services
        services.AddSpocR();
        services.AddDbContext();

        // Register auto update services
        services.AddTransient<AutoUpdaterService>();
        services.AddTransient<IPackageManager, NugetService>();

        // Build the service provider
        using var serviceProvider = services.BuildServiceProvider();

        PrintDeprecationBanner(serviceProvider.GetService<IConsoleService>());

        // Configure the command line app with dependency injection
        var app = new CommandLineApplication<Program>
        {
            Name = "spocr",
            Description = ".NET Core console for SpocR"
        };

        app.Conventions
           .UseDefaultConventions()
           .UseConstructorInjection(serviceProvider);

        await app.InitializeGlobalConfigAsync(serviceProvider);

        try
        {
            return await app.ExecuteAsync(args);
        }
        catch (CliValidationException validationEx)
        {
            try
            {
                var console = serviceProvider.GetService<IConsoleService>();
                if (console is not null)
                {
                    console.Error(validationEx.Message);
                }
                else
                {
                    Console.Error.WriteLine(validationEx.Message);
                }
            }
            catch { }

            return ExitCodes.ValidationError;
        }
        catch (Exception ex)
        {
            try
            {
                var console = serviceProvider.GetService<IConsoleService>();
                console?.Error($"Unhandled exception: {ex.Message}");
                if (ex.InnerException != null)
                    console?.Error($"Inner exception: {ex.InnerException.Message}");
            }
            catch { }
            return ExitCodes.InternalError;
        }
    }

    private Task<int> OnExecuteAsync(CommandLineApplication app, IConsoleService consoleService)
    {
        consoleService.Warn("No command specified. Showing help...");
        app.ShowHelp();
        return Task.FromResult(ExitCodes.ValidationError);
    }

    private static readonly string[] DeprecationBannerLines = new[]
    {
        "[SpocR deprecated] This tool is no longer maintained.",
        "Please migrate to Xtraq: https://github.com/nuetzliches/xtraq",
        "Migration guide: https://nuetzliches.github.io/xtraq/getting-started/migrating-from-spocr"
    };

    private static void PrintDeprecationBanner(IConsoleService consoleService)
    {
        foreach (var line in DeprecationBannerLines)
        {
            if (consoleService is not null)
            {
                consoleService.Warn(line);
            }
            else
            {
                Console.WriteLine(line);
            }
        }
    }

    static Task<int> Main(string[] args) => RunCliAsync(args);
}
