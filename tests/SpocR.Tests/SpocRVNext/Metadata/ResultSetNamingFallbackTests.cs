using System;
using System.IO;
using System.Text.Json;
using Xunit;
using SpocR.SpocRVNext.Metadata;

namespace SpocR.Tests.SpocRVNext.Metadata;

public class ResultSetNamingFallbackTests
{
    [Fact]
    public void UnparsableSql_KeepsFallbackResultSetName()
    {
        // Arrange: create temp schema snapshot directory structure
        var root = Path.Combine(Path.GetTempPath(), "spocr-test-" + Guid.NewGuid().ToString("N"));
        var schemaDir = Path.Combine(root, ".spocr", "schema");
        Directory.CreateDirectory(schemaDir);

        // Minimal legacy monolith snapshot JSON (no expanded index). We include a single procedure.
        // Use columns with no common >=3 prefix so ResultSetNaming.DeriveName yields "ResultSet1".
        var snapshotJson = """
        {
          "Procedures": [
            {
              "Name": "TestProc",
              "SchemaName": "dbo",
              "OperationName": "dbo__TestProc",
              "Sql": "CREATE PROCEDURE dbo.TestProc AS SELECT * FRM -- invalid keyword to force parse error",
              "ResultSets": [
                {
                  "Columns": [
                    {"Name": "Id", "SqlTypeName": "int", "IsNullable": false},
                    {"Name": "Name", "SqlTypeName": "nvarchar", "IsNullable": true}
                  ]
                }
              ]
            }
          ]
        }
        """;
        var snapshotPath = Path.Combine(schemaDir, "legacy.json");
        File.WriteAllText(snapshotPath, snapshotJson);

        var provider = new SchemaMetadataProvider(root);

        // Act
        var procs = provider.GetProcedures();
        var rs = provider.GetResultSets();

        // Assert
        Assert.Single(procs);
        Assert.Single(rs);
        // Because SQL parse failed, ResultSetNameResolver returns null and original name from ResultSetNaming (ResultSet1) stays.
        Assert.Equal("ResultSet1", rs[0].Name);
    }
}
