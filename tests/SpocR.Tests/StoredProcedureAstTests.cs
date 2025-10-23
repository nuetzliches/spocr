using System;
using System.Linq;
using Xunit;
using SpocR.Models;

namespace SpocR.Tests;

public class StoredProcedureAstTests
{
    private const string DefaultSchema = "samples";

    [Fact]
    public void ForJsonPath_With_Root_Should_Produce_ResultSet_With_ArrayWrapper()
    {
        var sql = @"CREATE PROCEDURE samples.OrderListAsJson AS
SELECT o.Id AS 'orders.id', o.Amount AS 'orders.amount'
FROM [samples].[Orders] o
FOR JSON PATH, ROOT('payload');";
        var model = StoredProcedureContentModel.Parse(sql, DefaultSchema);
        Assert.False(model.UsedFallbackParser, "Fallback parser should not be used for valid AST");
        Assert.Equal(0, model.ParseErrorCount); // tolerant parse
        Assert.True(model.ContainsSelect);
        var rs = Assert.Single(model.ResultSets);
        Assert.True(rs.ReturnsJson);
        Assert.True(rs.ReturnsJsonArray); // WITH array wrapper (no WITHOUT_ARRAY_WRAPPER option)
        Assert.Equal("payload", rs.JsonRootProperty);
        Assert.Contains(rs.Columns, c => c.Name == "orders.id");
        Assert.Contains(rs.Columns, c => c.Name == "orders.amount");
        var idCol = rs.Columns.First(c => c.Name == "orders.id");
        Assert.Equal("samples", idCol.SourceSchema);
        Assert.Equal("Orders", idCol.SourceTable);
        Assert.Equal("Id", idCol.SourceColumn);
    }

    [Fact]
    public void ForJsonPath_WithoutArrayWrapper_Should_Set_ReturnsJsonArray_False()
    {
        var sql = @"CREATE PROCEDURE samples.UserListSingle AS
SELECT u.Id AS 'user.id', u.Name AS 'user.name'
FROM samples.Users u
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, ROOT('payload');";
        var model = StoredProcedureContentModel.Parse(sql, DefaultSchema);
        var rs = Assert.Single(model.ResultSets);
        Assert.True(rs.ReturnsJson);
        Assert.False(rs.ReturnsJsonArray);
        Assert.Equal("payload", rs.JsonRootProperty);
    }

    [Fact]
    public void NestedJson_Subquery_Should_Flag_IsNestedJson_On_Column()
    {
        var sql = @"CREATE PROCEDURE samples.UserOrderWithUserJson AS
SELECT (
    SELECT u.Id AS 'user.id', u.Name AS 'user.name' FROM samples.Users u FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
) AS 'user', o.Id AS 'order.id'
FROM samples.Orders o
FOR JSON PATH, ROOT('payload');";
        var model = StoredProcedureContentModel.Parse(sql, DefaultSchema);
        var rs = Assert.Single(model.ResultSets);
        var userCol = rs.Columns.First(c => c.Name == "user");
        Assert.True(userCol.IsNestedJson == true, "Nested JSON column should be marked IsNestedJson");
        Assert.True(userCol.ReturnsJson == true);
        Assert.False(userCol.ReturnsJsonArray == true); // WITHOUT_ARRAY_WRAPPER inside nested
        Assert.Contains(userCol.Columns, c => c.Name == "user.id");
        Assert.Contains(userCol.Columns, c => c.Name == "user.name");
        // Outer column should not have direct Source bindings (nested container)
        Assert.Null(userCol.SourceSchema);
        Assert.Null(userCol.SourceTable);
    }


    [Fact]
    public void Ambiguous_SinglePart_Column_Should_Set_IsAmbiguous()
    {
        var sql = @"CREATE PROCEDURE samples.AmbiguousColumnTest AS
SELECT Id AS 'id'
FROM samples.Users u
JOIN samples.Orders o ON o.UserId = u.Id
FOR JSON PATH;";
        var model = StoredProcedureContentModel.Parse(sql, DefaultSchema);
        var rs = Assert.Single(model.ResultSets);
        var idCol = rs.Columns.First(c => c.Name == "id");
        Assert.True(idCol.IsAmbiguous == true, "Single-part column in multi-table scope should be ambiguous");
    }
}
