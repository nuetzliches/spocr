using System.Linq;
using Xunit;
using SpocR.Models;
using static SpocR.Models.StoredProcedureContentModel;

namespace SpocR.Tests;

public class PathFindAsJsonTypingTests
{
    private const string DefaultSchema = "samples";

    [Fact]
    public void RemoteStatusId_And_OrderNo_Should_Bind_Int_From_Table_DirectionCode_Typed()
    {
        // Resolver bereitstellen (AST-only strukturelle Typzuweisung)
        StoredProcedureContentModel.ResolveTableColumnType = (schema, table, column) =>
        {
            if (string.Equals(schema, "workflow", System.StringComparison.OrdinalIgnoreCase)
                && string.Equals(table, "Path", System.StringComparison.OrdinalIgnoreCase))
            {
                return column switch
                {
                    "OutputStatusId" => ("int", 4, false),
                    "InputStatusId" => ("int", 4, false),
                    "OutputOrderNo" => ("int", 4, false),
                    "InputOrderNo" => ("int", 4, false),
                    _ => default
                };
            }
            return default;
        };

        var sql = @"CREATE PROCEDURE workflow.PathFindAsJsonTest AS
SELECT
    CASE WHEN p.OutputStatusId IS NOT NULL THEN p.OutputStatusId ELSE p.InputStatusId END AS 'remoteStatusId',
    CASE WHEN p.OutputOrderNo IS NOT NULL THEN p.OutputOrderNo ELSE p.InputOrderNo END AS 'orderNo',
    IIF(1=1,'in','out') AS 'directionCode'
FROM workflow.Path p
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;";

        var model = StoredProcedureContentModel.Parse(sql, "workflow");
        var rs = Assert.Single(model.ResultSets);
        Assert.True(rs.ReturnsJson);
        Assert.False(rs.ReturnsJsonArray);

        var remoteStatusId = rs.Columns.First(c => c.Name == "remoteStatusId");
        var orderNo = rs.Columns.First(c => c.Name == "orderNo");
        var directionCode = rs.Columns.First(c => c.Name == "directionCode");

        Assert.Equal("int", remoteStatusId.SqlTypeName);
        Assert.Equal(4, remoteStatusId.MaxLength);
        Assert.Equal("int", orderNo.SqlTypeName);
        Assert.Equal(4, orderNo.MaxLength);

        Assert.Equal(ResultColumnExpressionKind.FunctionCall, directionCode.ExpressionKind);
        Assert.Equal("nvarchar", directionCode.SqlTypeName);
        Assert.Equal(3, directionCode.MaxLength);

        // Resolver zurücksetzen, um Seiteneffekte auf andere Tests zu vermeiden
        StoredProcedureContentModel.ResolveTableColumnType = null;
    }
}
