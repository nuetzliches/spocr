using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SpocR.SpocRVNext;
using SpocRVNext.Configuration;
using SpocR.Telemetry;
using SpocR.SpocRVNext.GoldenHash; // golden hash commands

namespace SpocR;

/// <summary>
/// Experimental vNext CLI bootstrap using System.CommandLine.
/// Enabled when environment variable SPOCR_EXPERIMENTAL_CLI=1.
/// Co-exists with legacy McMaster-based Program for gradual migration.
/// </summary>
internal static class ProgramVNextCLI
{
    private static readonly bool Verbose = string.Equals(Environment.GetEnvironmentVariable("SPOCR_VERBOSE"), "1", StringComparison.Ordinal);
    public static IEnumerable<Command> BuildExperimentalCommands()
    {
        if (!IsEnabled()) yield break;

        var pathOption = new Option<string>(
            name: "--path",
            description: "Execution / project path to operate on (for init-env / namespace derivation). Defaults to current directory.");
        var forceOption = new Option<bool>(
            name: "--force",
            description: "Force overwrite existing target file (for init-env)");

        var demoCommand = new Command("generate-demo", "Run a demo template render using the vNext generator");
        demoCommand.SetHandler((string? path) =>
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
                string? pr = null;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    pr = System.IO.Path.GetFullPath(path);
                }
                var cfg = EnvConfiguration.Load(projectRoot: pr, explicitConfigPath: path);
                resolvedMode = cfg.GeneratorMode;
                var renderer = provider.GetRequiredService<SpocR.SpocRVNext.Engine.ITemplateRenderer>();
                var gen = new SpocRGenerator(renderer, schemaProviderFactory: () => new SpocR.SpocRVNext.Metadata.SchemaMetadataProvider());
                var output = gen.RenderDemo();
                if (Verbose) Console.WriteLine($"[mode={cfg.GeneratorMode}] {output}");
                success = true;
                telemetry.Record(new ExperimentalCliUsageEvent(
                    command: "generate-demo",
                    mode: resolvedMode,
                    duration: DateTime.UtcNow - start,
                    success: success));
            }, pathOption);

        yield return demoCommand;

        var generateNextCommand = new Command("generate-next", "Generate demo outputs (next-only) and hash manifest (experimental)");
        generateNextCommand.SetHandler((string? path) =>
        {
            var services = new ServiceCollection();
            services.AddSingleton<SpocR.SpocRVNext.Engine.ITemplateRenderer, SpocR.SpocRVNext.Engine.SimpleTemplateEngine>();
            using var provider = services.BuildServiceProvider();
            string? pr = null;
            if (!string.IsNullOrWhiteSpace(path)) pr = System.IO.Path.GetFullPath(path);
            var cfg = EnvConfiguration.Load(projectRoot: pr, explicitConfigPath: path);
            var dispatcher = new SpocR.SpocRVNext.DualGenerationDispatcher(cfg, provider.GetRequiredService<SpocR.SpocRVNext.Engine.ITemplateRenderer>());
            var message = dispatcher.ExecuteDemo();
            if (Verbose) Console.WriteLine(message);
            if (Verbose) Console.WriteLine("Hash manifest (if next output) written under debug/codegen-demo/next/manifest.hash.json");
        }, pathOption);
        yield return generateNextCommand;

        // golden-hash write command
        var writeGolden = new Command("write-golden", "Write (or overwrite) golden hash manifest for current vNext output under debug/golden-hash.json")
        {
            pathOption
        };
        writeGolden.SetHandler((string? path) =>
        {
            var targetRoot = string.IsNullOrWhiteSpace(path) ? System.IO.Directory.GetCurrentDirectory() : System.IO.Path.GetFullPath(path!);
            var exit = GoldenHashCommands.WriteGolden(targetRoot);
            if (Verbose) Console.WriteLine(exit.Message);
        }, pathOption);
        yield return writeGolden;

        // golden-hash verify command
        var verifyGolden = new Command("verify-golden", "Verify current vNext output against golden hash manifest (relaxed unless SPOCR_STRICT_DIFF=1 or SPOCR_STRICT_GOLDEN=1)")
        {
            pathOption
        };
        verifyGolden.SetHandler((string? path) =>
        {
            var targetRoot = string.IsNullOrWhiteSpace(path) ? System.IO.Directory.GetCurrentDirectory() : System.IO.Path.GetFullPath(path!);
            var result = GoldenHashCommands.VerifyGolden(targetRoot);
            if (Verbose) Console.WriteLine(result.Message);
            if (result.ExitCode != 0) Environment.ExitCode = result.ExitCode; // do not throw
        }, pathOption);
        yield return verifyGolden;

        // init-env command
        var initEnv = new Command("init-env", "Create or update a .env in the target path (non-interactive unless --no-auto).")
        {
            pathOption,
            forceOption
        };
        initEnv.SetHandler(async (string? path, bool force) =>
        {
            var target = string.IsNullOrWhiteSpace(path) ? System.IO.Directory.GetCurrentDirectory() : System.IO.Path.GetFullPath(path!);
            var envPath = await SpocR.SpocRVNext.Cli.EnvBootstrapper.EnsureEnvAsync(target, autoApprove: true, force: force);
            if (Verbose) Console.WriteLine($"Initialized .env at: {envPath}");
        }, pathOption, forceOption);
        yield return initEnv;
    }

    private static bool IsEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("SPOCR_EXPERIMENTAL_CLI"), "1", StringComparison.Ordinal);

    // HostBinder removed (no longer needed after simplifying DI approach)
}