using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace SpocR.Tests;

public class SchemaSnapshotStructureTests
{
    private static string RepoRoot => FindRepoRoot();
    private static string FindRepoRoot()
    {
        // Traverse up from test assembly location until .spocr folder for sample exists
        var dir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 10 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "samples", "restapi", ".spocr", "schema")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Repository root with samples/restapi/.spocr/schema not found");
    }

    private static string SampleSchemaDir => Path.Combine(RepoRoot, "samples", "restapi", ".spocr", "schema");

    [Fact]
    public void ExpandedSnapshot_ShouldContain_Types_Tables_Views_Directories()
    {
        Assert.True(Directory.Exists(Path.Combine(SampleSchemaDir, "types")), "types directory missing");
        Assert.True(Directory.Exists(Path.Combine(SampleSchemaDir, "tables")), "tables directory missing");
        Assert.True(Directory.Exists(Path.Combine(SampleSchemaDir, "views")), "views directory missing");
    }

    private record FileHashEntry(string Schema, string Name, string File, string Hash);
    private record IndexModel(
        int SchemaVersion,
        string Fingerprint,
        ParserInfo Parser,
        Stats Stats,
        FileHashEntry[] Procedures,
        FileHashEntry[] TableTypes,
        int? FunctionsVersion,
        FileHashEntry[] Functions,
        FileHashEntry[] UserDefinedTypes,
        FileHashEntry[] Tables,
        FileHashEntry[] Views
    );
    private record ParserInfo(string ToolVersion, int ResultSetParserVersion);
    private record Stats(int ProcedureTotal, int ProcedureSkipped, int ProcedureLoaded, int UdttTotal, int TableTotal, int ViewTotal, int UserDefinedTypeTotal);

    [Fact]
    public void Index_Should_List_NewArtefactEntries()
    {
        var indexPath = Path.Combine(SampleSchemaDir, "index.json");
        Assert.True(File.Exists(indexPath), "index.json missing");
        var json = File.ReadAllText(indexPath);
        var model = JsonSerializer.Deserialize<IndexModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(model);
        Assert.True(model!.UserDefinedTypes?.Length > 0, "UserDefinedTypes list empty in index");
        Assert.NotNull(model.Stats);
        Assert.True(model.Stats!.TableTotal > 0, "TableTotal should report captured tables");
        Assert.True(model.Stats.ViewTotal >= 0, "ViewTotal missing in Stats");
    }

    [Fact]
    public void TableColumns_Prune_FalseAndZeroValues()
    {
        var tablesDir = Path.Combine(SampleSchemaDir, "tables");
        var firstTableFile = Directory.EnumerateFiles(tablesDir, "*.json").FirstOrDefault();
        Assert.False(string.IsNullOrWhiteSpace(firstTableFile), "No table snapshot file found");
        var doc = JsonDocument.Parse(File.ReadAllText(firstTableFile));
        Assert.True(doc.RootElement.TryGetProperty("Columns", out var cols), "Table file has no Columns array");
        foreach (var col in cols.EnumerateArray())
        {
            // IsNullable false must be pruned (absence). If present ensure true.
            if (col.TryGetProperty("IsNullable", out var isNullProp))
            {
                Assert.True(isNullProp.GetBoolean(), "IsNullable present but not true (false should be pruned)");
            }
            // MaxLength 0 must be pruned – if MaxLength exists ensure >0
            if (col.TryGetProperty("MaxLength", out var lenProp))
            {
                Assert.True(lenProp.GetInt32() > 0, "MaxLength should be >0 if present");
            }
            // BaseSqlTypeName only if different from SqlTypeName
            if (col.TryGetProperty("BaseSqlTypeName", out var baseProp) && col.TryGetProperty("SqlTypeName", out var sqlTypeProp))
            {
                Assert.NotEqual(sqlTypeProp.GetString(), baseProp.GetString());
            }
        }
    }

    [Fact]
    public void UserDefinedTypeFiles_ShouldContain_BaseSqlTypeName()
    {
        var typesDir = Path.Combine(SampleSchemaDir, "types");
        var firstTypeFile = Directory.EnumerateFiles(typesDir, "*.json").FirstOrDefault();
        Assert.False(string.IsNullOrWhiteSpace(firstTypeFile), "No user defined type file found");
        var doc = JsonDocument.Parse(File.ReadAllText(firstTypeFile));
        Assert.True(doc.RootElement.TryGetProperty("BaseSqlTypeName", out var baseType), "BaseSqlTypeName missing in UDT file");
        Assert.False(string.IsNullOrWhiteSpace(baseType.GetString()), "BaseSqlTypeName should not be empty");
    }

    [Fact]
    public void ViewSnapshot_Should_Pruned_NullableFalse()
    {
        var viewsDir = Path.Combine(SampleSchemaDir, "views");
        // Views may be empty in sample; skip test gracefully if none
        var viewFile = Directory.EnumerateFiles(viewsDir, "*.json").FirstOrDefault();
        if (viewFile == null)
        {
            return; // nothing to assert yet
        }
        var doc = JsonDocument.Parse(File.ReadAllText(viewFile));
        if (!doc.RootElement.TryGetProperty("Columns", out var cols)) return;
        foreach (var col in cols.EnumerateArray())
        {
            if (col.TryGetProperty("IsNullable", out var isNullProp))
            {
                Assert.True(isNullProp.GetBoolean(), "View column IsNullable present but not true (false should be pruned)");
            }
        }
    }
}
