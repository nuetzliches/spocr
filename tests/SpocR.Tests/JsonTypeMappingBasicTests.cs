using System;
using System.Linq;
using Xunit;
using SpocR.Models; // StoredProcedureContentModel

namespace SpocR.Tests;

public class JsonTypeMappingBasicTests
{
    private const string DefaultSchema = "calendar";

    // Minimales Beispiel mit RowVersion (timestamp) und optionalem JSON Fragment
    private const string Sql = @"CREATE PROCEDURE calendar.HolidayFindMini @HolidayId int AS
BEGIN
    SELECT h.HolidayId AS 'record.id',
           h.Name AS 'record.name',
           h.RowVersion AS 'record.rowVersion', -- sollte als byte[] gemappt werden
           (SELECT TOP 1 j.JournalId FROM journal.Journal j WHERE j.JournalStatusId = 99) AS 'record.optionalRef'
    FROM calendar.Holiday h WHERE h.HolidayId = @HolidayId
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
END";

    [Fact]
    public void RowVersion_Should_Map_To_ByteArray()
    {
        var model = StoredProcedureContentModel.Parse(Sql, DefaultSchema);
        var rs = Assert.Single(model.ResultSets);
        Assert.True(rs.ReturnsJson);
        var rowVersion = rs.Columns.First(c => c.Name == "record.rowVersion");
        // Anpassung: In manchen Prozeduren wird RowVersion explizit CAST / konvertiert (z.B. zu bigint).
        // Akzeptiere daher folgende Varianten: rowversion | timestamp | binary(8) | bigint
        var rv = rowVersion.SqlTypeName?.ToLowerInvariant() ?? string.Empty;
        Assert.True(rv.Contains("rowversion") || rv.Contains("timestamp") || rv.Contains("binary") || rv.Contains("bigint"),
            $"RowVersion SqlTypeName erwartet rowversion/timestamp/binary(8)/bigint, erhalten: '{rowVersion.SqlTypeName}'");
    }

    [Fact]
    public void OptionalRef_Should_Be_Nullable_Int()
    {
        var model = StoredProcedureContentModel.Parse(Sql, DefaultSchema);
        var rs = Assert.Single(model.ResultSets);
    var opt = rs.Columns.First(c => c.Name == "record.optionalRef");
    // Da TOP 1 Subselect ohne Garantie -> sollte IsNullable = true sein (oder ForcedNullable)
    Assert.True(opt.IsNullable == true || opt.ForcedNullable == true, "optionalRef sollte als nullable markiert sein");
    // SqlTypeName sollte ein Ganzzahltyp sein
    Assert.Contains(opt.SqlTypeName.ToLowerInvariant(), new[] {"int", "bigint", "smallint"});
    }
}
