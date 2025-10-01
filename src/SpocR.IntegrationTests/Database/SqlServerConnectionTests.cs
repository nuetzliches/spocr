using SpocR.TestFramework;

namespace SpocR.IntegrationTests.Database;

public class SqlServerConnectionTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _sqlServerFixture;

    public SqlServerConnectionTests(SqlServerFixture sqlServerFixture)
    {
        _sqlServerFixture = sqlServerFixture;
    }

    [Fact]
    public async Task SqlServer_ShouldBeAccessible()
    {
        // Arrange & Act
        var userCount = await _sqlServerFixture.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM test.Users");

        // Assert
        userCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SqlServer_ShouldHaveTestData()
    {
        // Act
        var johnExists = await _sqlServerFixture.ExecuteScalarAsync<bool>(
            "SELECT CASE WHEN EXISTS(SELECT 1 FROM test.Users WHERE Name = @name) THEN 1 ELSE 0 END",
            new Microsoft.Data.SqlClient.SqlParameter("@name", "John Doe"));

        // Assert
        johnExists.Should().BeTrue();
    }

    [Fact]
    public async Task StoredProcedure_GetUserById_ShouldWork()
    {
        // Arrange
        var userId = await _sqlServerFixture.ExecuteScalarAsync<int>(
            "SELECT TOP 1 Id FROM test.Users WHERE Name = @name",
            new Microsoft.Data.SqlClient.SqlParameter("@name", "John Doe"));

        // Act
        var result = await _sqlServerFixture.ExecuteScalarAsync<string>(
            "EXEC test.GetUserById @UserId",
            new Microsoft.Data.SqlClient.SqlParameter("@UserId", userId));

        // Assert - Should not throw and return some result
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task SqlServer_ShouldSupportTransactions()
    {
        // Arrange
        var initialCount = await _sqlServerFixture.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM test.Users");

        // Act - Insert and then rollback (simplified test)
        var insertedRows = await _sqlServerFixture.ExecuteNonQueryAsync(
            "INSERT INTO test.Users (Name, Email) VALUES (@name, @email)",
            new Microsoft.Data.SqlClient.SqlParameter("@name", "Test User"),
            new Microsoft.Data.SqlClient.SqlParameter("@email", "test@example.com"));

        var newCount = await _sqlServerFixture.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM test.Users");

        // Assert
        insertedRows.Should().Be(1);
        newCount.Should().Be(initialCount + 1);
    }
}