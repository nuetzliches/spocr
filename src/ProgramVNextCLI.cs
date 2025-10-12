using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SpocR.SpocRVNext;
using SpocRVNext.Configuration;
using SpocR.Telemetry;

#nullable enable

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

        var demoCommand = new Command("generate-demo", "Run a demo template render using the vNext generator")
        {
            modeOption
        };
        demoCommand.SetHandler(async (string? mode) =>
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
            var cfg = EnvConfiguration.Load(cliOverrides: envOverrides);
            resolvedMode = cfg.GeneratorMode;
            var renderer = provider.GetRequiredService<SpocR.SpocRVNext.Engine.ITemplateRenderer>();
            var gen = new SpocRGenerator(renderer);
            var output = gen.RenderDemo();
            Console.WriteLine($"[mode={cfg.GeneratorMode}] {output}");
            success = true;
            telemetry.Record(new ExperimentalCliUsageEvent(
                command: "generate-demo",
                mode: resolvedMode,
                duration: DateTime.UtcNow - start,
                success: success));
        }, modeOption);

        root.Add(demoCommand);

        var builder = new CommandLineBuilder(root)
            .UseDefaults();

        var parser = builder.Build();
        return await parser.InvokeAsync(args);
    }

    private static bool IsEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("SPOCR_EXPERIMENTAL_CLI"), "1", StringComparison.Ordinal);

    // HostBinder removed (no longer needed after simplifying DI approach)
}