using System;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpocR.Commands.Project;
using SpocR.Commands.Schema;
using SpocR.Commands.Spocr;
using SpocR.Commands.Snapshot;
using SpocR.DataContext;
using SpocR.Extensions;
using SpocR.AutoUpdater;
using System.IO;
using SpocR.Services;
using SpocR.Infrastructure;
using SpocRVNext.Configuration;

namespace SpocR;

[Command(Name = "spocr", UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.Throw)]
[Subcommand(typeof(InitCommand))]   // v5+ initialization (.env bootstrap)
[Subcommand(typeof(PullCommand))]
[Subcommand(typeof(BuildCommand))]
[Subcommand(typeof(RebuildCommand))]
[Subcommand(typeof(RemoveCommand))]
[Subcommand(typeof(VersionCommand))]
[Subcommand(typeof(ConfigCommand))]
[Subcommand(typeof(ProjectCommand))]
[Subcommand(typeof(SchemaCommand))]
[Subcommand(typeof(SnapshotCommand))]
[Subcommand(typeof(SpocR.Commands.Test.TestCommand))]
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
        var quiet = false;
        // Lightweight parse for --quiet / -q early to suppress banners
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (string.Equals(a, "--quiet", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-q", StringComparison.OrdinalIgnoreCase))
            {
                quiet = true;
                break;
            }
        }
        // Early sniff -p/--path to set SPOCR_CONFIG_PATH before EnvConfiguration is loaded
        try
        {
            string? cliConfig = null;
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (string.Equals(a, "-p", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "--path", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                    {
                        cliConfig = args[i + 1];
                    }
                }
                else if (a.StartsWith("-p=", StringComparison.OrdinalIgnoreCase))
                {
                    cliConfig = a.Substring(3);
                }
                else if (a.StartsWith("--path=", StringComparison.OrdinalIgnoreCase))
                {
                    cliConfig = a.Substring("--path=".Length);
                }
            }
            if (!string.IsNullOrWhiteSpace(cliConfig))
            {
                // Normalize to full path; if it's a file (spocr.json) keep it; if directory append spocr.json if present
                var full = Path.GetFullPath(cliConfig);
                if (Directory.Exists(full))
                {
                    var candidate = Path.Combine(full, "spocr.json");
                    if (File.Exists(candidate)) full = candidate; // prefer explicit file if exists
                }
                Environment.SetEnvironmentVariable("SPOCR_CONFIG_PATH", full);
            }
        }
        catch { /* non-fatal */ }

        // Experimental vNext CLI (System.CommandLine) short-circuit if enabled
        var experimentalExit = await ProgramVNextCLI.TryRunAsync(args);
        if (experimentalExit != -999)
        {
            return experimentalExit;
        }

        // Early lightweight .env parse to project root for update skip flags (SPOCR_NO_UPDATE / SPOCR_SKIP_UPDATE)
        try
        {
            void LoadSkipVarsFromEnv(string path)
            {
                if (!File.Exists(path)) return;
                foreach (var line in File.ReadAllLines(path))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                    var eq = trimmed.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = trimmed.Substring(0, eq).Trim();
                    if (key.Equals("SPOCR_NO_UPDATE", StringComparison.OrdinalIgnoreCase) || key.Equals("SPOCR_SKIP_UPDATE", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring(eq + 1).Trim();
                        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
                        {
                            Environment.SetEnvironmentVariable(key, val);
                        }
                    }
                }
            }
            var cwd = Directory.GetCurrentDirectory();
            LoadSkipVarsFromEnv(Path.Combine(cwd, ".env"));
            var cfgPath = Environment.GetEnvironmentVariable("SPOCR_CONFIG_PATH");
            if (!string.IsNullOrWhiteSpace(cfgPath))
            {
                try
                {
                    string dir = File.Exists(cfgPath) ? Path.GetDirectoryName(cfgPath)! : cfgPath;
                    LoadSkipVarsFromEnv(Path.Combine(dir, ".env"));
                }
                catch { /* ignore */ }
            }
        }
        catch { /* non-fatal */ }

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

        // Register EnvConfiguration (vNext config model – still coexists with legacy spocr.json consumers)
        try
        {
            var explicitCfg = Environment.GetEnvironmentVariable("SPOCR_CONFIG_PATH");
            var envCfg = EnvConfiguration.Load(explicitConfigPath: explicitCfg);
            services.AddSingleton(envCfg);
        }
        catch (Exception ex)
        {
            // Non-fatal: keep legacy path working even if env config incomplete
            Console.Error.WriteLine($"[spocr vNext config warning] {ex.Message}");
        }

        // Register SpocR services (legacy + shared)
        services.AddSpocR();
        services.AddDbContext();

        // vNext Template Engine (für DbContextGenerator Abhängigkeiten falls Flag aktiv)
        try
        {
            var templateRoot = Path.Combine(Directory.GetCurrentDirectory(), "src", "SpocRVNext", "Templates");
            if (Directory.Exists(templateRoot))
            {
                services.AddSingleton<SpocR.SpocRVNext.Engine.ITemplateRenderer, SpocR.SpocRVNext.Engine.SimpleTemplateEngine>();
                services.AddSingleton<SpocR.SpocRVNext.Engine.ITemplateLoader>(_ => new SpocR.SpocRVNext.Engine.FileSystemTemplateLoader(templateRoot));
            }
        }
        catch (Exception tex)
        {
            Console.Error.WriteLine($"[spocr templates warn] {tex.Message}");
        }

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

        // Explicit root handler to avoid reflection lookup issues (fallback help)
        app.OnExecute(() =>
        {
            if (!quiet)
            {
                Console.WriteLine("spocr - SpocR CLI");
                Console.WriteLine();
                Console.WriteLine("Usage: spocr <command> [options]");
                Console.WriteLine("Try 'spocr --help' or 'spocr <command> --help' for more information.");
            }
            return ExitCodes.Success;
        });

        await app.InitializeGlobalConfigAsync(serviceProvider);

        try
        {
            return await app.ExecuteAsync(args);
        }
        catch (Exception ex)
        {
            try
            {
                var console = serviceProvider.GetService<IConsoleService>();
                console?.Error($"Unhandled exception: {ex.Message}");
                if (ex.InnerException != null)
                    console?.Error($"Inner exception: {ex.InnerException.Message}");
                console?.Error(ex.StackTrace ?? "<no stacktrace>");
            }
            catch { }
            return ExitCodes.InternalError;
        }
    }

    // Root command fallback: show help if no subcommand provided (McMaster looks for parameterless OnExecute/OnExecuteAsync)
    public Task<int> OnExecuteAsync()
    {
        var args = Environment.GetCommandLineArgs();
        var hasQuiet = false;
        for (int i = 0; i < args.Length; i++) { var a = args[i]; if (a.Equals("--quiet", StringComparison.OrdinalIgnoreCase) || a.Equals("-q", StringComparison.OrdinalIgnoreCase)) { hasQuiet = true; break; } }
        if (!hasQuiet)
        {
            Console.WriteLine("spocr - SpocR CLI");
            Console.WriteLine();
            Console.WriteLine("Usage: spocr <command> [options]");
            Console.WriteLine("Try 'spocr --help' or 'spocr <command> --help' for more information.");
        }
        return Task.FromResult(ExitCodes.Success);
    }

    // Synchronous variant for McMaster default convention compatibility
    public int OnExecute()
    {
        var args2 = Environment.GetCommandLineArgs();
        var hasQuiet2 = false;
        for (int i = 0; i < args2.Length; i++) { var a = args2[i]; if (a.Equals("--quiet", StringComparison.OrdinalIgnoreCase) || a.Equals("-q", StringComparison.OrdinalIgnoreCase)) { hasQuiet2 = true; break; } }
        if (!hasQuiet2)
        {
            Console.WriteLine("spocr - SpocR CLI");
            Console.WriteLine();
            Console.WriteLine("Usage: spocr <command> [options]");
            Console.WriteLine("Try 'spocr --help' or 'spocr <command> --help' for more information.");
        }
        return ExitCodes.Success;
    }

    static Task<int> Main(string[] args) => RunCliAsync(args);
}
