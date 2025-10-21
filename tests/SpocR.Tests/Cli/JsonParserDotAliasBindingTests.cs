using System.Linq;
using Shouldly;
using Xunit;
using SpocR.Models;
using static SpocR.Models.StoredProcedureContentModel; // for ResultColumnExpressionKind

namespace SpocR.Tests.Cli;

/// <summary>
/// Verifies that dotted JSON alias columns obtain AST source bindings (SourceSchema/SourceTable/SourceColumn)
/// without heuristic type resolution. Ensures we did not re-introduce fallback logic after heuristic removal.
/// </summary>
public class JsonParserDotAliasBindingTests
{
    private const string Sql = @"CREATE PROCEDURE [dbo].[TestProc]
AS
BEGIN
    SELECT sa.AccountId AS [sourceAccount.accountId],
           sat.TypeId AS [sourceAccount.type.typeId]
    FROM [finance].[Account] AS sa
    INNER JOIN [finance].[Account_Type] AS sat ON sat.TypeId = sa.TypeId
    FOR JSON PATH;
END";

    [Fact]
    public void DottedAliases_Should_Have_SourceBinding_But_NoSqlType_FromParser()
    {
        var content = StoredProcedureContentModel.Parse(Sql, "dbo");
        content.ResultSets.ShouldNotBeNull();
        content.ResultSets.Count.ShouldBe(1);
        var set = content.ResultSets[0];
        set.ReturnsJson.ShouldBeTrue();
        set.Columns.Count.ShouldBeGreaterThan(1);

        var accountId = set.Columns.FirstOrDefault(c => c.Name == "sourceAccount.accountId");
        var typeId = set.Columns.FirstOrDefault(c => c.Name == "sourceAccount.type.typeId");
        accountId.ShouldNotBeNull();
        typeId.ShouldNotBeNull();

        // AST Source binding should be present
        accountId.SourceSchema.ShouldNotBeNull();
        accountId.SourceTable.ShouldNotBeNull();
        accountId.SourceColumn.ShouldBe("AccountId");
        typeId.SourceSchema.ShouldNotBeNull();
        typeId.SourceTable.ShouldNotBeNull();
        typeId.SourceColumn.ShouldBe("TypeId");

        // Parser stage must not assign SqlTypeName (enricher would do it later with DB metadata)
        accountId.SqlTypeName.ShouldBeNull();
        typeId.SqlTypeName.ShouldBeNull();

        // No heuristic ambiguity flags expected for straightforward alias bindings
        accountId.IsAmbiguous.ShouldBeNull();
        typeId.IsAmbiguous.ShouldBeNull();
    }

    [Fact]
    public void JsonQueryAlias_Should_Not_Have_SourceBinding()
    {
        const string sql = @"CREATE PROCEDURE [dbo].[JsonQueryProc]
AS
BEGIN
    SELECT JSON_QUERY(sa.JsonPayload, '$.nested') AS [payload.nested]
    FROM [finance].[Account] AS sa
    FOR JSON PATH;
END";
        var content = StoredProcedureContentModel.Parse(sql, "dbo");
        content.ResultSets.ShouldNotBeNull();
        content.ResultSets.Count.ShouldBe(1);
        var set = content.ResultSets[0];
        var col = set.Columns.FirstOrDefault(c => c.Name == "payload.nested");
        col.ShouldNotBeNull();
        col.ExpressionKind.ShouldBe(ResultColumnExpressionKind.JsonQuery);
        // JSON_QUERY should not produce direct source binding (source comes from function parameter, not column ref for alias itself)
        col.SourceSchema.ShouldBeNull();
        col.SourceTable.ShouldBeNull();
        col.SourceColumn.ShouldBeNull();
        col.SqlTypeName.ShouldBeNull();
    }

    [Fact]
    public void SingleQuotedDottedAlias_Should_Bind_SourceColumn()
    {
        const string sql2 = @"CREATE PROCEDURE [dbo].[QuotedAliasProc]
AS
BEGIN
    SELECT u.Email AS 'user.mail'
    FROM [samples].[Users] AS u
    FOR JSON PATH;
END";
        var content = StoredProcedureContentModel.Parse(sql2, "dbo");
        content.ResultSets.ShouldNotBeNull();
        content.ResultSets.Count.ShouldBe(1);
        var set = content.ResultSets[0];
        var col = set.Columns.FirstOrDefault(c => c.Name == "user.mail");
        col.ShouldNotBeNull();
        // Binding expected
        col.SourceSchema.ShouldNotBeNull();
        col.SourceTable.ShouldNotBeNull();
        col.SourceColumn.ShouldBe("Email");
    }

    [Fact]
    public void ComplexJoinAndApply_Should_Bind_DottedAliases()
    {
        const string sql = @"CREATE PROCEDURE [dbo].[ComplexProc]
AS
BEGIN
    SELECT i.InitiationId AS 'initiationId',
           sa.AccountId AS 'sourceAccount.accountId',
           sat.TypeId AS 'sourceAccount.type.typeId'
    FROM [samples].[Initiation] AS i
        INNER JOIN [samples].[Account] AS sa ON sa.AccountId = i.SourceAccountId
        INNER JOIN [samples].[Account_Type] AS sat ON sat.TypeId = sa.TypeId
        OUTER APPLY (
            SELECT SUM(it.Amount) AS Amount
            FROM [samples].[InitiationTransaction] AS it
            WHERE it.InitiationId = i.InitiationId
        ) AS it_sum
    WHERE EXISTS(SELECT TOP 1 1 FROM [samples].[InitiationTransaction] AS it2 WHERE it2.InitiationId = i.InitiationId)
    FOR JSON PATH;
END";
        var content = StoredProcedureContentModel.Parse(sql, "dbo");
        var set = content.ResultSets.ShouldHaveSingleItem();
        var saId = set.Columns.FirstOrDefault(c => c.Name == "sourceAccount.accountId");
        saId.ShouldNotBeNull();
        saId.SourceSchema.ShouldNotBeNull();
        saId.SourceTable.ShouldNotBeNull();
        saId.SourceColumn.ShouldBe("AccountId");
    }
}
