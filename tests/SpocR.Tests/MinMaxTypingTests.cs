using Xunit;
using System.Linq;
using SpocR.Models;

namespace SpocR.Tests;

public class MinMaxTypingTests
{
    private const string DefaultSchema = "dbo";
    private const string Sql = @"CREATE PROCEDURE dbo.MinMaxLiteralTest AS
BEGIN
    SELECT MIN(1) AS 'meta.minInt', MAX(CAST(1.5 AS decimal(4,2))) AS 'meta.maxDec'
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
END";

    [Fact]
    public void MinMax_Literal_Typing_Should_Infer_Int_And_Decimal()
    {
        var model = StoredProcedureContentModel.Parse(Sql, DefaultSchema);
        var rs = Assert.Single(model.ResultSets);
        Assert.True(rs.ReturnsJson);
        var minCol = rs.Columns.First(c => c.Name == "meta.minInt");
        var maxCol = rs.Columns.First(c => c.Name == "meta.maxDec");
        Assert.True(minCol.IsAggregate);
        Assert.Equal("min", minCol.AggregateFunction);
        Assert.Equal("int", minCol.SqlTypeName); // MIN(1)
        Assert.True(maxCol.IsAggregate);
        Assert.Equal("max", maxCol.AggregateFunction);
        Assert.Equal("decimal(18,2)", maxCol.SqlTypeName); // MAX(decimal literal)
    }
}
