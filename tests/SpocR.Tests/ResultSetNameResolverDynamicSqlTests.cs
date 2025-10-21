using SpocR.SpocRVNext.Metadata;
using Xunit;

namespace SpocR.Tests;

public class ResultSetNameResolverDynamicSqlTests
{
    [Theory]
    [InlineData("CREATE PROCEDURE dbo.DynamicExec AS BEGIN DECLARE @sql NVARCHAR(MAX)='SELECT * FROM dbo.Users'; EXEC(@sql); END")] // EXEC(@
    [InlineData("CREATE PROCEDURE dbo.DynamicSpExec AS BEGIN DECLARE @sql NVARCHAR(MAX)='SELECT * FROM dbo.Users'; EXEC sp_executesql @sql; END")] // sp_executesql
    [InlineData("CREATE PROCEDURE dbo.DynamicWithExecParen AS BEGIN DECLARE @p NVARCHAR(MAX)='SELECT 1'; EXEC (@p); END")] // EXEC ( @
    public void TryResolve_DynamicSqlPatterns_ReturnsNull(string sql)
    {
        var name = ResultSetNameResolver.TryResolve(0, sql);
        Assert.Null(name); // dynamic patterns must disable naming suggestion
    }

    [Fact]
    public void TryResolve_StaticSql_ReturnsFirstBaseTable()
    {
        const string sql = "CREATE PROCEDURE dbo.GetUsers AS BEGIN SELECT * FROM dbo.Users; SELECT * FROM dbo.Other; END";
        var name = ResultSetNameResolver.TryResolve(0, sql);
        Assert.Equal("Users", name); // first base table
    }
}
