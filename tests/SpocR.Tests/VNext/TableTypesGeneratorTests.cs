using System;
using System.IO;
using System.Text.Json;
using Xunit;
using SpocRVNext.Metadata;
using SpocRVNext.Configuration;
using SpocR.SpocRVNext.Generators;

namespace SpocR.Tests.VNext;

public class TableTypesGeneratorTests
{
    [Fact]
    public void GeneratesFiles_WhenSnapshotContainsTableTypes()
    {
        var root = Directory.CreateTempSubdirectory();
        // Arrange fake snapshot
        var schemaDir = Path.Combine(root.FullName, ".spocr", "schema");
        Directory.CreateDirectory(schemaDir);
        var snapshotPath = Path.Combine(schemaDir, "abc123.json");
        var snapshotJson = "{\n  \"UserDefinedTableTypes\": [ { \"Schema\": \"dbo\", \"Name\": \"UserIdList\", \"UserTypeId\": 1, \"Columns\": [ { \"Name\": \"Id\", \"SqlTypeName\": \"int\", \"IsNullable\": false, \"MaxLength\": null } ] } ]\n}";
        File.WriteAllText(snapshotPath, snapshotJson);
        // minimal .env to satisfy validation
        File.WriteAllText(Path.Combine(root.FullName, ".env"), "SPOCR_GENERATOR_MODE=next\n# SPOCR_NAMESPACE placeholder\n");
        var cfg = EnvConfiguration.Load(projectRoot: root.FullName);
        var provider = new TableTypeMetadataProvider();
        // Move CWD temporarily
        var original = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(root.FullName);
        try
        {
            // Prepare templates directory with our templates (copy from repo if exists or create minimal)
            var templatesDir = Path.Combine(root.FullName, "src", "SpocRVNext", "Templates");
            Directory.CreateDirectory(templatesDir);
            // Create interface template
            File.WriteAllText(Path.Combine(templatesDir, "ITableType.spt"), "namespace {{ Namespace }}.TableTypes;\npublic interface ITableType {}\n");
            // Create table type template (simplified)
            File.WriteAllText(Path.Combine(templatesDir, "TableType.spt"), "namespace {{ Namespace }}.TableTypes.{{ Schema }};\npublic readonly record struct {{ TypeName }}(\n{{#each Columns}}    {{ ClrType }} {{ PropertyName }}{{ Separator }}\n{{/each}}) : ITableType { }\n");
            var loader = new SpocR.SpocRVNext.Engine.FileSystemTemplateLoader(templatesDir);
            var gen = new TableTypesGenerator(cfg, provider, new SpocR.SpocRVNext.Engine.SimpleTemplateEngine(), loader);
            var count = gen.Generate();
            Assert.True(count >= 1);
            var outDir = Path.Combine(root.FullName, cfg.OutputDir!, "dbo");
            Assert.True(Directory.Exists(outDir));
            var files = Directory.GetFiles(outDir, "*TableType.cs");
            Assert.Contains(files, f => f.EndsWith("UserIdListTableType.cs"));
            // Assert interface file exists at root output directory
            Assert.True(File.Exists(Path.Combine(root.FullName, cfg.OutputDir!, "ITableType.cs")));
            // Assert generated file contains inheritance marker
            var content = File.ReadAllText(files[0]);
            Assert.Contains(": ITableType", content);
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
        }
    }
}
