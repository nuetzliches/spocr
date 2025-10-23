using System.Linq;
using Xunit;
using SpocR.Models; // enthält StoredProcedureContentModel & ResultColumnExpressionKind

namespace SpocR.Tests;

public class JournalMetricsTypingTests
{
    private const string DefaultSchema = "journal"; // simulate journal schema default

    // Minimal reduzierte Version der Originalprozedur zum Testen der Aggregat + Computed Typflag-Erkennung
    // Fokus: Inneres Subselect mit drei SUM(IIF(...)) Aggregaten + äußerer SELECT der Aggregat-Spalten inklusive Addition.
    private const string Sql = @"CREATE PROCEDURE journal.JournalFindMetricsMini @DebitorId int, @CreditorId int AS
BEGIN
    SELECT sub.SummaryGlobal AS 'summary.global',
           sub.DebitorIsOpen + sub.CreditorIsOpen AS 'summary.total',
           sub.DebitorIsOpen AS 'debitor.isOpen',
           sub.CreditorIsOpen AS 'creditor.isOpen',
           @DebitorId AS 'params.debitorId',
           @CreditorId AS 'params.creditorId'
    FROM (
        SELECT
            SUM(IIF(j.CreditorId IS NULL AND j.DebitorId IS NULL, 1, 0)) AS SummaryGlobal,
            SUM(IIF(j.CommunicationTypeId = 11 AND j.JournalStatusId = 1, 1, 0)) AS DebitorIsOpen,
            SUM(IIF(j.CommunicationTypeId = 12 AND j.JournalStatusId = 1, 1, 0)) AS CreditorIsOpen
        FROM journal.Journal j
    ) AS sub
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
END";

    [Fact]
    public void Aggregates_And_Computed_Addition_Should_Set_Flags()
    {
        var model = StoredProcedureContentModel.Parse(Sql, DefaultSchema);
        var rs = Assert.Single(model.ResultSets);
        Assert.True(rs.ReturnsJson);
        // Finde die vier Zielspalten
        var globalCol = rs.Columns.First(c => c.Name == "summary.global");
        var totalCol = rs.Columns.First(c => c.Name == "summary.total");
        var debCol = rs.Columns.First(c => c.Name == "debitor.isOpen");
        var credCol = rs.Columns.First(c => c.Name == "creditor.isOpen");

        // Erwartung: summary.global stammt aus SUM(IIF(...)) -> IsAggregate true & AggregateFunction sum & HasIntegerLiteral true
        Assert.True(globalCol.IsAggregate, "summary.global sollte als Aggregat erkannt werden");
        Assert.Equal("sum", globalCol.AggregateFunction);
        Assert.True(globalCol.HasIntegerLiteral, "SUM(IIF(1,0)) Muster sollte HasIntegerLiteral setzen");

        // Debitor/Creditor Open Flags ebenfalls aggregiert
        Assert.True(debCol.IsAggregate); Assert.Equal("sum", debCol.AggregateFunction);
        Assert.True(credCol.IsAggregate); Assert.Equal("sum", credCol.AggregateFunction);

        // summary.total ist Addition zweier Aggregat-Spalten -> Computed, kein direktes Aggregat-Flag
        Assert.Equal(StoredProcedureContentModel.ResultColumnExpressionKind.Computed, totalCol.ExpressionKind);
        Assert.False(totalCol.IsAggregate, "Addition zweier Aggregat-Spalten selbst kein Aggregat");
        // Integer Literal Flag sollte aufgrund Propagation (beide Operanden integer) gesetzt sein
        Assert.True(totalCol.HasIntegerLiteral, "Computed Addition von zwei int Aggregaten sollte HasIntegerLiteral tragen");
    }
}
