using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;
using SpocR.SpocRVNext.Metadata;

namespace SpocR.Tests.SpocRVNext.Metadata;

/// <summary>
/// Validates that dynamic SQL patterns (sp_executesql / EXEC(@sql)) force generic result set naming (resolver returns null).
/// </summary>
public class ResultSetNameResolverDynamicSqlTests
{
    [Fact]
    public void DynamicSql_SpExecutesql_Forces_Generic_Naming()
    {
        var sql = "DECLARE @sql NVARCHAR(max)='SELECT * FROM dbo.Users'; EXEC sp_executesql @sql;";
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var schemaDir = Path.Combine(root, ".spocr", "schema");
        Directory.CreateDirectory(schemaDir);
        var snapshot = new
        {
            StoredProcedures = new[] {
                new {
                    Schema = "dbo",
                    Name = "DynUsers",
                    Sql = sql,
                    ResultSets = new object[] {
                        new { Columns = new[] { new { Name = "Id", SqlTypeName = "int", IsNullable = false } } }
                    }
                }
            }
        };
        File.WriteAllText(Path.Combine(schemaDir, "dyn-users.json"), JsonSerializer.Serialize(snapshot));
        var provider = new SchemaMetadataProvider(root);
        var proc = provider.GetProcedures().Single(p => p.ProcedureName == "DynUsers");
        Assert.Single(proc.ResultSets);
        Assert.StartsWith("ResultSet", proc.ResultSets[0].Name); // Resolver skipped dynamic SQL
    }

    [Fact]
    public void DynamicSql_ExecVariable_Forces_Generic_Naming_MultipleSets()
    {
        var sql = "DECLARE @sql NVARCHAR(max)='SELECT Id FROM dbo.Users'; EXEC(@sql); SELECT 1 AS X;";
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var schemaDir = Path.Combine(root, ".spocr", "schema");
        Directory.CreateDirectory(schemaDir);
        var snapshot = new
        {
            StoredProcedures = new[] {
                new {
                    Schema = "dbo",
                    Name = "DynMulti",
                    Sql = sql,
                    ResultSets = new object[] {
                        new { Columns = new[] { new { Name = "Id", SqlTypeName = "int", IsNullable = false } } },
                        new { Columns = new[] { new { Name = "X", SqlTypeName = "int", IsNullable = false } } }
                    }
                }
            }
        };
        File.WriteAllText(Path.Combine(schemaDir, "dyn-multi.json"), JsonSerializer.Serialize(snapshot));
        var provider = new SchemaMetadataProvider(root);
        var proc = provider.GetProcedures().Single(p => p.ProcedureName == "DynMulti");
        Assert.Equal(2, proc.ResultSets.Count);
        Assert.Equal("ResultSet1", proc.ResultSets[0].Name);
        Assert.Equal("ResultSet2", proc.ResultSets[1].Name);
    }
}
