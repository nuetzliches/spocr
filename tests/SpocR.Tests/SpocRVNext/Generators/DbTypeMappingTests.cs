using SpocR.SpocRVNext.Generators;
using Xunit;
using System.Reflection;

namespace SpocR.Tests.SpocRVNext.Generators;

public class DbTypeMappingTests
{
    private static string Invoke(string sqlType)
    {
        var m = typeof(ProceduresGenerator).GetMethod("MapDbType", BindingFlags.NonPublic | BindingFlags.Static);
        return (string)m!.Invoke(null, new object?[] { sqlType })!;
    }

    [Theory]
    [InlineData("int", "System.Data.DbType.Int32")]
    [InlineData("bigint", "System.Data.DbType.Int64")]
    [InlineData("smallint", "System.Data.DbType.Int16")]
    [InlineData("tinyint", "System.Data.DbType.Byte")]
    [InlineData("bit", "System.Data.DbType.Boolean")]
    [InlineData("decimal(18,2)", "System.Data.DbType.Decimal")]
    [InlineData("numeric(10,0)", "System.Data.DbType.Decimal")]
    [InlineData("money", "System.Data.DbType.Decimal")]
    [InlineData("float", "System.Data.DbType.Double")]
    [InlineData("real", "System.Data.DbType.Single")]
    [InlineData("date", "System.Data.DbType.DateTime2")]
    [InlineData("datetime2", "System.Data.DbType.DateTime2")]
    [InlineData("datetime", "System.Data.DbType.DateTime2")]
    [InlineData("smalldatetime", "System.Data.DbType.DateTime2")]
    [InlineData("datetimeoffset", "System.Data.DbType.DateTime2")]
    [InlineData("time", "System.Data.DbType.DateTime2")]
    [InlineData("uniqueidentifier", "System.Data.DbType.Guid")]
    [InlineData("varbinary(max)", "System.Data.DbType.Binary")]
    [InlineData("binary(50)", "System.Data.DbType.Binary")]
    [InlineData("image", "System.Data.DbType.Binary")]
    [InlineData("xml", "System.Data.DbType.Xml")]
    [InlineData("nvarchar(50)", "System.Data.DbType.String")]
    [InlineData("varchar(10)", "System.Data.DbType.String")]
    [InlineData("nchar(5)", "System.Data.DbType.String")]
    [InlineData("char(5)", "System.Data.DbType.String")]
    [InlineData("text", "System.Data.DbType.String")]
    [InlineData("ntext", "System.Data.DbType.String")]
    public void Maps_Known_Types(string input, string expected)
    {
        Assert.Equal(expected, Invoke(input));
    }

    [Fact]
    public void Fallback_To_String_For_Unknown()
    {
        Assert.Equal("System.Data.DbType.String", Invoke("geography"));
    }
}
