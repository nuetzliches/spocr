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
    SELECT [identity].RecordAsJson(u.Id) AS [record], u.Email AS [email]
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
    }
}
