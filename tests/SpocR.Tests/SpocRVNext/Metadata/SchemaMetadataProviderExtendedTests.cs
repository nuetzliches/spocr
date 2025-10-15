using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;
using SpocR.SpocRVNext.Metadata;

namespace SpocR.Tests.SpocRVNext.Metadata;

public sealed class SchemaMetadataProviderExtendedTests
{
    private static string CreateSnapshot(params object[] procedures)
    {
        var root = Path.Combine(Path.GetTempPath(), "spocr-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var schemaDir = Path.Combine(root, ".spocr", "schema");
        Directory.CreateDirectory(schemaDir);
        var doc = new
        {
            Procedures = procedures
        };
        var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(schemaDir, "snapshot-test.json"), json);
        return root;
    }

    [Fact]
    public void MultiResult_FirstRenamed_SecondGetsSuffix()
    {
        var proc = new
        {
            Schema = "dbo",
            Name = "GetStuff",
            Sql = "SELECT * FROM dbo.Users; SELECT Id FROM dbo.Roles;",
            Inputs = Array.Empty<object>(),
            ResultSets = new object[]
            {
                new { Columns = new[]{ new { Name = "Id", SqlTypeName = "int", IsNullable = false, MaxLength = (int?)null } } },
                new { Columns = new[]{ new { Name = "Id", SqlTypeName = "int", IsNullable = false, MaxLength = (int?)null } } }
            }
        };
        var root = CreateSnapshot(proc);
        try
        {
            var provider = new SchemaMetadataProvider(root);
            var rs = provider.GetResultSets();
            Assert.Equal(2, rs.Count);
            Assert.Equal("Users", rs[0].Name);
            Assert.Equal("Users1", rs[1].Name); // Neuer Suffix statt generischem ResultSet Name
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void UnparsableSql_FallsBack_GenericNamesRemain()
    {
        var proc = new
        {
            Schema = "dbo",
            Name = "Broken",
            Sql = "SELECT * FROM dbo.Users; /* unterbrochen */ THIS IS NOT SQL",
            Inputs = Array.Empty<object>(),
            ResultSets = new object[]
            {
                new { Columns = new[]{ new { Name = "ColA", SqlTypeName = "int", IsNullable = false, MaxLength = (int?)null } } }
            }
        };
        var root = CreateSnapshot(proc);
        try
        {
            var provider = new SchemaMetadataProvider(root);
            var rs = provider.GetResultSets();
            Assert.Single(rs);
            Assert.StartsWith("ResultSet", rs[0].Name, StringComparison.OrdinalIgnoreCase);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void MixedCaseTable_NormalizesToTableName()
    {
        var proc = new
        {
            Schema = "dbo",
            Name = "GetUsers",
            Sql = "SELECT * FROM DBO.UsErS;",
            Inputs = Array.Empty<object>(),
            ResultSets = new object[]
            {
                new { Columns = new[]{ new { Name = "Id", SqlTypeName = "int", IsNullable = false, MaxLength = (int?)null } } }
            }
        };
        var root = CreateSnapshot(proc);
        try
        {
            var provider = new SchemaMetadataProvider(root);
            var rs = provider.GetResultSets();
            Assert.Single(rs);
            Assert.Equal("UsErS".ToLowerInvariant().Replace("users","users"), rs[0].Name.ToLowerInvariant()); // Weak check: just ensure name becomes 'Users' ignoring case
            Assert.Equal("users", rs[0].Name.ToLowerInvariant());
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void DuplicateBaseTable_SubsequentGetsNumericSuffix()
    {
        var proc = new
        {
            Schema = "dbo",
            Name = "DoubleUsers",
            Sql = "SELECT * FROM dbo.Users; SELECT * FROM dbo.Users;",
            Inputs = Array.Empty<object>(),
            ResultSets = new object[]
            {
                new { Columns = new[]{ new { Name = "Id", SqlTypeName = "int", IsNullable = false, MaxLength = (int?)null } } },
                new { Columns = new[]{ new { Name = "Id", SqlTypeName = "int", IsNullable = false, MaxLength = (int?)null } } }
            }
        };
        var root = CreateSnapshot(proc);
        try
        {
            var provider = new SchemaMetadataProvider(root);
            var rs = provider.GetResultSets();
            Assert.Equal(2, rs.Count);
            Assert.Equal("Users", rs[0].Name);
            Assert.Equal("Users1", rs[1].Name);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
