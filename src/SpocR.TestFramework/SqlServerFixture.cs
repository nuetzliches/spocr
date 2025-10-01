using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;

namespace SpocR.TestFramework;

/// <summary>
/// Provides SQL Server database fixtures for integration testing using Testcontainers
/// </summary>
public class SqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;
    
    public string ConnectionString { get; private set; } = string.Empty;
    
    public async Task InitializeAsync()
    {
        _container = new MsSqlBuilder()
            .WithPassword("TestPassword123!")
            .WithCleanUp(true)
            .Build();
            
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
        
        // Wait for SQL Server to be ready
        await WaitForSqlServerAsync();
        
        // Create test database schema
        await CreateTestSchemaAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    private async Task WaitForSqlServerAsync()
    {
        const int maxRetries = 30;
        const int delayMs = 1000;
        
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                using var connection = new SqlConnection(ConnectionString);
                await connection.OpenAsync();
                return;
            }
            catch (SqlException)
            {
                if (i == maxRetries - 1)
                    throw;
                await Task.Delay(delayMs);
            }
        }
    }

    private async Task CreateTestSchemaAsync()
    {
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        
        var createSchemaScript = """
            -- Create test schema
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'test')
                EXEC('CREATE SCHEMA test')
            
            -- Create test table
            IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'test.Users') AND type in (N'U'))
            BEGIN
                CREATE TABLE test.Users (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    Name NVARCHAR(100) NOT NULL,
                    Email NVARCHAR(255) NOT NULL,
                    CreatedAt DATETIME2 DEFAULT GETDATE()
                )
            END
            
            -- Create test stored procedure
            IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'test.GetUserById') AND type in (N'P', N'PC'))
                DROP PROCEDURE test.GetUserById
            
            EXEC('
            CREATE PROCEDURE test.GetUserById
                @UserId INT
            AS
            BEGIN
                SELECT Id, Name, Email, CreatedAt 
                FROM test.Users 
                WHERE Id = @UserId
            END')
            
            -- Insert test data
            IF NOT EXISTS (SELECT * FROM test.Users)
            BEGIN
                INSERT INTO test.Users (Name, Email) VALUES 
                    (''John Doe'', ''john.doe@example.com''),
                    (''Jane Smith'', ''jane.smith@example.com'')
            END
            """;

        using var command = new SqlCommand(createSchemaScript, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ExecuteScalarAsync<T>(string sql, params SqlParameter[] parameters)
    {
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        
        var result = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(result!, typeof(T));
    }

    public async Task<int> ExecuteNonQueryAsync(string sql, params SqlParameter[] parameters)
    {
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        
        return await command.ExecuteNonQueryAsync();
    }
}