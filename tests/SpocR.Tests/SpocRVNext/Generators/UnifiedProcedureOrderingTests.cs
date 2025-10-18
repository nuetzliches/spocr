using System;
using System.IO;
using System.Linq;
using Xunit;
using SpocR.SpocRVNext.Metadata;
using SpocR.SpocRVNext.Generators;
using SpocR.SpocRVNext.Engine;

namespace SpocR.Tests.SpocRVNext.Generators;

public class UnifiedProcedureOrderingTests
{
    [Fact]
    public void GeneratedFile_HasExpectedSectionOrder()
    {
        // Arrange: create temp project root with minimal snapshot containing one procedure with input, output, resultset.
        var root = Path.Combine(Path.GetTempPath(), "spocr-order-" + Guid.NewGuid().ToString("N"));
        var schemaDir = Path.Combine(root, ".spocr", "schema");
        Directory.CreateDirectory(schemaDir);
        var json = """
        {
          "Procedures": [
            {
              "Name": "UserList",
              "SchemaName": "dbo",
              "OperationName": "dbo__UserList",
              "Sql": "CREATE PROCEDURE dbo.UserList @x INT AS SELECT Id, Name FROM Users;",
              "InputParameters": [ { "Name": "x", "SqlTypeName": "int", "IsNullable": false } ],
              "OutputParameters": [ { "Name": "y", "SqlTypeName": "int", "IsNullable": true } ],
              "ResultSets": [ { "Columns": [ { "Name": "Id", "SqlTypeName": "int", "IsNullable": false }, { "Name": "Name", "SqlTypeName": "nvarchar", "IsNullable": true } ] } ]
            }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(schemaDir, "legacy.json"), json);

        // Use metadata provider to load descriptors.
        var provider = new SchemaMetadataProvider(root);
        var procs = provider.GetProcedures();
        Assert.Single(procs);
        var proc = procs[0];

        // We need ProceduresGenerator to render unified file; construct with minimal dependencies.
        // Examine constructor signature; if changed, adapt minimal mocks. For now assume parameterless instantiation via reflection (internal).
        // Fallback: invoke Generate passing in loaded descriptors and capture written file.
        var engine = new SimpleTemplateEngine();
        // Derive repo root from test assembly location (walk upwards until src/SpocRVNext/Templates exists)
        var repoRoot = DeriveRepoRootFromAssembly();
        var loader = InMemoryTemplateLoader.Create(repoRoot);
        var gen = new ProceduresGenerator(engine, () => provider.GetProcedures(), loader, root, null);
        var outDir = Path.Combine(root, "generated");
        Directory.CreateDirectory(outDir);
        // Do NOT change CurrentDirectory (loader relies on repoRoot independent of working dir)
        // Minimal EnvConfiguration overrides for namespace derivation.
        Environment.SetEnvironmentVariable("SPOCR_NAMESPACE", "TestNs");
        var written = gen.Generate("TestNs", outDir);
        Assert.True(written >= 1, "Generator should write at least one file.");

        // Compute expected type names per NamePolicy (replicate generator splitting logic on '.')
        var opRaw = proc.OperationName; // may be dbo__UserList OR dbo.UserList depending on snapshot normalization
        var procPart = opRaw;
        var dotIdx = opRaw.IndexOf('.');
        if (dotIdx > 0) procPart = opRaw[(dotIdx + 1)..];
        var inputType = SpocR.SpocRVNext.Utils.NamePolicy.Input(procPart);
        var outputType = SpocR.SpocRVNext.Utils.NamePolicy.Output(procPart);
        var unifiedType = SpocR.SpocRVNext.Utils.NamePolicy.Result(procPart);
        var procedureType = SpocR.SpocRVNext.Utils.NamePolicy.Procedure(procPart);
        var planType = procedureType + "Plan";
        var rsType = SpocR.SpocRVNext.Utils.NamePolicy.ResultSet(procPart, proc.ResultSets[0].Name);

        // Dynamically locate the generated unified procedure file (avoid hard-coded filename assumptions)
        var candidateFiles = Directory.GetFiles(outDir, "*.cs", SearchOption.AllDirectories);
        string? file = null;
        foreach (var f in candidateFiles)
        {
            var text = File.ReadAllText(f);
            if (text.Contains($"public sealed class {unifiedType}"))
            {
                file = f;
                break;
            }
        }
        Assert.True(file != null, $"Unified procedure file not found. Searched {candidateFiles.Length} files for unified type '{unifiedType}'.");
        var code = File.ReadAllText(file!);

        int headerIdx = code.IndexOf("#nullable enable", StringComparison.Ordinal);
        int namespaceIdx = code.IndexOf("namespace", StringComparison.Ordinal);
        int usingIdx = code.IndexOf("using System;", StringComparison.Ordinal);
        int inputIdx = code.IndexOf($"public readonly record struct {inputType}", StringComparison.Ordinal);
        int outputIdx = code.IndexOf($"public readonly record struct {outputType}", StringComparison.Ordinal);
        int rsIdx = code.IndexOf($"public readonly record struct {rsType}", StringComparison.Ordinal);
        int unifiedIdx = code.IndexOf($"public sealed class {unifiedType}", StringComparison.Ordinal);
        int planIdx = code.IndexOf($"internal static partial class {planType}", StringComparison.Ordinal);
        int execIdx = code.IndexOf($"public static class {procedureType}", StringComparison.Ordinal);

        // Build ordered list of present section indices
        var indices = new System.Collections.Generic.List<(string Label, int Index)>
        {
            ("header", headerIdx),
            ("namespace", namespaceIdx),
            ("usings", usingIdx)
        };
        if (inputIdx >= 0) indices.Add(("input", inputIdx));
        if (outputIdx >= 0) indices.Add(("output", outputIdx));
        if (rsIdx >= 0) indices.Add(("resultset", rsIdx));
        indices.Add(("unified", unifiedIdx));
        indices.Add(("plan", planIdx));
        indices.Add(("executor", execIdx));
        // Validate all indices are present
        foreach (var (Label, Index) in indices)
        {
            Assert.True(Index >= 0, $"Missing section '{Label}' (index={Index}) in generated code. Code excerpt: {code.Substring(0, Math.Min(code.Length, 400))}");
        }
        // Ensure strictly increasing order
        for (int i = 1; i < indices.Count; i++)
        {
            Assert.True(indices[i - 1].Index < indices[i].Index, $"Section order violation: '{indices[i - 1].Label}' (idx={indices[i - 1].Index}) should precede '{indices[i].Label}' (idx={indices[i].Index}).");
        }
    }

    [Fact]
    public void GeneratedFile_MultiResult_HasSequentialResultSetRecordsInOrder()
    {
        // Arrange: snapshot with two result sets (second generic) plus input/output
        var root = Path.Combine(Path.GetTempPath(), "spocr-order-multi-" + Guid.NewGuid().ToString("N"));
        var schemaDir = Path.Combine(root, ".spocr", "schema");
        Directory.CreateDirectory(schemaDir);
        var json = """
                {
                    "Procedures": [
                        {
                            "Name": "UserDetailAndRoles",
                            "SchemaName": "dbo",
                            "OperationName": "dbo__UserDetailAndRoles",
                            "Sql": "CREATE PROCEDURE dbo.UserDetailAndRoles @id INT AS SELECT Id, Name FROM Users; SELECT RoleName FROM Roles;",
                            "InputParameters": [ { "Name": "id", "SqlTypeName": "int", "IsNullable": false } ],
                            "OutputParameters": [ { "Name": "status", "SqlTypeName": "int", "IsNullable": true } ],
                            "ResultSets": [
                                { "Columns": [ { "Name": "Id", "SqlTypeName": "int", "IsNullable": false }, { "Name": "Name", "SqlTypeName": "nvarchar", "IsNullable": true } ] },
                                { "Columns": [ { "Name": "RoleName", "SqlTypeName": "nvarchar", "IsNullable": false } ] }
                            ]
                        }
                    ]
                }
                """;
        File.WriteAllText(Path.Combine(schemaDir, "legacy.json"), json);

        var provider = new SchemaMetadataProvider(root);
        var proc = provider.GetProcedures().Single();
        var engine = new SimpleTemplateEngine();
        var repoRoot = DeriveRepoRootFromAssembly();
        var loader = InMemoryTemplateLoader.Create(repoRoot);
        var gen = new ProceduresGenerator(engine, () => provider.GetProcedures(), loader, root, null);
        var outDir = Path.Combine(root, "generated");
        Directory.CreateDirectory(outDir);
        Environment.SetEnvironmentVariable("SPOCR_NAMESPACE", "TestNs");
        gen.Generate("TestNs", outDir);

        // Name derivations
        var opRaw = proc.OperationName;
        var procPart = opRaw;
        var dotIdx = opRaw.IndexOf('.');
        if (dotIdx > 0) procPart = opRaw[(dotIdx + 1)..];
        var unifiedType = SpocR.SpocRVNext.Utils.NamePolicy.Result(procPart);
        var rsType1 = SpocR.SpocRVNext.Utils.NamePolicy.ResultSet(procPart, proc.ResultSets[0].Name);
        var rsType2 = SpocR.SpocRVNext.Utils.NamePolicy.ResultSet(procPart, proc.ResultSets[1].Name);

        var file = Directory.GetFiles(outDir, "*.cs", SearchOption.AllDirectories)
                .Select(f => new { f, text = File.ReadAllText(f) })
                .FirstOrDefault(x => x.text.Contains($"public sealed class {unifiedType}"));
        Assert.NotNull(file);
        var code = file!.text;

        // Indices for the two result set record structs
        int rs1Idx = code.IndexOf($"public readonly record struct {rsType1}", StringComparison.Ordinal);
        int rs2Idx = code.IndexOf($"public readonly record struct {rsType2}", StringComparison.Ordinal);
        Assert.True(rs1Idx >= 0, "First result set record struct missing.");
        Assert.True(rs2Idx >= 0, "Second result set record struct missing.");
        Assert.True(rs1Idx < rs2Idx, $"Result set records out of order (rs1Idx={rs1Idx}, rs2Idx={rs2Idx}).");

        // Ensure unified aggregate appears after both result sets
        int unifiedIdx = code.IndexOf($"public sealed class {unifiedType}", StringComparison.Ordinal);
        Assert.True(unifiedIdx > rs2Idx, "Unified aggregate should appear after all result set record structs.");
    }

    // Legacy helper removed (logic consolidated into list-based assertions)

    private sealed class InMemoryTemplateLoader : ITemplateLoader
    {
        private readonly System.Collections.Generic.Dictionary<string, string> _templates;
        private InMemoryTemplateLoader(System.Collections.Generic.Dictionary<string, string> templates) { _templates = templates; }
        public static InMemoryTemplateLoader Create(string repoRoot)
        {
            var dict = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string unifiedPath = Path.Combine(repoRoot, "src", "SpocRVNext", "Templates", "Procedures", "UnifiedProcedure.spt");
            string headerPath = Path.Combine(repoRoot, "src", "SpocRVNext", "Templates", "_Header.spt");
            Console.WriteLine($"[ordering-test] repoRoot={repoRoot}");
            Console.WriteLine($"[ordering-test] unifiedPath={unifiedPath} exists={File.Exists(unifiedPath)}");
            Console.WriteLine($"[ordering-test] headerPath={headerPath} exists={File.Exists(headerPath)}");
            if (File.Exists(unifiedPath)) dict["UnifiedProcedure"] = File.ReadAllText(unifiedPath);
            if (File.Exists(headerPath)) dict["_Header"] = File.ReadAllText(headerPath); else dict["_Header"] = "// <auto-generated/>";
            return new InMemoryTemplateLoader(dict);
        }
        public bool TryLoad(string name, out string content) => _templates.TryGetValue(name, out content);
        public System.Collections.Generic.IEnumerable<string> ListNames() => _templates.Keys;
    }

    private static string DeriveRepoRootFromAssembly()
    {
        var baseDir = AppContext.BaseDirectory; // tests/SpocR.Tests/bin/Debug/netX
        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "SpocRVNext", "Templates"))) return dir.FullName;
            dir = dir.Parent;
        }
        // Fallback: current directory
        return Directory.GetCurrentDirectory();
    }
}
