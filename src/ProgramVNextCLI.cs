using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SpocR.SpocRVNext;
using SpocR.SpocRVNext.Engine;
using SpocR.SpocRVNext.Generators;
using SpocR.SpocRVNext.GoldenHash; // golden hash commands
using SpocR.SpocRVNext.Telemetry;
using SpocR.SpocRVNext.Utils;
using SpocRVNext.Configuration;
using SpocRVNext.Metadata;

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
            string? projectRoot = null;
            if (!string.IsNullOrWhiteSpace(path))
            {
                try { projectRoot = Path.GetFullPath(path); }
                catch { projectRoot = path; }
            }

            var cfg = EnvConfiguration.Load(projectRoot: projectRoot, explicitConfigPath: path);
            var runner = new NextOnlyDemoRunner(cfg, projectRoot);
            var message = runner.Execute();
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

    private sealed class NextOnlyDemoRunner
    {
        private readonly EnvConfiguration _cfg;
        private readonly string _projectRoot;
        private readonly ITemplateRenderer _renderer;
        private readonly ITemplateLoader? _loader;
        private readonly string? _templatesRoot;

        public NextOnlyDemoRunner(EnvConfiguration cfg, string? explicitProjectRoot)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _renderer = new SimpleTemplateEngine();
            _projectRoot = ResolveProjectRoot(explicitProjectRoot);
            (_templatesRoot, _loader) = ResolveTemplates();
        }

        public string Execute(string? baseOutputDir = null)
        {
            baseOutputDir ??= Path.Combine(_projectRoot, "debug", "codegen-demo");
            Directory.CreateDirectory(baseOutputDir);
            var nextDir = Path.Combine(baseOutputDir, "next");
            Directory.CreateDirectory(nextDir);

            ApplyTemplateCacheState();

            var generator = new SpocRGenerator(_renderer, _loader, schemaProviderFactory: () => new SpocR.SpocRVNext.Metadata.SchemaMetadataProvider(_projectRoot));
            var demoContent = generator.RenderDemo();
            File.WriteAllText(Path.Combine(nextDir, "DemoNext.cs"), demoContent);

            var generatedDir = Path.Combine(nextDir, "generated");
            generator.GenerateMinimalDbContext(generatedDir);

            var tableTypesGenerator = new TableTypesGenerator(_cfg, new TableTypeMetadataProvider(_projectRoot), _renderer, _loader, _projectRoot);
            var tableTypeCount = tableTypesGenerator.Generate();
            File.WriteAllText(Path.Combine(nextDir, "_tabletypes.info"), $"Generiert {tableTypeCount} TableType-Record-Structs");

            var manifest = DirectoryHasher.HashDirectory(nextDir);
            DirectoryHasher.WriteManifest(manifest, Path.Combine(nextDir, "manifest.hash.json"));

            return $"[next-only demo] generator ran in mode={_cfg.GeneratorMode}; table-types={tableTypeCount}; manifest hash={manifest.AggregateSha256}";
        }

        private static string ResolveProjectRoot(string? explicitProjectRoot)
        {
            if (!string.IsNullOrWhiteSpace(explicitProjectRoot))
            {
                try { return Path.GetFullPath(explicitProjectRoot); }
                catch { return explicitProjectRoot!; }
            }

            return ProjectRootResolver.ResolveCurrent();
        }

        private static (string? templatesRoot, ITemplateLoader? loader) ResolveTemplates()
        {
            try
            {
                var solutionRoot = ProjectRootResolver.GetSolutionRootOrCwd();
                var templatesDir = Path.Combine(solutionRoot, "src", "SpocRVNext", "Templates");
                if (Directory.Exists(templatesDir))
                {
                    return (templatesDir, new FileSystemTemplateLoader(templatesDir));
                }
            }
            catch
            {
                // ignore loader resolution failures; generation will fallback where possible
            }

            return (null, null);
        }

        private void ApplyTemplateCacheState()
        {
            if (_templatesRoot is null)
            {
                return;
            }

            try
            {
                var manifest = DirectoryHasher.HashDirectory(_templatesRoot, path => path.EndsWith(".spt", StringComparison.OrdinalIgnoreCase));
                var cacheDir = Path.Combine(_projectRoot, ".spocr", "cache");
                Directory.CreateDirectory(cacheDir);
                var cacheFile = Path.Combine(cacheDir, "cache-state.json");

                CacheState? previous = null;
                if (File.Exists(cacheFile))
                {
                    try { previous = JsonSerializer.Deserialize<CacheState>(File.ReadAllText(cacheFile)); }
                    catch { /* ignore corrupt cache */ }
                }

                var currentVersion = typeof(SpocRGenerator).Assembly.GetName().Version?.ToString() ?? "4.5-bridge";
                var state = new CacheState
                {
                    TemplatesHash = manifest.AggregateSha256,
                    GeneratorVersion = currentVersion,
                    LastWriteUtc = DateTime.UtcNow
                };

                File.WriteAllText(cacheFile, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));

                bool changed = previous == null || !string.Equals(previous.TemplatesHash, state.TemplatesHash, StringComparison.Ordinal) || !string.Equals(previous.GeneratorVersion, state.GeneratorVersion, StringComparison.Ordinal);
                if (changed)
                {
                    CacheControl.ForceReload = true;
                    var reason = previous == null ? "initialization" : (!string.Equals(previous.TemplatesHash, state.TemplatesHash, StringComparison.Ordinal) ? "hash-diff" : "version-change");
                    var shortHash = state.TemplatesHash.Length >= 8 ? state.TemplatesHash.Substring(0, 8) : state.TemplatesHash;
                    Console.Out.WriteLine($"[spocr vNext] Info: Template cache-state {reason}; hash={shortHash} → reload metadata. path={cacheFile}");
                }
                else
                {
                    var shortHash = state.TemplatesHash.Length >= 8 ? state.TemplatesHash.Substring(0, 8) : state.TemplatesHash;
                    Console.Out.WriteLine($"[spocr vNext] Info: Template cache-state unchanged (hash={shortHash}) path={cacheFile}.");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[spocr vNext] Warning: Failed template cache-state evaluation: {ex.Message}");
            }
        }

        private sealed class CacheState
        {
            public string TemplatesHash { get; set; } = string.Empty;
            public string GeneratorVersion { get; set; } = string.Empty;
            public DateTime LastWriteUtc { get; set; }
        }
    }
}