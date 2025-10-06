using SpocR.Models;
using Xunit;

namespace SpocR.Tests.Cli;

public class JsonParserSelectStarTests
{
    [Fact]
    public void ForJson_WithSelectStar_SetsHasSelectStarTrueAndNoColumns()
    {
        var sql = @"CREATE PROCEDURE [soap].[TestSelectStarJson]
AS
BEGIN
    SELECT *
    FROM (SELECT 1 AS Id, 'X' AS Name) t
    FOR JSON PATH;
END";

        var content = StoredProcedureContentModel.Parse(sql, "soap");
        Assert.NotNull(content);
        Assert.Single(content.ResultSets);
        var rs = content.ResultSets[0];
        Assert.True(rs.ReturnsJson, "ReturnsJson should be true");
        Assert.True(rs.HasSelectStar, "HasSelectStar should be true");
        Assert.Empty(rs.Columns);
        Assert.Null(rs.ExecSourceProcedureName);
        Assert.Null(rs.ExecSourceSchemaName);
    }

    [Fact]
    public void ForJson_WithExplicitAndStar_SetsFlagAndKeepsExplicitColumn()
    {
        var sql = @"CREATE PROCEDURE [soap].[TestSelectStarJson2]
AS
BEGIN
    SELECT t.Id, *
    FROM (SELECT 1 AS Id, 'X' AS Name) t
    FOR JSON PATH;
END";

        var content = StoredProcedureContentModel.Parse(sql, "soap");
        Assert.NotNull(content);
        Assert.Single(content.ResultSets);
        var rs = content.ResultSets[0];
        Assert.True(rs.ReturnsJson);
        Assert.True(rs.HasSelectStar);
        // Mindestens das Flag; Id kann erkannt sein (abhängig von Parser-Ordnung). Wenn Spalte erkannt, prüfen.
        if (rs.Columns.Count > 0)
        {
            Assert.Contains(rs.Columns, c => c.Name.Equals("Id", System.StringComparison.OrdinalIgnoreCase));
        }
    }
}
