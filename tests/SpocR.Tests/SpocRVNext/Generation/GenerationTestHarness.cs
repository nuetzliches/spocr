using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SpocR.SpocRVNext;
using SpocR.SpocRVNext.Engine;
using SpocR.SpocRVNext.Metadata;
using SpocRVNext.Configuration;

namespace SpocR.Tests.SpocRVNext.Generation;

internal static class GenerationTestHarness
{
    public sealed record RunResult(string Root, string OutputDir, IReadOnlyList<string> GeneratedFiles, string AggregateHash);

    public static RunResult RunFromSnapshotJson(string snapshotJson, string? explicitNamespace = null)
    {
        var root = Directory.CreateTempSubdirectory("spocr_vnext_" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(Path.Combine(root.FullName, ".env"), "SPOCR_GENERATOR_MODE=next\n" + (explicitNamespace != null ? $"SPOCR_NAMESPACE={explicitNamespace}\n" : string.Empty));
        var schemaDir = Path.Combine(root.FullName, ".spocr", "schema");
        Directory.CreateDirectory(schemaDir);
        File.WriteAllText(Path.Combine(schemaDir, "snapshot.json"), snapshotJson);

        var cfg = EnvConfiguration.Load(projectRoot: root.FullName);
        var renderer = new SimpleTemplateEngine();
        var gen = new SpocRGenerator(renderer, schemaProviderFactory: () => new SchemaMetadataProvider(root.FullName));
        gen.GenerateAll(cfg, root.FullName);
        var outDir = Path.Combine(root.FullName, "SpocR");
        if (!Directory.Exists(outDir) || Directory.GetFiles(outDir, "*.cs", SearchOption.AllDirectories).Length == 0)
        {
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "SpocRDbContext.cs"), "// seeded minimal\nnamespace " + (cfg.NamespaceRoot ?? "Seeded.SpocR") + ";\npublic class SpocRDbContext {}");
        }
        gen.GenerateAll(cfg, root.FullName);
        var files = Directory.Exists(outDir)
            ? Directory.GetFiles(outDir, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();
        var agg = HashFiles(files, outDir);
        return new RunResult(root.FullName, outDir, files, agg);
    }

    public static RunResult RunAgainstProject(string projectRoot)
    {
        var envFile = Path.Combine(projectRoot, ".env");
        if (!File.Exists(envFile)) File.WriteAllText(envFile, "SPOCR_GENERATOR_MODE=next\n");
        var cfg = EnvConfiguration.Load(projectRoot: projectRoot);
        var renderer = new SimpleTemplateEngine();
        var gen = new SpocRGenerator(renderer, schemaProviderFactory: () => new SchemaMetadataProvider(projectRoot));
        gen.GenerateAll(cfg, projectRoot);
        var outDir = Path.Combine(projectRoot, "SpocR");
        if (!Directory.Exists(outDir) || Directory.GetFiles(outDir, "*.cs", SearchOption.AllDirectories).Length == 0)
        {
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "SpocRDbContext.cs"), "// seeded minimal\nnamespace " + (cfg.NamespaceRoot ?? "Seeded.SpocR") + ";\npublic class SpocRDbContext {}");
        }
        gen.GenerateAll(cfg, projectRoot);
        var files = Directory.Exists(outDir)
            ? Directory.GetFiles(outDir, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();
        var agg = HashFiles(files, outDir);
        return new RunResult(projectRoot, outDir, files, agg);
    }

    private static string HashFiles(IEnumerable<string> files, string root)
    {
        using var sha = SHA256.Create();
        var sb = new StringBuilder();
        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            var content = File.ReadAllText(file).Replace("\r\n", "\n");
            sb.AppendLine(rel);
            sb.AppendLine(content);
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }
}
