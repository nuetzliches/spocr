using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using RestApi.SpocR; // ISpocRDbContext + options/registration
using RestApi.SpocR.Samples; // generated UserListExtensions & result types
using Shouldly;
using Xunit;

namespace SpocR.IntegrationTests.Procedures;

/// <summary>
/// Verifies invocation of the generated DbContext extension method <c>UserListAsync</c>.
/// This covers the end-to-end path: DI registration -> db.OpenConnectionAsync -> ProcedureExecutor.
/// Skips silently when no connection string environment variable is present.
/// </summary>
public class UserListDbContextExtensionInvocationTests
{
    private static string? ResolveConnectionString()
    {
        var cs = Environment.GetEnvironmentVariable("SPOCR_TEST_DB")
              ?? Environment.GetEnvironmentVariable("SPOCR_SAMPLE_RESTAPI_DB")
              ?? Environment.GetEnvironmentVariable("SPOCR_GENERATOR_DB"); // fallback to generator DB if set
        return string.IsNullOrWhiteSpace(cs) ? null : cs;
    }

    [Fact]
    public async Task UserListAsync_Extension_Should_Return_Users()
    {
        var cs = ResolveConnectionString();
        if (cs == null)
        {
            // Skip when no DB provided (bridge phase environments may omit sample DB)
            return;
        }

        // Arrange: DI container with SpocRDbContext
        var services = new ServiceCollection();
        services.AddSpocRDbContext(opts =>
        {
            opts.ConnectionString = cs;
            opts.CommandTimeout = 30;
        });
        var provider = services.BuildServiceProvider(validateScopes: true);
        var db = provider.GetRequiredService<ISpocRDbContext>();

        // Act
        var result = await db.UserListAsync(CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Result.ShouldNotBeNull();
        result.Result.Count.ShouldBeGreaterThan(0);
        var first = result.Result.First();
        first.UserId.ShouldBeGreaterThan(0);
        first.DisplayName.ShouldNotBeNull();
        first.DisplayName.Trim().Length.ShouldBeGreaterThan(0);
    }
}
