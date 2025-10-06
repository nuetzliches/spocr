using System;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpocR.Commands.Project;
using SpocR.Commands.Schema;
using SpocR.Commands.Spocr;
using SpocR.Commands.StoredProcedure;
using SpocR.Commands.Snapshot;
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
[Subcommand(typeof(StoredProcedureCommand))]
[Subcommand(typeof(SnapshotCommand))]
[Subcommand(typeof(SpocR.Commands.Test.TestCommand))]
[HelpOption("-?|-h|--help")]
public class Program
{
    static async Task<int> Main(string[] args)
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
        catch (Exception ex)
        {
            // Attempt to log via console service if available
            try
            {
                var console = serviceProvider.GetService<IConsoleService>();
                console?.Error($"Unhandled exception: {ex.Message}");
                if (ex.InnerException != null)
                {
                    console?.Error($"Inner exception: {ex.InnerException.Message}");
                }
            }
            catch
            {
                // Swallow any logging failures – we are already failing hard.
            }
            return ExitCodes.InternalError;
        }

        // Automatic update check on startup
        // var consoleService = serviceProvider.GetRequiredService<IConsoleService>();
        // var autoUpdater = serviceProvider.GetRequiredService<AutoUpdaterService>();

        // Display the current environment
        // consoleService.Verbose($"Current environment: {environment}");

        // try
        // {
        //     // Check for updates without blocking execution
        //     _ = Task.Run(async () =>
        //     {
        //         try
        //         {
        //             await autoUpdater.RunAsync();
        //         }
        //         catch (Exception ex)
        //         {
        //             // Update check failures should not affect the main execution
        //             consoleService.Warn($"Update check failed: {ex.Message}");
        //         }
        //     });

        //     app.OnExecute(() =>
        //     {
        //         app.ShowRootCommandFullNameAndVersion();
        //         app.ShowHelp();
        //         return 0;
        //     });

        //     // Execute the command line
        //     return await app.ExecuteAsync(args);
        // }
        // catch (Exception ex)
        // {
        //     consoleService.Error($"Unhandled exception: {ex.Message}");
        //     if (ex.InnerException != null)
        //     {
        //         consoleService.Error($"Inner exception: {ex.InnerException.Message}");
        //     }
        //     return 1; // Return error code
        // }
    }
}
