using System;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpocR.Commands.Project;
using SpocR.Commands.Schema;
using SpocR.Commands.Spocr;
using SpocR.Commands.StoredProcdure;
using SpocR.DataContext;
using SpocR.Extensions;
using SpocR.AutoUpdater;
using SpocR.Services;
using System.IO;

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
[Subcommand(typeof(StoredProcdureCommand))]
[HelpOption("-?|-h|--help")]
public class Program
{
    static async Task<int> Main(string[] args)
    {
        // Umgebung aus Umgebungsvariablen ermitteln
        string environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
                             Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ??
                             "Production";

        // Konfiguration mit den bestehenden Microsoft.Extensions.Configuration-APIs erstellen
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false)
            .Build();

        // ServiceCollection für Dependency Injection
        var services = new ServiceCollection();

        // Konfiguration als Service registrieren
        services.AddSingleton<IConfiguration>(configuration);

        // SpocR-Dienste registrieren
        services.AddSpocR();
        services.AddDbContext();

        // Auto-Update Dienste registrieren
        services.AddTransient<AutoUpdaterService>();
        services.AddTransient<IPackageManager, NugetService>();

        // ServiceProvider erstellen
        using var serviceProvider = services.BuildServiceProvider();

        // CommandLine-App mit Dependency Injection konfigurieren
        var app = new CommandLineApplication<Program>
        {
            Name = "spocr",
            Description = ".NET Core console for SpocR"
        };

        app.Conventions
           .UseDefaultConventions()
           .UseConstructorInjection(serviceProvider);

        app.InitializeGlobalConfig(serviceProvider);

        // Automatische Prüfung auf Updates beim Startup
        var consoleService = serviceProvider.GetRequiredService<IConsoleService>();
        var autoUpdater = serviceProvider.GetRequiredService<AutoUpdaterService>();

        // Aktuelle Umgebung anzeigen (optional)
        consoleService.Verbose($"Current environment: {environment}");

        try
        {
            // Prüfung auf Updates, aber nicht blockierend ausführen
            _ = Task.Run(async () =>
            {
                try
                {
                    await autoUpdater.RunAsync();
                }
                catch (Exception ex)
                {
                    // Fehler beim Update-Check sollten die Hauptfunktion nicht beeinträchtigen
                    consoleService.Warn($"Update check failed: {ex.Message}");
                }
            });

            app.OnExecute(() =>
            {
                app.ShowRootCommandFullNameAndVersion();
                app.ShowHelp();
                return 0;
            });

            // Command line ausführen
            return await app.ExecuteAsync(args);
        }
        catch (Exception ex)
        {
            consoleService.Error($"Unhandled exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                consoleService.Error($"Inner exception: {ex.InnerException.Message}");
            }
            return 1; // Fehlercode zurückgeben
        }
    }
}
