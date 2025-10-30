using System.Linq;
using SpocR.SpocRVNext.Models;
using Xunit;

namespace SpocR.Tests.Cli;

public class ExecAppendNormalizationTests
{
    [Fact]
    public void ProcedureWithOwnJsonAndExec_AppendsExecutedProcResultSets()
    {
        // Caller has its own FOR JSON result set plus an EXEC of another proc.
        var executed = @"CREATE PROCEDURE [dbo].[InnerProc]
AS
BEGIN
    SELECT 1 AS InnerId, 'A' AS InnerName FOR JSON PATH;
END";
        var caller = @"CREATE PROCEDURE [dbo].[OuterProc]
AS
BEGIN
    SELECT 42 AS OuterId FOR JSON PATH;
    EXEC dbo.InnerProc;
END";

        var innerModel = StoredProcedureContentModel.Parse(executed, "dbo");
        var outerModel = StoredProcedureContentModel.Parse(caller, "dbo");

        // Simulate normalization append logic the SchemaManager would perform:
        // If outer has its own sets and exactly one executed proc, append inner sets.
        Assert.Single(outerModel.ResultSets);
        Assert.Single(innerModel.ResultSets);
        Assert.Single(outerModel.ExecutedProcedures);

        if (outerModel.ExecutedProcedures.Count == 1)
        {
            var executedCall = outerModel.ExecutedProcedures[0];
            // Recreate new ResultSet instances with ExecSource metadata (mimicking SchemaManager logic)
            var appended = innerModel.ResultSets
                .Select(rs => new StoredProcedureContentModel.ResultSet
                {
                    ReturnsJson = rs.ReturnsJson,
                    ReturnsJsonArray = rs.ReturnsJsonArray,
                    JsonRootProperty = rs.JsonRootProperty,
                    Columns = rs.Columns,
                    HasSelectStar = rs.HasSelectStar,
                    ExecSourceSchemaName = executedCall.Schema,
                    ExecSourceProcedureName = executedCall.Name
                })
                .ToList();

            // Because ResultSets is IReadOnlyList in model, we cannot AddRange directly; simulate final list the normalizer would produce.
            var combined = outerModel.ResultSets.Concat(appended).ToArray();

            // Assert on expected combined state without mutating (immutability of model)
            Assert.Equal(2, combined.Length);
            Assert.Null(combined[0].ExecSourceProcedureName);
            Assert.Equal("InnerProc", combined[1].ExecSourceProcedureName);
            Assert.Equal("dbo", combined[1].ExecSourceSchemaName);
            return; // success path
        }
        // Fallback should not happen for this test scenario
        Assert.Fail("Expected single executed procedure for append simulation.");
    }
}
