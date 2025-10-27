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
