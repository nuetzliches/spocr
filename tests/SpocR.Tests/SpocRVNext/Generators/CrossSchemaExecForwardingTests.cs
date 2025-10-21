using System;
using System.IO;
using System.Linq;
using Xunit;
using SpocR.SpocRVNext.Engine;
using SpocR.SpocRVNext.Metadata;
using SpocR.SpocRVNext.Generators;
using System.Text.RegularExpressions;

namespace SpocR.Tests.SpocRVNext.Generators;

/// <summary>
/// Tests cross-schema EXEC forwarding logic in ProceduresGenerator:
/// 1) Pure wrapper procedure (only placeholder) -> full replacement with target sets.
/// 2) Mixed procedure (own set + placeholder) -> append forwarded sets.
/// Uses synthetic snapshot JSON describing procedures and placeholder result sets with ExecSource metadata.
/// </summary>
public class CrossSchemaExecForwardingTests
{
    [Fact]
    public void PureWrapper_Should_Forward_All_Target_ResultSets()
    {
        var (code, mappingCount) = GenerateWithInMemoryProvider(wrapperOnly: true);
        // Expect two forwarded mappings
        Assert.Equal(2, mappingCount);
        // Names now prefixed with target procedure for uniqueness
        Assert.Contains("new(\"TargetProc_ResultSetA\"", code);
        Assert.Contains("new(\"TargetProc_ResultSetB\"", code);
    }

    [Fact]
    public void MixedProcedure_Should_Append_Target_ResultSets_After_Own()
    {
        var (code, mappingCount) = GenerateWithInMemoryProvider(wrapperOnly: false);
        // Own set + 2 forwarded = 3 mappings
        Assert.Equal(3, mappingCount);
        Assert.Contains("new(\"OwnSet\"", code);
        var ownIdx = code.IndexOf("new(\"OwnSet\"", StringComparison.Ordinal);
        var aIdx = code.IndexOf("new(\"TargetProc_ResultSetA\"", StringComparison.Ordinal);
        Assert.True(ownIdx >= 0 && aIdx > ownIdx);
    }

    private static (string code, int mappingCount) GenerateWithInMemoryProvider(bool wrapperOnly)
    {
        // Build target procedure with two concrete result sets
        var targetSets = new[]
        {
            new ResultSetDescriptor(0, "ResultSetA", new [] { new FieldDescriptor("a","a","int",false,"int") }),
            new ResultSetDescriptor(1, "ResultSetB", new [] { new FieldDescriptor("b","b","int",false,"int") })
        };
        var targetProc = new ProcedureDescriptor(
            ProcedureName: "TargetProc",
            Schema: "other",
            OperationName: "other__TargetProc",
            InputParameters: Array.Empty<FieldDescriptor>(),
            OutputFields: Array.Empty<FieldDescriptor>(),
            ResultSets: targetSets
        );
        // Wrapper placeholder(s)
        var placeholder = new ResultSetDescriptor(0, "ResultSet1", Array.Empty<FieldDescriptor>(), ExecSourceSchemaName: "other", ExecSourceProcedureName: "TargetProc");
        ResultSetDescriptor ownSet = new(0, "OwnSet", new[] { new FieldDescriptor("id", "id", "int", false, "int") });
        var wrapperSets = wrapperOnly ? new[] { placeholder } : new[] { ownSet, placeholder };
        var wrapperProc = new ProcedureDescriptor(
            ProcedureName: wrapperOnly ? "WrapperOnlyTest" : "MixedWrapperTest",
            Schema: "dbo",
            OperationName: wrapperOnly ? "dbo__WrapperOnlyTest" : "dbo__MixedWrapperTest",
            InputParameters: Array.Empty<FieldDescriptor>(),
            OutputFields: Array.Empty<FieldDescriptor>(),
            ResultSets: wrapperSets
        );
        var procedures = new[] { targetProc, wrapperProc };
        // Sanity: ensure placeholder metadata present when expected
        if (!wrapperOnly)
        {
            Assert.Contains(wrapperProc.ResultSets, rs => rs.Fields.Count == 0 && rs.ExecSourceProcedureName != null);
        }
        else
        {
            Assert.Single(wrapperProc.ResultSets, rs => rs.Fields.Count == 0 && rs.ExecSourceProcedureName != null);
        }
        var root = Path.Combine(Path.GetTempPath(), "spocr-xschema-inmem-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var engine = new SimpleTemplateEngine();
        var repoRoot = DeriveRepoRootFromAssembly();
        var loader = InMemoryTemplateLoader.Create(repoRoot);
        var gen = new ProceduresGenerator(engine, () => procedures, loader, root, null);
        var outDir = Path.Combine(root, "generated");
        Directory.CreateDirectory(outDir);
        gen.Generate("TestNs", outDir);
        var file = Directory.GetFiles(outDir, "*.cs", SearchOption.AllDirectories)
            .Select(f => new { f, text = File.ReadAllText(f) })
            .First(x => x.text.Contains(wrapperOnly ? "WrapperOnlyTest" : "MixedWrapperTest"));
        var code = file.text;
        var mappingCount = Regex.Matches(code, "new\\(\\\"").Count; // count all mappings for this procedure
        return (code, mappingCount);
    }

    private sealed class InMemoryTemplateLoader : SpocR.SpocRVNext.Engine.ITemplateLoader
    {
        private readonly System.Collections.Generic.Dictionary<string, string> _templates;
        private InMemoryTemplateLoader(System.Collections.Generic.Dictionary<string, string> templates) { _templates = templates; }
        public static InMemoryTemplateLoader Create(string repoRoot)
        {
            var dict = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string unifiedPath = Path.Combine(repoRoot, "src", "SpocRVNext", "Templates", "Procedures", "UnifiedProcedure.spt");
            string headerPath = Path.Combine(repoRoot, "src", "SpocRVNext", "Templates", "_Header.spt");
            if (File.Exists(unifiedPath)) dict["UnifiedProcedure"] = File.ReadAllText(unifiedPath);
            if (File.Exists(headerPath)) dict["_Header"] = File.ReadAllText(headerPath); else dict["_Header"] = "// <auto-generated/>";
            return new InMemoryTemplateLoader(dict);
        }
        public bool TryLoad(string name, out string content) => _templates.TryGetValue(name, out content);
        public System.Collections.Generic.IEnumerable<string> ListNames() => _templates.Keys;
    }

    private static string DeriveRepoRootFromAssembly()
    {
        var baseDir = AppContext.BaseDirectory; // tests bin path
        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "SpocRVNext", "Templates"))) return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }
}
