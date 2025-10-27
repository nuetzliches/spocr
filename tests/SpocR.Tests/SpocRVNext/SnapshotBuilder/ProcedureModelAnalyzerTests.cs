using System.Linq;
using SpocR.SpocRVNext.SnapshotBuilder.Analyzers;
using SpocR.SpocRVNext.SnapshotBuilder.Models;
using Xunit;

namespace SpocR.Tests.SpocRVNext.SnapshotBuilder;

public sealed class ProcedureModelAnalyzerTests
{
    [Fact]
    public void AggregateAnalyzer_sets_flags_for_aliases()
    {
        const string definition = @"
CREATE PROCEDURE dbo.Sample
AS
BEGIN
    SELECT
        AVG(Value) AS AverageValue,
        SUM(Value) AS TotalValue
    FROM dbo.Foo;
END";

        var model = new ProcedureModel();
        var resultSet = new ProcedureResultSet();
        resultSet.Columns.Add(new ProcedureResultColumn { Name = "AverageValue" });
        resultSet.Columns.Add(new ProcedureResultColumn { Name = "TotalValue" });
        model.ResultSets.Add(resultSet);

        ProcedureModelAggregateAnalyzer.Apply(definition, model);

        var avg = Assert.Single(resultSet.Columns, c => c.Name == "AverageValue");
        Assert.True(avg.IsAggregate);
        Assert.Equal("avg", avg.AggregateFunction);

        var sum = Assert.Single(resultSet.Columns, c => c.Name == "TotalValue");
        Assert.True(sum.IsAggregate);
        Assert.Equal("sum", sum.AggregateFunction);
    }

    [Fact]
    public void AggregateAnalyzer_sets_literal_flags()
    {
        const string definition = @"
CREATE PROCEDURE dbo.Sample
AS
BEGIN
    SELECT
        SUM(1) AS IntSum,
        SUM(1.5) AS DecimalSum
    FROM dbo.Foo;
END";

        var model = new ProcedureModel();
        var resultSet = new ProcedureResultSet();
        resultSet.Columns.Add(new ProcedureResultColumn { Name = "IntSum" });
        resultSet.Columns.Add(new ProcedureResultColumn { Name = "DecimalSum" });
        model.ResultSets.Add(resultSet);

        ProcedureModelAggregateAnalyzer.Apply(definition, model);

        var intSum = Assert.Single(resultSet.Columns, c => c.Name == "IntSum");
        Assert.True(intSum.HasIntegerLiteral);
        Assert.False(intSum.HasDecimalLiteral);

        var decimalSum = Assert.Single(resultSet.Columns, c => c.Name == "DecimalSum");
        Assert.True(decimalSum.HasDecimalLiteral);
        Assert.False(decimalSum.HasIntegerLiteral);
    }

    [Fact]
    public void AggregateAnalyzer_handles_string_literal_aliases()
    {
        const string definition = @"
CREATE PROCEDURE dbo.Sample
AS
BEGIN
    SELECT COUNT(*) AS 'agg.countAll'
    FROM dbo.Foo;
END";

        var model = new ProcedureModel();
        var resultSet = new ProcedureResultSet();
        resultSet.Columns.Add(new ProcedureResultColumn { Name = "agg.countAll" });
        model.ResultSets.Add(resultSet);

        ProcedureModelAggregateAnalyzer.Apply(definition, model);

        var column = Assert.Single(resultSet.Columns);
        Assert.True(column.IsAggregate);
        Assert.Equal("count", column.AggregateFunction);
    }

    [Fact]
    public void AggregateAnalyzer_matches_bracketed_aliases()
    {
        const string definition = @"
CREATE PROCEDURE dbo.Sample
AS
BEGIN
    SELECT SUM(Value) AS [Total Value]
    FROM dbo.Foo;
END";

        var model = new ProcedureModel();
        var resultSet = new ProcedureResultSet();
        resultSet.Columns.Add(new ProcedureResultColumn { Name = "[Total Value]" });
        model.ResultSets.Add(resultSet);

        ProcedureModelAggregateAnalyzer.Apply(definition, model);

        var column = Assert.Single(resultSet.Columns);
        Assert.True(column.IsAggregate);
        Assert.Equal("sum", column.AggregateFunction);
    }

