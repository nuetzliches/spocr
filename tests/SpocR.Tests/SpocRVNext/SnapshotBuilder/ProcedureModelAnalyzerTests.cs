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
}
