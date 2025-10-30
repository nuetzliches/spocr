using System.Linq;
using Xunit;
using SpocR.SpocRVNext.Models;
using static SpocR.SpocRVNext.Models.StoredProcedureContentModel;

namespace SpocR.Tests;

public class FunctionJsonExpansionTests
{
    [Fact]
    public void FunctionRecordAsJson_Should_ProduceDeferredReference()
    {
        // Aktiviert Deferral für Funktions-JSON Expansion
        Environment.SetEnvironmentVariable("SPOCR_DEFER_JSON_FUNCTION_EXPANSION", "1");

        var sql = @"SELECT identity.RecordAsJson(@RecordId, @RowVersion, @CreatedUserId, @CreatedDt, @UpdatedUserId, @UpdatedDt) AS record
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;";
        var model = StoredProcedureContentModel.Parse(sql, "identity");
        System.Console.WriteLine("[test-diag] parseErrors=" + model.ParseErrorCount);
        var rs = Assert.Single(model.ResultSets);
        Assert.True(rs.ReturnsJson);
        Assert.False(rs.ReturnsJsonArray);

        var record = rs.Columns.First(c => c.Name == "record");
        // Container Flags
        Assert.True(record.ReturnsJson.GetValueOrDefault());
        Assert.Null(record.ReturnsJsonArray); // Noch nicht bestimmt im Snapshot
        Assert.True(record.IsNestedJson.GetValueOrDefault());
        Assert.Equal(ResultColumnExpressionKind.FunctionCall, record.ExpressionKind);
        Assert.NotNull(record.Reference);
        Assert.Equal("Function", record.Reference.Kind);
        Assert.Equal("identity", record.Reference.Schema);
        Assert.Equal("RecordAsJson", record.Reference.Name);
        Assert.True(record.DeferredJsonExpansion.GetValueOrDefault());
        // Noch keine verschachtelten Columns erzeugt
        Assert.True(record.Columns == null || record.Columns.Count == 0);
        // directionCode analog Test: Dummy IIF für Vergleich
        var sql2 = @"SELECT identity.RecordAsJson(@RecordId, @RowVersion, @CreatedUserId, @CreatedDt, @UpdatedUserId, @UpdatedDt) AS record,
       IIF(1=1,'in','out') AS directionCode
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;";
        var model2 = StoredProcedureContentModel.Parse(sql2, "identity");
        System.Console.WriteLine("[test-diag] parseErrors2=" + model2.ParseErrorCount);
        var rs2 = Assert.Single(model2.ResultSets);
        var directionCode = rs2.Columns.First(c => c.Name == "directionCode");
        Assert.Equal(ResultColumnExpressionKind.FunctionCall, directionCode.ExpressionKind);
        Assert.Equal("nvarchar", directionCode.SqlTypeName);
        Assert.Equal(3, directionCode.MaxLength);

        // Cleanup Flag
        Environment.SetEnvironmentVariable("SPOCR_DEFER_JSON_FUNCTION_EXPANSION", null);
    }
}
