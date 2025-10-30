using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpocR.SpocRVNext.Cli;
using SpocR.SpocRVNext.Extensions;
using SpocR.SpocRVNext.Infrastructure;
using SpocR.SpocRVNext.Runtime;
using SpocR.Services;
using SpocR.SpocRVNext.Data;
using SpocR.Utils;
using SpocRVNext.Configuration;

namespace SpocR;

public static class Program
{
    public static async Task<int> RunCliAsync(string[] args)
    {
        var quiet = false;
        for (int i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (string.Equals(token, "--quiet", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "-q", StringComparison.OrdinalIgnoreCase))
            {
                quiet = true;
                break;
            }
        }

        try
        {
            string? cliConfig = null;
            for (int i = 0; i < args.Length; i++)
            {
                var current = args[i];
                if (string.Equals(current, "-p", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(current, "--path", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                    {
                        cliConfig = args[i + 1];
                    }
                }
                else if (current.StartsWith("-p=", StringComparison.OrdinalIgnoreCase))
                {
                    cliConfig = current.Substring(3);
                }
                else if (current.StartsWith("--path=", StringComparison.OrdinalIgnoreCase))
                {
                    cliConfig = current.Substring("--path=".Length);
                }
            }

            if (!string.IsNullOrWhiteSpace(cliConfig))
            {
                var (configHint, projectRoot) = NormalizeCliProjectHint(cliConfig);
                if (!string.IsNullOrWhiteSpace(configHint))
                {
                    Environment.SetEnvironmentVariable("SPOCR_CONFIG_PATH", configHint);
                }

                if (!string.IsNullOrWhiteSpace(projectRoot))
                {
                    Environment.SetEnvironmentVariable("SPOCR_PROJECT_ROOT", projectRoot);
                }
            }
        }
        catch
        {
        }

        try
        {
            static void LoadSkipVarsFromEnv(string path)
            {
                if (!File.Exists(path))
                {
                    return;
                }

                foreach (var rawLine in File.ReadAllLines(path))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith('#'))
                    {
                        continue;
                    }

                    var equalsIndex = line.IndexOf('=');
                    if (equalsIndex <= 0)
                    {
                        continue;
                    }

                    var key = line.Substring(0, equalsIndex).Trim();
                    if (!key.Equals("SPOCR_NO_UPDATE", StringComparison.OrdinalIgnoreCase) &&
                        !key.Equals("SPOCR_SKIP_UPDATE", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var value = line.Substring(equalsIndex + 1).Trim();
                    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
                    {
                        Environment.SetEnvironmentVariable(key, value);
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
                    string directory = File.Exists(cfgPath) ? Path.GetDirectoryName(cfgPath)! : cfgPath;
                    LoadSkipVarsFromEnv(Path.Combine(directory, ".env"));
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        string environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
                             Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ??
                             "Production";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        try
        {
            var explicitCfg = Environment.GetEnvironmentVariable("SPOCR_CONFIG_PATH");
            var envCfg = EnvConfiguration.Load(explicitConfigPath: explicitCfg);
            services.AddSingleton(envCfg);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[spocr config warn] {ex.Message}");
        }

        services.AddSpocR();
        services.AddDbContext();

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


        using var serviceProvider = services.BuildServiceProvider();
        var runtime = serviceProvider.GetRequiredService<SpocrCliRuntime>();
        var commandOptionsAccessor = serviceProvider.GetRequiredService<CommandOptions>();

        var pathOption = new Option<string?>("--path", "Path to the project root containing .env");
        pathOption.AddAlias("-p");

        var dryRunOption = new Option<bool>("--dry-run", "Run command without making any changes");
        dryRunOption.AddAlias("-d");

        var forceOption = new Option<bool>("--force", "Run command even if warnings were raised");
        forceOption.AddAlias("-f");

        var quietOption = new Option<bool>("--quiet", "Run without extra interaction");
        quietOption.AddAlias("-q");

        var verboseOption = new Option<bool>("--verbose", "Show additional diagnostic information");
        verboseOption.AddAlias("-v");

        var noVersionCheckOption = new Option<bool>("--no-version-check", "Ignore version mismatch between installation and config file");
        var noAutoUpdateOption = new Option<bool>("--no-auto-update", "Skip the auto update check");
        var debugOption = new Option<bool>("--debug", "Use debug environment settings");
        var noCacheOption = new Option<bool>("--no-cache", "Do not read or write the local procedure metadata cache");
        var procedureOption = new Option<string?>("--procedure", "Process only specific procedures (comma separated schema.name)");

        var root = new RootCommand("SpocR CLI (vNext)")
        {
            TreatUnmatchedTokensAsErrors = true
        };

        static CliCommandOptions BindOptions(ParseResult parseResult,
            Option<string?> path,
            Option<bool> dryRun,
            Option<bool> force,
            Option<bool> quiet,
            Option<bool> verbose,
            Option<bool> noVersionCheck,
            Option<bool> noAutoUpdate,
            Option<bool> debug,
            Option<bool> noCache,
            Option<string?> procedure)
        {
            return new CliCommandOptions
            {
                Path = parseResult.GetValueForOption(path)?.Trim(),
                DryRun = parseResult.GetValueForOption(dryRun),
                Force = parseResult.GetValueForOption(force),
                Quiet = parseResult.GetValueForOption(quiet),
                Verbose = parseResult.GetValueForOption(verbose),
                NoVersionCheck = parseResult.GetValueForOption(noVersionCheck),
                NoAutoUpdate = parseResult.GetValueForOption(noAutoUpdate),
                Debug = parseResult.GetValueForOption(debug),
                NoCache = parseResult.GetValueForOption(noCache),
                Procedure = parseResult.GetValueForOption(procedure)?.Trim()
            };
        }

        static void PrepareCommandEnvironment(CliCommandOptions options)
        {
            DirectoryUtils.SetBasePath(options.Path);
            CacheControl.ForceReload = options.NoCache;

            if (!string.IsNullOrWhiteSpace(options.Procedure))
            {
                Environment.SetEnvironmentVariable("SPOCR_BUILD_PROCEDURES", options.Procedure);
            }
        }

        void AddCommonOptions(Command command)
        {
            command.AddOption(pathOption);
            command.AddOption(dryRunOption);
            command.AddOption(forceOption);
            command.AddOption(quietOption);
            command.AddOption(verboseOption);
            command.AddOption(noVersionCheckOption);
            command.AddOption(noAutoUpdateOption);
            command.AddOption(debugOption);
            command.AddOption(noCacheOption);
            command.AddOption(procedureOption);
        }

        var pullCommand = new Command("pull", "Pull database metadata into .spocr snapshots using .env settings");
        AddCommonOptions(pullCommand);
        pullCommand.SetHandler(async context =>
        {
            var options = BindOptions(context.ParseResult, pathOption, dryRunOption, forceOption, quietOption, verboseOption, noVersionCheckOption, noAutoUpdateOption, debugOption, noCacheOption, procedureOption);
            PrepareCommandEnvironment(options);
            commandOptionsAccessor.Update(options);
            var result = await runtime.PullAsync(options).ConfigureAwait(false);
            context.ExitCode = CommandResultMapper.Map(result);
        });
        root.AddCommand(pullCommand);

        var buildCommand = new Command("build", "Generate vNext client code from current snapshots using .env configuration");
        AddCommonOptions(buildCommand);
        buildCommand.SetHandler(async context =>
        {
            var options = BindOptions(context.ParseResult, pathOption, dryRunOption, forceOption, quietOption, verboseOption, noVersionCheckOption, noAutoUpdateOption, debugOption, noCacheOption, procedureOption);
            PrepareCommandEnvironment(options);
            commandOptionsAccessor.Update(options);
            var result = await runtime.BuildAsync(options).ConfigureAwait(false);
            context.ExitCode = CommandResultMapper.Map(result);
        });
        root.AddCommand(buildCommand);

        var rebuildCommand = new Command("rebuild", "Run pull and build sequentially using .env configuration");
        AddCommonOptions(rebuildCommand);
        rebuildCommand.SetHandler(async context =>
        {
            var options = BindOptions(context.ParseResult, pathOption, dryRunOption, forceOption, quietOption, verboseOption, noVersionCheckOption, noAutoUpdateOption, debugOption, noCacheOption, procedureOption);
            PrepareCommandEnvironment(options);
            commandOptionsAccessor.Update(options);

            var pullResult = await runtime.PullAsync(options).ConfigureAwait(false);
            if (CommandResultMapper.Map(pullResult) != ExitCodes.Success)
            {
                context.ExitCode = CommandResultMapper.Map(pullResult);
                return;
            }

            var buildResult = await runtime.BuildAsync(options).ConfigureAwait(false);
            context.ExitCode = CommandResultMapper.Map(buildResult);
        });
        root.AddCommand(rebuildCommand);

        var versionCommand = new Command("version", "Show installed and latest SpocR versions");
        versionCommand.AddOption(quietOption);
        versionCommand.AddOption(verboseOption);
        versionCommand.AddOption(noAutoUpdateOption);
        versionCommand.SetHandler(async context =>
        {
            var options = new CliCommandOptions
            {
                Quiet = context.ParseResult.GetValueForOption(quietOption),
                Verbose = context.ParseResult.GetValueForOption(verboseOption),
                NoAutoUpdate = context.ParseResult.GetValueForOption(noAutoUpdateOption)
            };
            commandOptionsAccessor.Update(options);
            context.ExitCode = CommandResultMapper.Map(await runtime.GetVersionAsync().ConfigureAwait(false));
        });
        root.AddCommand(versionCommand);

        var initCommand = new Command("init", "Initialize SpocR project (.env bootstrap)");
        initCommand.AddOption(pathOption);
        initCommand.AddOption(forceOption);

        var namespaceOption = new Option<string?>("--namespace", "Root namespace (SPOCR_NAMESPACE)");
        namespaceOption.AddAlias("-n");
        initCommand.AddOption(namespaceOption);

        var connectionOption = new Option<string?>("--connection", "Metadata pull connection string (SPOCR_GENERATOR_DB)");
        connectionOption.AddAlias("-c");
        initCommand.AddOption(connectionOption);

        var schemasOption = new Option<string?>("--schemas", "Comma separated allow-list (SPOCR_BUILD_SCHEMAS)");
        schemasOption.AddAlias("-s");
        initCommand.AddOption(schemasOption);

        initCommand.SetHandler(async context =>
        {
            var targetPath = context.ParseResult.GetValueForOption(pathOption)?.Trim();
            var force = context.ParseResult.GetValueForOption(forceOption);
            var nsValue = context.ParseResult.GetValueForOption(namespaceOption)?.Trim();
            var connection = context.ParseResult.GetValueForOption(connectionOption)?.Trim();
            var schemas = context.ParseResult.GetValueForOption(schemasOption)?.Trim();

            var effectivePath = string.IsNullOrWhiteSpace(targetPath) ? Directory.GetCurrentDirectory() : targetPath;
            var resolved = DirectoryUtils.IsPath(effectivePath) ? effectivePath : Path.GetFullPath(effectivePath);
            Directory.CreateDirectory(resolved);

            var envPath = await SpocR.SpocRVNext.Cli.EnvBootstrapper.EnsureEnvAsync(resolved, autoApprove: true, force: force).ConfigureAwait(false);

            try
            {
                var lines = File.ReadAllLines(envPath);

                static string NormalizeKey(string key) => key.Trim().ToUpperInvariant();

                void Upsert(string key, string value)
                {
                    var normalized = NormalizeKey(key);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        if (line.TrimStart().StartsWith(normalized + "=", StringComparison.OrdinalIgnoreCase))
                        {
                            lines[i] = normalized + "=" + value;
                            return;
                        }
                    }

                    Array.Resize(ref lines, lines.Length + 1);
                    lines[^1] = normalized + "=" + value;
                }

                if (!string.IsNullOrWhiteSpace(nsValue))
                {
                    Upsert("SPOCR_NAMESPACE", nsValue);
                }

                if (!string.IsNullOrWhiteSpace(connection))
                {
                    Upsert("SPOCR_GENERATOR_DB", connection);
                }

                if (!string.IsNullOrWhiteSpace(schemas))
                {
                    var normalizedSchemas = string.Join(',', schemas.Split(',').Select(static s => s.Trim()).Where(static s => s.Length > 0));
                    if (!string.IsNullOrWhiteSpace(normalizedSchemas))
                    {
                        Upsert("SPOCR_BUILD_SCHEMAS", normalizedSchemas);
                    }
                }

                File.WriteAllLines(envPath, lines);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[spocr init warn] post-processing .env failed: {ex.Message}");
            }

            Console.WriteLine($"[spocr init] .env ready at {envPath}");
            Console.WriteLine("JSON helpers ship enabled by default; no preview flags required.");
            Console.WriteLine("Next: run 'spocr pull' then 'spocr build' (or 'spocr rebuild').");
            context.ExitCode = ExitCodes.Success;
        });
        root.AddCommand(initCommand);

        foreach (var experimentalCommand in ProgramVNextCLI.BuildExperimentalCommands())
        {
            root.AddCommand(experimentalCommand);
        }

        if (!quiet)
        {
            root.Description += "\nSet SPOCR_EXPERIMENTAL_CLI=1 to enable additional experimental commands.";
        }

        return await root.InvokeAsync(args).ConfigureAwait(false);
    }

    private static (string configPath, string? projectRoot) NormalizeCliProjectHint(string rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return (string.Empty, null);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(rawInput.Trim());
        }
        catch
        {
            fullPath = rawInput.Trim();
        }

        static bool IsEnvFile(string value) => value.EndsWith(".env", StringComparison.OrdinalIgnoreCase) || value.EndsWith(".env.local", StringComparison.OrdinalIgnoreCase);
        static bool IsLegacyConfig(string value) => value.EndsWith(Constants.ConfigurationFile, StringComparison.OrdinalIgnoreCase);

        if (Directory.Exists(fullPath))
        {
            return (fullPath, fullPath);
        }

        if (File.Exists(fullPath))
        {
            var fileName = Path.GetFileName(fullPath);
            if (IsEnvFile(fileName))
            {
                var root = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
                return (fullPath, root);
            }

            if (IsLegacyConfig(fileName))
            {
                var root = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
                var envPath = Path.Combine(root, ".env");
                return (envPath, root);
            }

            var fallbackRoot = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
            return (fallbackRoot, fallbackRoot);
        }

        if (IsEnvFile(fullPath))
        {
            var root = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
            return (fullPath, root);
        }

        if (IsLegacyConfig(fullPath))
        {
            var root = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
            var envPath = Path.Combine(root, ".env");
            return (envPath, root);
        }

        return (fullPath, fullPath);
    }

    public static Task<int> Main(string[] args) => RunCliAsync(args);
}

