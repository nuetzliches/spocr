using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using SpocR.SpocRVNext.Engine;
using SpocR.SpocRVNext.Utils;
using SpocRVNext.Configuration;
using SpocRVNext.Metadata;
using SpocR.SpocRVNext.Generators;
using System.Text.Json;
using SpocR.Utils;

namespace SpocR.SpocRVNext;

/// <summary>
/// Übergangs-Dispatcher für gleichzeitige (dual) oder reine (next) vNext / Legacy-Demo-Generierung.
/// Unified project root resolution (previous sample-specific heuristic removed).
/// TODO: In Zukunft durch konsolidierte Pipeline ersetzen oder entfernen.
/// </summary>
public sealed class DualGenerationDispatcher
{
    private readonly EnvConfiguration _cfg;
    private readonly ITemplateRenderer _renderer;

    public DualGenerationDispatcher(EnvConfiguration cfg, ITemplateRenderer renderer)
    {
        _cfg = cfg;
        _renderer = renderer;
    }

    public string ExecuteDemo(string? baseOutputDir = null)
    {
        // Target directory (debug playground)
        baseOutputDir ??= Path.Combine(Directory.GetCurrentDirectory(), "debug", "codegen-demo");
        Directory.CreateDirectory(baseOutputDir);
        var nextDir = Path.Combine(baseOutputDir, "next");
        var legacyDir = Path.Combine(baseOutputDir, "legacy");

        string? nextContent = null;
        string? legacyContent = null;

        // Unified project root for this run (uses -p / environment through ProjectRootResolver)
        var projectRoot = ProjectRootResolver.ResolveCurrent();
        var solutionRoot = ProjectRootResolver.GetSolutionRootOrCwd();
        var templatesDir = Path.Combine(solutionRoot, "src", "SpocRVNext", "Templates");
        ITemplateLoader? loaderUnified = Directory.Exists(templatesDir) ? new FileSystemTemplateLoader(templatesDir) : null;

        // Template cache-state: detect changes in template files and trigger ForceReload once.
        TryApplyTemplateCacheState();

        switch (_cfg.GeneratorMode)
        {
            case "legacy":
                legacyContent = "// legacy demo placeholder";
                Directory.CreateDirectory(legacyDir);
                File.WriteAllText(Path.Combine(legacyDir, "DemoLegacy.cs"), legacyContent);
                break;
            case "next":
                {
                    // Reiner vNext-Durchlauf
                    var genNext = new SpocRGenerator(_renderer, loaderUnified, schemaProviderFactory: () => new SpocR.SpocRVNext.Metadata.SchemaMetadataProvider(projectRoot));
                    nextContent = genNext.RenderDemo();
                    Directory.CreateDirectory(nextDir);
                    File.WriteAllText(Path.Combine(nextDir, "DemoNext.cs"), nextContent);
                    genNext.GenerateMinimalDbContext(Path.Combine(nextDir, "generated")); // Demo
                    var ttGen = new TableTypesGenerator(_cfg, new TableTypeMetadataProvider(projectRoot), _renderer, loaderUnified);
                    var count = ttGen.Generate();
                    File.WriteAllText(Path.Combine(nextDir, "_tabletypes.info"), $"Generiert {count} TableType-Record-Structs");
                }
                break;
            case "dual":
                legacyContent = "// legacy demo placeholder";
                var gen = new SpocRGenerator(_renderer, loaderUnified, schemaProviderFactory: () => new SpocR.SpocRVNext.Metadata.SchemaMetadataProvider(projectRoot));
                nextContent = gen.RenderDemo();
                Directory.CreateDirectory(legacyDir);
                Directory.CreateDirectory(nextDir);
                File.WriteAllText(Path.Combine(legacyDir, "DemoLegacy.cs"), legacyContent);
                File.WriteAllText(Path.Combine(nextDir, "DemoNext.cs"), nextContent);
                // Full vNext generation in the project root
                var outDir = Path.Combine(projectRoot, "SpocR");
                Directory.CreateDirectory(outDir);
                gen.GenerateAll(_cfg, projectRoot);
                var ttGenDual = new TableTypesGenerator(_cfg, new TableTypeMetadataProvider(projectRoot), _renderer, loaderUnified);
                var countDual = ttGenDual.Generate();
                File.WriteAllText(Path.Combine(nextDir, "_tabletypes.info"), $"Generiert {countDual} TableType-Record-Structs");
                // Diff (Legacy vs Demo Next) – Demonstrationszweck
                var diff = DirectoryDiff.Compare(legacyDir, nextDir, allowListGlobs: ReadAllowList(baseOutputDir));
                var summaryPath = Path.Combine(baseOutputDir, "diff-summary.txt");
                File.WriteAllText(summaryPath, FormatDiff(diff));
                break;
            default:
                throw new InvalidOperationException($"Unknown mode '{_cfg.GeneratorMode}'");
        }

        // Hash next output (if present)
        if (Directory.Exists(nextDir))
        {
            var manifest = DirectoryHasher.HashDirectory(nextDir);
            DirectoryHasher.WriteManifest(manifest, Path.Combine(nextDir, "manifest.hash.json"));
        }

        return _cfg.GeneratorMode switch
        {
            "legacy" => "[legacy-only demo placeholder written]",
            "next" => nextContent ?? string.Empty,
            "dual" => $"[dual] legacy+next written; diff summary created; next={nextContent}",
            _ => ""
        };
    }

