using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace SpocR.SpocRVNext.Data;

/// <summary>
/// Minimal placeholder for the upcoming next-generation SpocR database interaction context.
/// Intentionally lean: will be extended with generated partial members.
/// </summary>
public partial class SpocRDbContext
{
    private readonly string _connectionString;

    public SpocRDbContext(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Creates and opens a new DbConnection (synchronous open acceptable for initial scaffold).
    /// </summary>
    public DbConnection OpenConnection()
    {
        var conn = new SqlConnection(_connectionString);
        conn.Open();
        return conn;
    }
}
