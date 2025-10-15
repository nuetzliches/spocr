using System;
using System.IO;
using System.Linq;
using Xunit;
using SpocR.SpocRVNext.Metadata;

namespace SpocR.Tests.SpocRVNext.Metadata;

public class SchemaMetadataProviderResultSetResolverTests
{
    private string CreateSnapshot(string content)
    {
        var tmpRoot = Path.Combine(Path.GetTempPath(), "spocr-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmpRoot, ".spocr", "schema"));
        File.WriteAllText(Path.Combine(tmpRoot, ".spocr", "schema", "snapshot-test.json"), content);
        return tmpRoot;
    }

    [Fact]
    public void Renames_Generic_ResultSet_Name_From_Table_In_Sql()
    {
        // Arrange: single procedure, single generic result set with two columns; raw SQL selects from dbo.Users
        var snapshotJson = @"{
  ""Procedures"": [
    {
      ""Schema"": ""dbo"",
      ""Name"": ""GetUsers"",
      ""Sql"": ""CREATE PROCEDURE dbo.GetUsers AS SELECT Id, UserName FROM dbo.Users;"",
      ""Inputs"": [],
      ""ResultSets"": [
        {
          ""Columns"": [
            { ""Name"": ""Id"", ""SqlTypeName"": ""int"", ""IsNullable"": false },
            { ""Name"": ""UserName"", ""SqlTypeName"": ""nvarchar"", ""IsNullable"": true, ""MaxLength"": 100 }
          ]
        }
      ]
    }
  ]
}";
        var root = CreateSnapshot(snapshotJson);
        var provider = new SchemaMetadataProvider(root);

        // Act
        var rs = provider.GetResultSets();
        var proc = provider.GetProcedures().Single();

        // Assert
        Assert.Single(rs);
        Assert.Equal("Users", rs[0].Name); // renamed from generic ResultSet1 -> Users
        Assert.Equal("dbo.GetUsers", proc.OperationName);
    }

    [Fact]
  public void Duplicate_Table_Uses_Suffix_For_Subsequent()
    {
  // Arrange: two result sets; second resolves to same table name; expect suffix Items1
        var snapshotJson = @"{
  ""Procedures"": [
    {
      ""Schema"": ""dbo"",
      ""Name"": ""GetStuff"",
      ""Sql"": ""CREATE PROCEDURE dbo.GetStuff AS SELECT * FROM dbo.Items; SELECT * FROM dbo.Items;"",
      ""Inputs"": [],
      ""ResultSets"": [
        { ""Columns"": [ { ""Name"": ""Id"", ""SqlTypeName"": ""int"", ""IsNullable"": false } ] },
        { ""Columns"": [ { ""Name"": ""Id"", ""SqlTypeName"": ""int"", ""IsNullable"": false } ] }
      ]
    }
  ]
}";
        var root = CreateSnapshot(snapshotJson);
        var provider = new SchemaMetadataProvider(root);

        // Act
        var rs = provider.GetResultSets().OrderBy(r => r.Index).ToList();

        // Assert
        Assert.Equal(2, rs.Count);
  Assert.Equal("Items", rs[0].Name); // first renamed
  Assert.Equal("Items1", rs[1].Name); // second now suffixed instead of generic
    }
}
