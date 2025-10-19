using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using RestApi.SpocR.Samples; // generated UserListProcedure
using Shouldly;
using Xunit;

namespace SpocR.IntegrationTests.Procedures;

/// <summary>
/// Integration test for the vNext generated UserListProcedure. Ensures at least one row is returned
/// and basic field integrity is present. Skips automatically if no connection string is available
/// via environment (SPOCR_TEST_DB or SPOCR_SAMPLE_RESTAPI_DB).
/// </summary>
public class UserListProcedureTests
{
    private static string? ResolveConnectionString()
    {
        var cs = Environment.GetEnvironmentVariable("SPOCR_TEST_DB")
              ?? Environment.GetEnvironmentVariable("SPOCR_SAMPLE_RESTAPI_DB")
              ?? Environment.GetEnvironmentVariable("SPOCR_GENERATOR_DB"); // fallback to generator DB if set
        return string.IsNullOrWhiteSpace(cs) ? null : cs;
    }

    [Fact]
    public async Task ExecuteAsync_Should_Return_Users()
    {
        var cs = ResolveConnectionString();
        if (cs == null)
        {
            // Skip when no DB provided
            return;
        }

        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        var result = await UserListProcedure.ExecuteAsync(conn, CancellationToken.None);
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Result.ShouldNotBeNull();
        result.Result.Count.ShouldBeGreaterThan(0); // At least one user expected in sample seed

        var first = result.Result.First();
        first.UserId.ShouldBeGreaterThan(0);
        first.Email.ShouldNotBeNull();
        first.Email.Trim().Length.ShouldBeGreaterThan(0);
        first.DisplayName.ShouldNotBeNull();
        first.DisplayName.Trim().Length.ShouldBeGreaterThan(0);
        // Bio may be optional; CreatedAt should be a reasonable past or near-present timestamp
        first.CreatedAt.ShouldBeLessThan(DateTime.UtcNow.AddMinutes(5));
        first.CreatedAt.ShouldBeGreaterThan(DateTime.UtcNow.AddYears(-5));
    }
}