    [Fact]
    public void AggregateAnalyzer_resolves_derived_table_aliases()
    {
        const string definition = @"
CREATE PROCEDURE samples.AggregateTypingExtended
AS
BEGIN
    SELECT
        sub.CountAll AS 'agg.countAll',
        sub.CountBig AS 'agg.countBig'
    FROM (
        SELECT
            COUNT(*) AS CountAll,
            COUNT_BIG(*) AS CountBig
        FROM samples.Orders o
    ) AS sub
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
END";

        var model = new ProcedureModel();
        var resultSet = new ProcedureResultSet();
        resultSet.Columns.Add(new ProcedureResultColumn { Name = "agg.countAll" });
        resultSet.Columns.Add(new ProcedureResultColumn { Name = "agg.countBig" });
        model.ResultSets.Add(resultSet);

        ProcedureModelAggregateAnalyzer.Apply(definition, model);

        var countAll = Assert.Single(resultSet.Columns, c => c.Name == "agg.countAll");
        Assert.True(countAll.IsAggregate);
        Assert.Equal("count", countAll.AggregateFunction);

        var countBig = Assert.Single(resultSet.Columns, c => c.Name == "agg.countBig");
        Assert.True(countBig.IsAggregate);
        Assert.Equal("count_big", countBig.AggregateFunction);
    }

    [Fact]
    public void AggregateAnalyzer_returns_summary_for_dotted_aliases()
    {
        const string definition = @"
CREATE PROCEDURE samples.AggregateTypingExtended
AS
BEGIN
    SELECT
        sub.CountAll AS 'agg.countAll'
    FROM (
        SELECT COUNT(*) AS CountAll FROM samples.Orders
    ) AS sub
    FOR JSON PATH;
END";

        var summary = ProcedureModelAggregateAnalyzer.CollectAggregateSummaries(definition);
        Assert.True(summary.TryGetValue("agg.countAll", out var aggregate));
        Assert.True(aggregate.IsAggregate);
        Assert.Equal("count", aggregate.FunctionName);
    }

    [Fact]
    public void AggregateAnalyzer_detects_exists_predicate()
    {
        const string definition = @"
CREATE PROCEDURE samples.ExistsProbe
AS
BEGIN
    SELECT
        CASE WHEN EXISTS(SELECT 1 FROM samples.Orders) THEN 1 ELSE 0 END AS HasRows
    FOR JSON PATH;
END";

        var summary = ProcedureModelAggregateAnalyzer.CollectAggregateSummaries(definition);
        Assert.True(summary.TryGetValue("HasRows", out var aggregate));
        Assert.True(aggregate.IsAggregate);
        Assert.Equal("exists", aggregate.FunctionName);
        Assert.Equal("bit", aggregate.SqlTypeName);
    }

    [Fact]
    public void JsonAnalyzer_sets_result_set_flags_with_root()
    {
        const string definition = @"
CREATE PROCEDURE dbo.UsersAsJson
AS
BEGIN
    SELECT Id, Name FROM dbo.Users FOR JSON PATH, ROOT('payload');
END";

        var model = new ProcedureModel();
        model.ResultSets.Add(new ProcedureResultSet());

        ProcedureModelJsonAnalyzer.Apply(definition, model);

        var resultSet = Assert.Single(model.ResultSets);
        Assert.True(resultSet.ReturnsJson);
        Assert.True(resultSet.ReturnsJsonArray);
        Assert.Equal("payload", resultSet.JsonRootProperty);
    }

    [Fact]
    public void JsonAnalyzer_detects_without_array_wrapper()
    {
        const string definition = @"
CREATE PROCEDURE dbo.UsersAsObject
AS
BEGIN
    SELECT Id, Name FROM dbo.Users FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
END";

        var model = new ProcedureModel();
        model.ResultSets.Add(new ProcedureResultSet());

        ProcedureModelJsonAnalyzer.Apply(definition, model);

        var resultSet = Assert.Single(model.ResultSets);
        Assert.True(resultSet.ReturnsJson);
        Assert.False(resultSet.ReturnsJsonArray);
    }

