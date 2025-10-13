using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using SpocR.SpocRVNext.Engine;
using SpocR.SpocRVNext.Utils;
using SpocRVNext.Configuration;
using SpocRVNext.Metadata;
using SpocR.SpocRVNext.Generators;

namespace SpocR.SpocRVNext;

/// <summary>
/// Placeholder service for future dual-generation orchestration.
/// Will later:
///  - Invoke legacy generator (existing pathways) and new generator side-by-side when mode=dual.
///  - Collect hashes & produce diff metrics.
///  - Respect allow-list for benign differences.
/// Currently only demonstrates mode branching.
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
        baseOutputDir ??= Path.Combine(Directory.GetCurrentDirectory(), "debug", "codegen-demo");
        Directory.CreateDirectory(baseOutputDir);
        var nextDir = Path.Combine(baseOutputDir, "next");
        var legacyDir = Path.Combine(baseOutputDir, "legacy");

        string? nextContent = null;
        string? legacyContent = null;

        switch (_cfg.GeneratorMode)
        {
            case "legacy":
                legacyContent = "// legacy demo placeholder";
                Directory.CreateDirectory(legacyDir);
                File.WriteAllText(Path.Combine(legacyDir, "DemoLegacy.cs"), legacyContent);
                break;
            case "next":
                {
                    // Instantiate template loader (filesystem) pointing to Templates folder
                    var templatesDir = Path.Combine(Directory.GetCurrentDirectory(), "src", "SpocRVNext", "Templates");
                    ITemplateLoader? loader = Directory.Exists(templatesDir)
                        ? new FileSystemTemplateLoader(templatesDir)
                        : null;
                    var genNext = new SpocRGenerator(_renderer, loader);
                    nextContent = genNext.RenderDemo();
                    Directory.CreateDirectory(nextDir);
                    File.WriteAllText(Path.Combine(nextDir, "DemoNext.cs"), nextContent);
                    genNext.GenerateMinimalDbContext(Path.Combine(nextDir, "generated"));
                    // vNext real output: TableTypes
                    var ttGen = new TableTypesGenerator(_cfg, new TableTypeMetadataProvider(Directory.GetCurrentDirectory()), _renderer, loader);
                    var count = ttGen.Generate();
                    File.WriteAllText(Path.Combine(nextDir, "_tabletypes.info"), $"Generated {count} table type record structs");
                }
                break;
            case "dual":
                legacyContent = "// legacy demo placeholder";
                var templatesDirDual = Path.Combine(Directory.GetCurrentDirectory(), "src", "SpocRVNext", "Templates");
                ITemplateLoader? loaderDual = Directory.Exists(templatesDirDual)
                    ? new FileSystemTemplateLoader(templatesDirDual)
                    : null;
                var gen = new SpocRGenerator(_renderer, loaderDual);
                nextContent = gen.RenderDemo();
                Directory.CreateDirectory(legacyDir);
                Directory.CreateDirectory(nextDir);
                File.WriteAllText(Path.Combine(legacyDir, "DemoLegacy.cs"), legacyContent);
                File.WriteAllText(Path.Combine(nextDir, "DemoNext.cs"), nextContent);
                gen.GenerateMinimalDbContext(Path.Combine(nextDir, "generated"));
                // vNext real output: TableTypes
                var ttGenDual = new TableTypesGenerator(_cfg, new TableTypeMetadataProvider(Directory.GetCurrentDirectory()), _renderer, loaderDual);
                var countDual = ttGenDual.Generate();
                File.WriteAllText(Path.Combine(nextDir, "_tabletypes.info"), $"Generated {countDual} table type record structs");
                // Diff step (allow-list reading optional future extension)
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
}