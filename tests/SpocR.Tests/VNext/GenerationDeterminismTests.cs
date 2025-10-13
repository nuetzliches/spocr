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

namespace SpocR.Tests.VNext;

public class GenerationDeterminismTests
{
    [Fact]
    public void RepeatedRuns_AreDeterministic_ForProceduresAndInputs()
    {
        // Arrange temp root
        var root = Directory.CreateTempSubdirectory();
        var env = "SPOCR_GENERATOR_MODE=next\n"; // use default namespace root -> SpocR.Generated
        File.WriteAllText(Path.Combine(root.FullName, ".env"), env);
        var schemaDir = Path.Combine(root.FullName, ".spocr", "schema");
        Directory.CreateDirectory(schemaDir);
        // Intentionally shuffle property order and procedure order to test normalization
        var snapshot = "{\n  \"Procedures\": [\n    {\n      \"Name\": \"CalcB\",\n      \"Schema\": \"dbo\",\n      \"Inputs\": [ { \"Name\": \"@Y\", \"IsOutput\": false, \"SqlTypeName\": \"int\", \"IsNullable\": false }, { \"Name\": \"@X\", \"IsOutput\": true, \"SqlTypeName\": \"int\", \"IsNullable\": true } ],\n      \"ResultSets\": [ { \"Columns\": [ { \"Name\": \"Val\", \"SqlTypeName\": \"int\", \"IsNullable\": true } ] } ]\n    },\n    {\n      \"Schema\": \"dbo\",\n      \"Name\": \"CalcA\",\n      \"ResultSets\": [ { \"Columns\": [ { \"Name\": \"Value\", \"SqlTypeName\": \"int\", \"IsNullable\": false } ] } ],\n      \"Inputs\": []\n    }\n  ]\n}";
        File.WriteAllText(Path.Combine(schemaDir, "snap.json"), snapshot);

        var cfg = EnvConfiguration.Load(projectRoot: root.FullName);
        var renderer = new SimpleTemplateEngine();
        var gen = new SpocRGenerator(renderer, schemaProviderFactory: () => new SchemaMetadataProvider(root.FullName));

        // Act: run twice
        gen.GenerateAll(cfg, root.FullName);
        var hash1 = HashOutput(Path.Combine(root.FullName, "SpocR"));
        gen.GenerateAll(cfg, root.FullName);
        var hash2 = HashOutput(Path.Combine(root.FullName, "SpocR"));

        // Assert
        Assert.Equal(hash1, hash2);
    }

    private static string HashOutput(string dir)
    {
        if (!Directory.Exists(dir)) return string.Empty;
        var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        using var sha = SHA256.Create();
        var sb = new StringBuilder();
        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            // Normalize newlines
            content = content.Replace("\r\n", "\n");
            sb.AppendLine(Path.GetRelativePath(dir, file));
            sb.AppendLine(content);
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }
}
