using System;
using System.IO;
using System.Linq;
using Xunit;
using SpocR.SpocRVNext.Metadata;

namespace SpocR.Tests;

public class ExecSourceReferenceTests
{
    [Fact]
    public void ExecSourceResultSet_GetsProcedureReference()
    {
        // Arrange: fabricate minimal legacy snapshot file with ExecSource* fields
        var root = Path.Combine(Path.GetTempPath(), "spocr_exec_ref_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var schemaDir = Path.Combine(root, ".spocr", "schema");
        Directory.CreateDirectory(schemaDir);
        var snapshotPath = Path.Combine(schemaDir, "abc123.json");
        var json = "{\n  \"Procedures\": [\n    { \"Schema\": \"dbo\", \"Name\": \"WrapperProc\", \"ResultSets\": [ { \"ExecSourceSchemaName\": \"dbo\", \"ExecSourceProcedureName\": \"InnerProc\", \"Columns\": [] } ] }\n  ]\n}";
        File.WriteAllText(snapshotPath, json);

        // Act
        var provider = new SchemaMetadataProvider(root);
        var rs = provider.GetResultSets().FirstOrDefault(r => r.Name.Equals("ResultSet0", StringComparison.OrdinalIgnoreCase));

        // Assert
        Assert.NotNull(rs);
        Assert.NotNull(rs.Reference); // Consolidated
        Assert.Equal("Procedure", rs.Reference!.Kind);
        Assert.Equal("dbo", rs.Reference.Schema);
        Assert.Equal("InnerProc", rs.Reference.Name);
        // Backward fields still load for now (transitional)
        Assert.Equal("dbo", rs.ExecSourceSchemaName);
        Assert.Equal("InnerProc", rs.ExecSourceProcedureName);
    }
}