    [Fact]
    public void JsonAnalyzer_marks_nested_subquery_columns()
    {
        const string definition = @"
CREATE PROCEDURE dbo.UserWithOrderJson
AS
BEGIN
    SELECT (
        SELECT o.Id AS 'order.id'
        FROM dbo.Orders o
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
    ) AS [order], u.Id
    FROM dbo.Users u
    FOR JSON PATH;
END";

        var model = new ProcedureModel();
        var resultSet = new ProcedureResultSet();
        resultSet.Columns.Add(new ProcedureResultColumn { Name = "order" });
        resultSet.Columns.Add(new ProcedureResultColumn { Name = "Id" });
        model.ResultSets.Add(resultSet);

        ProcedureModelJsonAnalyzer.Apply(definition, model);

        var orderColumn = Assert.Single(resultSet.Columns, c => c.Name == "order");
        Assert.True(orderColumn.ReturnsJson);
        Assert.True(orderColumn.IsNestedJson);
        Assert.False(orderColumn.ReturnsJsonArray);
    }

    [Fact]
    public void JsonAnalyzer_normalizes_bracketed_aliases()
    {
        const string definition = @"
CREATE PROCEDURE dbo.OrderJsonBracketAlias
AS
BEGIN
    SELECT (
        SELECT o.Id FROM dbo.Orders o FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
    ) AS [Order Payload]
    FROM dbo.Users u;
END";

        var model = new ProcedureModel();
        var resultSet = new ProcedureResultSet();
        resultSet.Columns.Add(new ProcedureResultColumn { Name = "[Order Payload]" });
        model.ResultSets.Add(resultSet);

        ProcedureModelJsonAnalyzer.Apply(definition, model);

        var column = Assert.Single(resultSet.Columns);
        Assert.True(column.ReturnsJson);
        Assert.True(column.IsNestedJson);
        Assert.False(column.ReturnsJsonArray);
    }

    [Fact]
    public void JsonAnalyzer_marks_json_query_columns()
    {
        const string definition = @"
CREATE PROCEDURE dbo.PayloadFromJsonQuery
AS
BEGIN
    SELECT JSON_QUERY(DataColumn, '$.nested') AS Payload
    FROM dbo.Documents;
END";

        var model = new ProcedureModel();
        var resultSet = new ProcedureResultSet();
        resultSet.Columns.Add(new ProcedureResultColumn { Name = "Payload" });
        model.ResultSets.Add(resultSet);

        ProcedureModelJsonAnalyzer.Apply(definition, model);

        var payload = Assert.Single(resultSet.Columns, c => c.Name == "Payload");
        Assert.True(payload.ReturnsJson);
        Assert.True(payload.IsNestedJson);
        Assert.Null(payload.ReturnsJsonArray);
    }

    [Fact]
    public void ExecAnalyzer_collects_procedure_calls()
    {
        const string definition = @"
CREATE PROCEDURE dbo.CallOther
AS
BEGIN
    EXEC reporting.GenerateReport @Id = 42;
END";

        var model = new ProcedureModel();

        ProcedureModelExecAnalyzer.Apply(definition, model);

        var executed = Assert.Single(model.ExecutedProcedures);
        Assert.Equal("reporting", executed.Schema);
        Assert.Equal("GenerateReport", executed.Name);
    }

    [Fact]
    public void ExecAnalyzer_deduplicates_and_overwrites_previous_results()
    {
        const string definition = @"
CREATE PROCEDURE dbo.CallOther
AS
BEGIN
    EXEC dbo.DoWork;
    EXEC DoWork;
END";

        var model = new ProcedureModel();
        model.ExecutedProcedures.Add(new ProcedureExecutedProcedureCall { Schema = "legacy", Name = "Old" });

        ProcedureModelExecAnalyzer.Apply(definition, model);

        var exec = Assert.Single(model.ExecutedProcedures);
        Assert.Equal("dbo", exec.Schema);
        Assert.Equal("DoWork", exec.Name);

        ProcedureModelExecAnalyzer.Apply("CREATE PROCEDURE dbo.Empty AS SELECT 1;", model);

        Assert.Empty(model.ExecutedProcedures);
    }
}
