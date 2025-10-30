using System.Linq;
using Xunit;
using SpocR.SpocRVNext.Models; // StoredProcedureContentModel

namespace SpocR.Tests;

// Tests für erweiterte Aggregat Typinferenz: COUNT, COUNT_BIG, AVG, EXISTS, SUM.
public class AggregateTypingExtendedTests
{
    private const string DefaultSchema = "samples";

    // Derived-Table Pattern (inneres Subselect) analog JournalMetrics für stabile Aggregat-Propagation.
    private const string Sql = @"CREATE PROCEDURE samples.AggregateTypingExtended AS
BEGIN
    SELECT
        sub.CountAll AS 'agg.countAll',
        sub.CountBig AS 'agg.countBig',
        sub.AvgAmount AS 'agg.avgAmount',
        sub.HasPositive AS 'agg.hasPositive',
        sub.SumAmount AS 'agg.sumAmount'
    FROM (
        SELECT
            COUNT(*) AS CountAll,
            COUNT_BIG(*) AS CountBig,
            AVG(o.Amount) AS AvgAmount,
            CASE WHEN EXISTS(SELECT 1 FROM samples.Orders o2 WHERE o2.Amount > 0) THEN 1 ELSE 0 END AS HasPositive,
            SUM(o.Amount) AS SumAmount
        FROM samples.Orders o
    ) AS sub
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
END";

    [Fact]
    public void Aggregates_Should_Have_Inferred_Types()
    {
        var model = StoredProcedureContentModel.Parse(Sql, DefaultSchema);

        var rs = Assert.Single(model.ResultSets);
        Assert.True(rs.ReturnsJson);

        var countAll = rs.Columns.First(c => c.Name == "agg.countAll");
        var countBig = rs.Columns.First(c => c.Name == "agg.countBig");
        var avgAmount = rs.Columns.First(c => c.Name == "agg.avgAmount");
        var hasPositive = rs.Columns.First(c => c.Name == "agg.hasPositive");
        var sumAmount = rs.Columns.First(c => c.Name == "agg.sumAmount");

        Assert.True(countAll.IsAggregate, "agg.countAll sollte als COUNT Aggregat erkannt werden");
        Assert.Equal("count", countAll.AggregateFunction);
        Assert.Equal("int", countAll.SqlTypeName);

        Assert.True(countBig.IsAggregate, "agg.countBig sollte als COUNT_BIG Aggregat erkannt werden");
        Assert.Equal("count_big", countBig.AggregateFunction);
        Assert.Equal("bigint", countBig.SqlTypeName);

        Assert.True(avgAmount.IsAggregate, "agg.avgAmount sollte als AVG Aggregat erkannt werden");
        Assert.Equal("avg", avgAmount.AggregateFunction);
        Assert.Equal("decimal(18,2)", avgAmount.SqlTypeName);

        Assert.True(hasPositive.IsAggregate, "agg.hasPositive sollte als EXISTS Aggregat erkannt werden");
        Assert.Equal("exists", hasPositive.AggregateFunction);
        Assert.Equal("bit", hasPositive.SqlTypeName);

        Assert.True(sumAmount.IsAggregate, "agg.sumAmount sollte als SUM Aggregat erkannt werden");
        Assert.Equal("sum", sumAmount.AggregateFunction);
        Assert.NotNull(sumAmount.SqlTypeName); // kann int oder decimal Fallback sein
    }

    // Erweiterte Tests für JSON_QUERY Wrapper & STRING_AGG werden separat ergänzt.
}
