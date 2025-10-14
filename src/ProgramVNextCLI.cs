using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SpocR.SpocRVNext;
using SpocRVNext.Configuration;
using SpocR.Telemetry;

namespace SpocR;

/// <summary>
/// Experimental vNext CLI bootstrap using System.CommandLine.
/// Enabled when environment variable SPOCR_EXPERIMENTAL_CLI=1.
/// Co-exists with legacy McMaster-based Program for gradual migration.
/// </summary>
internal static class ProgramVNextCLI
{
    public static async Task<int> TryRunAsync(string[] args)
    {
        if (!IsEnabled()) return -999; // signal not executed

        var root = new RootCommand("SpocR vNext experimental CLI")
        {
            TreatUnmatchedTokensAsErrors = true
        };

        var modeOption = new Option<string>(
            name: "--mode",
            description: "Generator mode (legacy|dual|next). Overrides SPOCR_GENERATOR_MODE if supplied.");
        var pathOption = new Option<string>(
            name: "--path",
            description: "Execution / project path to operate on (for init-env / namespace derivation). Defaults to current directory.");
        var forceOption = new Option<bool>(
            name: "--force",
            description: "Force overwrite existing target file (for init-env)");

        var demoCommand = new Command("generate-demo", "Run a demo template render using the vNext generator")
        {
            modeOption
        };
        demoCommand.SetHandler((string? mode, string? path) =>
            {
                // Build lightweight service provider per invocation (cheap here; can be cached if expanded)
                var services = new ServiceCollection();
                services.AddSingleton<SpocR.SpocRVNext.Engine.ITemplateRenderer, SpocR.SpocRVNext.Engine.SimpleTemplateEngine>();
                services.AddSingleton<IExperimentalCliTelemetry, ConsoleExperimentalCliTelemetry>();
                using var provider = services.BuildServiceProvider();
                var telemetry = provider.GetRequiredService<IExperimentalCliTelemetry>();
                var start = DateTime.UtcNow;
                bool success = false;
                string resolvedMode = "";
                var envOverrides = new System.Collections.Generic.Dictionary<string, string?>();
                if (!string.IsNullOrWhiteSpace(mode)) envOverrides["SPOCR_GENERATOR_MODE"] = mode;
                string? pr = null;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    pr = System.IO.Path.GetFullPath(path);
                }
                var cfg = EnvConfiguration.Load(projectRoot: pr, cliOverrides: envOverrides, explicitConfigPath: path);
                resolvedMode = cfg.GeneratorMode;
                var renderer = provider.GetRequiredService<SpocR.SpocRVNext.Engine.ITemplateRenderer>();
                var gen = new SpocRGenerator(renderer, schemaProviderFactory: () => new SpocR.SpocRVNext.Metadata.SchemaMetadataProvider());
                var output = gen.RenderDemo();
                Console.WriteLine($"[mode={cfg.GeneratorMode}] {output}");
                success = true;
                telemetry.Record(new ExperimentalCliUsageEvent(
                    command: "generate-demo",
                    mode: resolvedMode,
                    duration: DateTime.UtcNow - start,
                    success: success));
            }, modeOption, pathOption);

        root.Add(demoCommand);

        var generateNextCommand = new Command("generate-next", "Generate demo outputs (legacy/next) and hash manifest (experimental)")
        {
            modeOption
        };
        generateNextCommand.SetHandler((string? mode, string? path) =>
        {
            var services = new ServiceCollection();
            services.AddSingleton<SpocR.SpocRVNext.Engine.ITemplateRenderer, SpocR.SpocRVNext.Engine.SimpleTemplateEngine>();
            using var provider = services.BuildServiceProvider();
            var envOverrides = new System.Collections.Generic.Dictionary<string, string?>();
            if (!string.IsNullOrWhiteSpace(mode)) envOverrides["SPOCR_GENERATOR_MODE"] = mode;
            string? pr = null;
            if (!string.IsNullOrWhiteSpace(path)) pr = System.IO.Path.GetFullPath(path);
            var cfg = EnvConfiguration.Load(projectRoot: pr, cliOverrides: envOverrides, explicitConfigPath: path);
            var dispatcher = new SpocR.SpocRVNext.DualGenerationDispatcher(cfg, provider.GetRequiredService<SpocR.SpocRVNext.Engine.ITemplateRenderer>());
            var message = dispatcher.ExecuteDemo();
            Console.WriteLine(message);
            Console.WriteLine("Hash manifest (if next output) written under debug/codegen-demo/next/manifest.hash.json");
        }, modeOption, pathOption);
        root.Add(generateNextCommand);

        // init-env command
        var initEnv = new Command("init-env", "Create or update a .env in the target path (non-interactive unless --no-auto).")
        {
            pathOption,
            forceOption,
            modeOption
        };
        initEnv.SetHandler(async (string? path, bool force, string? mode) =>
        {
            var target = string.IsNullOrWhiteSpace(path) ? System.IO.Directory.GetCurrentDirectory() : System.IO.Path.GetFullPath(path!);
            var desiredMode = string.IsNullOrWhiteSpace(mode) ? (Environment.GetEnvironmentVariable("SPOCR_GENERATOR_MODE") ?? "dual") : mode!;
            var envPath = await SpocR.SpocRVNext.Cli.EnvBootstrapper.EnsureEnvAsync(target, desiredMode, autoApprove: true, force: force);
            Console.WriteLine($"Initialized .env at: {envPath}");
        }, pathOption, forceOption, modeOption);
        root.Add(initEnv);

        var builder = new CommandLineBuilder(root)
            .UseDefaults();

        var parser = builder.Build();
        return await parser.InvokeAsync(args);
    }

    private static bool IsEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("SPOCR_EXPERIMENTAL_CLI"), "1", StringComparison.Ordinal);

    // HostBinder removed (no longer needed after simplifying DI approach)
}