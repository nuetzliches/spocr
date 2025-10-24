using System;
using System.Linq;
using Xunit;
using SpocR.Models;
using static SpocR.Models.StoredProcedureContentModel;

namespace SpocR.Tests;

public class IifParsingTests
{
    private const string DefaultSchema = "samples";

    [Fact]
    public void Iif_With_Two_Different_SourceColumns_Should_Not_Infer_Type()
    {
        var sql = @"CREATE PROCEDURE samples.IifDifferentColumns AS
SELECT IIF(u.Id > 0, u.Id, o.Id) AS 'result.id'
FROM samples.Users u
JOIN samples.Orders o ON o.UserId = u.Id
FOR JSON PATH;";
        var model = StoredProcedureContentModel.Parse(sql, DefaultSchema);
        var rs = Assert.Single(model.ResultSets);
        var col = rs.Columns.First(c => c.Name == "result.id");
        Assert.Equal(ResultColumnExpressionKind.FunctionCall, col.ExpressionKind); // IIF classified as FunctionCall
        Assert.True(string.IsNullOrWhiteSpace(col.SqlTypeName), "Type should not be inferred when branches differ after heuristic removal");
    }

    [Fact]
    public void Iif_With_Literal_Strings_Should_Set_NVarChar_Length()
    {
        var sql = @"CREATE PROCEDURE samples.IifLiteralStrings AS
SELECT IIF(1=1, 'abc', 'abcdef') AS 'val'
FROM samples.Users u
FOR JSON PATH;";
        var model = StoredProcedureContentModel.Parse(sql, DefaultSchema);
        var rs = Assert.Single(model.ResultSets);
        var col = rs.Columns.First(c => c.Name == "val");
        Assert.Equal(ResultColumnExpressionKind.FunctionCall, col.ExpressionKind);
        Assert.Equal("nvarchar", col.SqlTypeName);
        Assert.Equal(6, col.MaxLength); // longest literal
    }

    [Fact]
    public void Iif_With_Same_SourceColumn_Should_Not_Infer_Type_Now()
    {
        var sql = @"CREATE PROCEDURE samples.IifSameColumn AS
SELECT IIF(u.Id > 0, u.Id, u.Id) AS 'x'
FROM samples.Users u
FOR JSON PATH;";
        var model = StoredProcedureContentModel.Parse(sql, DefaultSchema);
        var rs = Assert.Single(model.ResultSets);
        var col = rs.Columns.First(c => c.Name == "x");
        // Previously might infer 'int'; after removal should stay empty
        Assert.Equal(ResultColumnExpressionKind.FunctionCall, col.ExpressionKind);
        Assert.True(string.IsNullOrWhiteSpace(col.SqlTypeName));
    }
}
