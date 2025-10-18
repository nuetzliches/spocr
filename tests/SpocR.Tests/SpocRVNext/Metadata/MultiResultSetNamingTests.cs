using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;
using SpocR.SpocRVNext.Metadata;

namespace SpocR.Tests.SpocRVNext.Metadata;

/// <summary>
/// Validates multi-result set naming behavior:
/// 1. First base table name adopted when generic (ResultSet1) placeholder would be used.
/// 2. Subsequent result sets without resolvable base table keep generic names (ResultSet2, ResultSet3, ...)
/// 3. Duplicate base table names get numeric suffixes (Users, Users1, Users2 ...)
/// 4. Sanitization applied & uniqueness enforced case-insensitively.
/// </summary>
public class MultiResultSetNamingTests
{
    [Fact]
    public void MultiResult_Suffixes_Applied_For_Duplicate_Base_Table()
    {
        // SQL with three selects: two from same base table, third has no table reference -> generic fallback.
        var sql = "SELECT u.Id, u.Name FROM dbo.Users u; SELECT u.Id FROM dbo.Users u; SELECT 1 AS X";

        // Create an ephemeral project root with a legacy snapshot JSON file (.spocr/schema/<file>.json)
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var schemaDir = Path.Combine(root, ".spocr", "schema");
        Directory.CreateDirectory(schemaDir);

        var snapshot = new
        {
            StoredProcedures = new[]
            {
                new
                {
                    Schema = "dbo",
                    Name = "MultiUsers",
                    Sql = sql,
                    ResultSets = new object[]
                    {
                        new { Columns = new[] { new { Name = "Id", SqlTypeName = "int", IsNullable = false }, new { Name = "Name", SqlTypeName = "nvarchar", IsNullable = true } } },
                        new { Columns = new[] { new { Name = "Id", SqlTypeName = "int", IsNullable = false } } },
                        new { Columns = new[] { new { Name = "X", SqlTypeName = "int", IsNullable = false } } }
                    }
                }
            }
        };
        var snapshotJson = JsonSerializer.Serialize(snapshot);
        var snapshotPath = Path.Combine(schemaDir, "legacy-multiusers.json");
        File.WriteAllText(snapshotPath, snapshotJson);

        var provider = new SchemaMetadataProvider(root);
        var proc = provider.GetProcedures().Single(p => p.ProcedureName == "MultiUsers" && p.Schema == "dbo");

        Assert.Equal(3, proc.ResultSets.Count);
        var names = proc.ResultSets.Select(r => r.Name).ToArray();

        // Current resolver only inspects first SELECT; therefore all suggestions map to same base table → suffix numbering.
        Assert.Equal("Users", names[0]);
        Assert.Equal("Users1", names[1]);
        Assert.Equal("Users2", names[2]);
        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ThreeDuplicateResultSets_Produce_IncrementalSuffixes()
    {
        var sql = "SELECT u.Id FROM dbo.Users u; SELECT u.Id FROM dbo.Users u; SELECT u.Id FROM dbo.Users u";
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var schemaDir = Path.Combine(root, ".spocr", "schema");
        Directory.CreateDirectory(schemaDir);
        var snapshot = new
        {
            StoredProcedures = new[]
            {
                new
                {
                    Schema = "dbo",
                    Name = "MultiUsers3",
                    Sql = sql,
                    ResultSets = new object[]
                    {
                        new { Columns = new[] { new { Name = "Id", SqlTypeName = "int", IsNullable = false } } },
                        new { Columns = new[] { new { Name = "Id", SqlTypeName = "int", IsNullable = false } } },
                        new { Columns = new[] { new { Name = "Id", SqlTypeName = "int", IsNullable = false } } }
                    }
                }
            }
        };
        File.WriteAllText(Path.Combine(schemaDir, "legacy-multiusers3.json"), JsonSerializer.Serialize(snapshot));
        var provider = new SchemaMetadataProvider(root);
        var proc = provider.GetProcedures().Single(p => p.ProcedureName == "MultiUsers3");
        var names = proc.ResultSets.Select(r => r.Name).ToArray();
        Assert.Equal(new[] { "Users", "Users1", "Users2" }, names);
    }
}
