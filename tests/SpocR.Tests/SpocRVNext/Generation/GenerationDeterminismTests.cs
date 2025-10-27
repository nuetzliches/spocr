using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using SpocR.SpocRVNext;
using SpocR.SpocRVNext.Engine;
using SpocR.SpocRVNext.Metadata;
using SpocRVNext.Configuration;

namespace SpocR.Tests.SpocRVNext.Generation;

public class GenerationDeterminismTests
{
    [Fact]
    public void RepeatedRuns_AreDeterministic_ForProceduresAndInputs()
    {
        var root = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(root.FullName, ".env"), "SPOCR_GENERATOR_MODE=next\nSPOCR_NAMESPACE=Determinism.Sample\n");
        var schemaDir = Path.Combine(root.FullName, ".spocr", "schema");
        Directory.CreateDirectory(schemaDir);
    var snapshot = "{\n  \"Procedures\": [\n    {\n      \"Name\": \"CalcB\",\n      \"Schema\": \"dbo\",\n      \"Parameters\": [ { \"Name\": \"Y\", \"TypeRef\": \"sys.int\" }, { \"Name\": \"X\", \"TypeRef\": \"sys.int\", \"IsOutput\": true, \"IsNullable\": true } ],\n      \"ResultSets\": [ { \"Columns\": [ { \"Name\": \"Val\", \"TypeRef\": \"sys.int\", \"IsNullable\": true } ] } ]\n    },\n    {\n      \"Schema\": \"dbo\",\n      \"Name\": \"CalcA\",\n      \"ResultSets\": [ { \"Columns\": [ { \"Name\": \"Value\", \"TypeRef\": \"sys.int\" } ] } ],\n      \"Parameters\": []\n    }\n  ]\n}";
        File.WriteAllText(Path.Combine(schemaDir, "snap.json"), snapshot);
        var cfg = EnvConfiguration.Load(projectRoot: root.FullName);
        var renderer = new SimpleTemplateEngine();
        var gen = new SpocRGenerator(renderer, schemaProviderFactory: () => new SchemaMetadataProvider(root.FullName));
        gen.GenerateAll(cfg, root.FullName);
        var hash1 = HashOutput(Path.Combine(root.FullName, "SpocR"));
        gen.GenerateAll(cfg, root.FullName);
        var hash2 = HashOutput(Path.Combine(root.FullName, "SpocR"));
        Assert.Equal(hash1, hash2);
    }

    private static string HashOutput(string dir)
    {
        if (!Directory.Exists(dir)) return string.Empty;
        var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        using var sha = SHA256.Create();
        var sb = new StringBuilder();
        foreach (var file in files)
        {
            var content = File.ReadAllText(file).Replace("\r\n", "\n");
            sb.AppendLine(Path.GetRelativePath(dir, file));
            sb.AppendLine(content);
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }
}