    private void TryApplyTemplateCacheState()
    {
        try
        {
            var solutionRoot = ProjectRootResolver.GetSolutionRootOrCwd();
            var projectRoot = ProjectRootResolver.ResolveCurrent();
            var templatesDir = Path.Combine(solutionRoot, "src", "SpocRVNext", "Templates");
            if (!Directory.Exists(templatesDir)) return; // nothing to hash

            var manifest = SpocR.SpocRVNext.Utils.DirectoryHasher.HashDirectory(templatesDir, p => p.EndsWith(".spt", StringComparison.OrdinalIgnoreCase));
            var templatesHash = manifest.AggregateSha256;

            // Use the existing sample/project .spocr cache folder (same location as snapshot cache files)
            var cacheDir = Path.Combine(projectRoot, ".spocr", "cache");
            Directory.CreateDirectory(cacheDir);
            var cacheFile = Path.Combine(cacheDir, "cache-state.json");
            CacheState? previous = null;
            if (File.Exists(cacheFile))
            {
                try
                {
                    var json = File.ReadAllText(cacheFile);
                    previous = JsonSerializer.Deserialize<CacheState>(json);
                }
                catch { /* ignore corrupt */ }
            }

            var currentVersion = GetGeneratorVersion();
            var state = new CacheState { TemplatesHash = templatesHash, GeneratorVersion = currentVersion, LastWriteUtc = DateTime.UtcNow };
            var outJson = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(cacheFile, outJson);

            bool changed = previous == null || previous.TemplatesHash != templatesHash || previous.GeneratorVersion != currentVersion;
            if (changed)
            {
                CacheControl.ForceReload = true; // one-time reload
                var reason = previous == null ? "initialization" : (previous.TemplatesHash != templatesHash ? "hash-diff" : "version-change");
                Console.Out.WriteLine($"[spocr vNext] Info: Template cache-state {reason}; hash={templatesHash.Substring(0, 8)} → reload metadata. path={cacheFile}");
            }
            else
            {
                Console.Out.WriteLine($"[spocr vNext] Info: Template cache-state unchanged (hash={templatesHash.Substring(0, 8)}) path={cacheFile}.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[spocr vNext] Warning: Failed template cache-state evaluation: {ex.Message}");
        }
    }

    private static string GetGeneratorVersion()
    {
        try
        {
            // Attempt to read version from SpocR assembly (fallback hard-coded bridge label)
            var asm = typeof(DualGenerationDispatcher).Assembly.GetName();
            return asm.Version?.ToString() ?? "4.5-bridge";
        }
        catch { return "4.5-bridge"; }
    }

    private sealed class CacheState
    {
        public string TemplatesHash { get; set; } = string.Empty;
        public string GeneratorVersion { get; set; } = string.Empty;
        public DateTime LastWriteUtc { get; set; }
    }

    private static IEnumerable<string>? ReadAllowList(string baseOutputDir)
    {
        var root = Directory.GetParent(Directory.GetParent(baseOutputDir!)!.FullName)!.FullName; // go up from debug/codegen-demo
        var file = Path.Combine(root, ".spocr-diff-allow");
        if (!File.Exists(file)) return null;
        return File.ReadAllLines(file)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();
    }

    private static string FormatDiff(DiffSummary diff)
    {
        return $"Added: {diff.Added.Count}\nRemoved: {diff.Removed.Count}\nChanged: {diff.Changed.Count}\nTotalLegacy: {diff.TotalLegacy}\nTotalNext: {diff.TotalNext}\n" +
               (diff.Added.Count > 0 ? "\n+ " + string.Join("\n+ ", diff.Added) : string.Empty) +
               (diff.Removed.Count > 0 ? "\n- " + string.Join("\n- ", diff.Removed) : string.Empty) +
               (diff.Changed.Count > 0 ? "\n~ " + string.Join("\n~ ", diff.Changed) : string.Empty);
    }

    // Former FindSampleProjectRoot heuristic removed: project root determined centrally via ProjectRootResolver.
}