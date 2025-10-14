using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RestApi.SpocR; // generated db context
using Shouldly;
using Xunit;

namespace SpocR.IntegrationTests;

/// <summary>
/// Verifies that the generated SpocRDbContext can be resolved via a minimal service provider
/// and successfully opens a connection (SELECT 1) using an environment-provided connection string.
/// This does NOT spin up containers; it relies on SPOCR_TEST_DB or SPOCR_SAMPLE_RESTAPI_DB.
/// If neither is present the test is skipped to avoid false negatives on local dev without DB.
/// </summary>
public class DbContextConnectivityTests
{
    private static string? ResolveConnectionString()
    {
        // Prefer explicit test variable, then sample variable as fallback
        var cs = Environment.GetEnvironmentVariable("SPOCR_TEST_DB")
              ?? Environment.GetEnvironmentVariable("SPOCR_SAMPLE_RESTAPI_DB");
        return string.IsNullOrWhiteSpace(cs) ? null : cs;
    }

    [Fact]
    public async Task SpocRDbContext_Should_OpenConnection_And_Select1()
    {
        var cs = ResolveConnectionString();
        if (cs == null)
        {
            // Skip when no DB available (consistent with local dev scenarios)
            return; // xUnit: returning without asserts effectively skips (could also use Skip parameter)
        }

        var services = new ServiceCollection();
        services.AddSpocRDbContext(o => o.ConnectionString = cs);
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<ISpocRDbContext>();

        await using var conn = await db.OpenConnectionAsync(CancellationToken.None);
        conn.ShouldNotBeNull();
        conn.State.ShouldBe(System.Data.ConnectionState.Open);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        var scalar = await cmd.ExecuteScalarAsync();
        scalar.ShouldNotBeNull();
        Convert.ToInt32(scalar).ShouldBe(1);
    }
}
