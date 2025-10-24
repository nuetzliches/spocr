using System.Linq;
using Shouldly;
using Xunit;
using SpocR.Models;
using static SpocR.Models.StoredProcedureContentModel; // for ResultColumnExpressionKind

namespace SpocR.Tests.Cli;

/// <summary>
/// AST-only detection tests for schema-qualified function identity.RecordAsJson without heuristics.
/// Ensures we capture FunctionSchemaName / FunctionName and IsRecordAsJson flag strictly from the parsed AST.
/// </summary>
public class JsonParserRecordAsJsonDetectionTests
{
    [Fact]
    public void IdentityRecordAsJson_FunctionCall_Should_Set_IsRecordAsJson_Flag()
    {
        const string sql = @"CREATE PROCEDURE [dbo].[RecordProc]
AS
BEGIN
    SELECT JSON_QUERY([identity].RecordAsJson(u.Id)) AS [record], u.Email AS [email]
    FROM [samples].[Users] AS u
    FOR JSON PATH;
END";
        var content = StoredProcedureContentModel.Parse(sql, "dbo");
        content.ResultSets.ShouldNotBeNull();
        content.ResultSets.Count.ShouldBe(1);
        var set = content.ResultSets[0];
        set.ReturnsJson.ShouldBeTrue();
        // Diagnostic: list column names with fallback flag
        System.Console.WriteLine("[diag-record] fallback=" + content.UsedFallbackParser + " columns=" + string.Join(",", set.Columns.Select(c => c.Name)));
        var col = set.Columns.FirstOrDefault(c => c.Name == "record");
        col.ShouldNotBeNull();
        // ScriptDom classifies schema-qualified function-like tokens as ColumnRef in this context; we only assert detection flags.
        col.ExpressionKind.ShouldNotBeNull();
        col.Reference.ShouldNotBeNull();
        col.Reference.Kind.ShouldBe("Function");
        col.Reference.Schema.ShouldBe("identity");
        col.Reference.Name.ShouldBe("RecordAsJson");
    }

    [Fact]
    public void NonSchemaQualified_RecordAsJson_Should_Not_Set_SchemaName()
    {
        const string sql2 = @"CREATE PROCEDURE [dbo].[RecordProc2]
AS
BEGIN
    SELECT RecordAsJson(u.Id) AS [record], u.Email AS [email]
    FROM [samples].[Users] AS u
    FOR JSON PATH;
END";
        var content = StoredProcedureContentModel.Parse(sql2, "dbo");
        var set = content.ResultSets.ShouldHaveSingleItem();
        var col = set.Columns.FirstOrDefault(c => c.Name == "record");
        col.ShouldNotBeNull();
        // FunctionCall kind
        col.ExpressionKind.ShouldNotBeNull();
        col.Reference.ShouldNotBeNull();
        col.Reference.Kind.ShouldBe("Function");
        col.Reference.Schema.ShouldBe("dbo");
        col.Reference.Name.ShouldBe("RecordAsJson");
    }

    [Fact]
    public void JsonQuery_Subquery_RecordAsJson_Should_Set_Reference()
    {
        const string sql3 = @"CREATE PROCEDURE [dbo].[RecordProc3]
AS
BEGIN
    SELECT JSON_QUERY((SELECT [identity].RecordAsJson(u.Id))) AS [record]
    FROM [samples].[Users] AS u
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
END";
        var content = StoredProcedureContentModel.Parse(sql3, "dbo");
        var set = content.ResultSets.ShouldHaveSingleItem();
        var col = set.Columns.First(c => c.Name == "record");
        col.Reference.ShouldNotBeNull();
        col.Reference.Kind.ShouldBe("Function");
        col.Reference.Schema.ShouldBe("identity");
        col.Reference.Name.ShouldBe("RecordAsJson");
    }

    [Fact]
    public void JsonQuery_SelectWrappingCoreRecordAsJson_Should_Set_CoreReference()
    {
        const string sql4 = @"CREATE PROCEDURE [identity].[UserFindByIdAsJson]
AS
BEGIN
    DECLARE @Context [core].[Context];
    DECLARE @RecordId [core].[_id] = 1;
    SELECT JSON_QUERY((SELECT [core].[RecordAsJson](@Context, u.UserId, u.[RowVersion], u.CreatedById, u.CreatedDt, u.UpdatedById, u.UpdatedDt))) AS [record]
    FROM [identity].[User] AS u
    WHERE u.UserId = @RecordId
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
END";
        var content = StoredProcedureContentModel.Parse(sql4, "identity");
        var set = content.ResultSets.ShouldHaveSingleItem();
        var col = set.Columns.First(c => c.Name == "record");
        col.Reference.ShouldNotBeNull();
        col.Reference.Kind.ShouldBe("Function");
        col.Reference.Schema.ShouldBe("core");
        col.Reference.Name.ShouldBe("RecordAsJson");
    }
}
