using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using RestApi.SpocR.Samples; // generated procedures & inputs
using Shouldly;
using Xunit;

namespace SpocR.IntegrationTests.Procedures;

/// <summary>
/// Integration test covering a full Create -> Retrieve (CRUD read) roundtrip using vNext generated procedures.
/// Ensures that CreateUserWithOutput returns an output id and that the user can subsequently be retrieved via UserFind.
/// The test is skipped automatically when no database connection string environment variable is present.
/// </summary>
public class CreateUserWithOutputProcedureTests
{
    private static string? ResolveConnectionString()
    {
        var cs = Environment.GetEnvironmentVariable("SPOCR_TEST_DB")
              ?? Environment.GetEnvironmentVariable("SPOCR_SAMPLE_RESTAPI_DB")
              ?? Environment.GetEnvironmentVariable("SPOCR_GENERATOR_DB");
        return string.IsNullOrWhiteSpace(cs) ? null : cs;
    }

    [Fact]
    public async Task Execute_Create_Then_Find_Roundtrip()
    {
        var cs = ResolveConnectionString();
        if (cs == null)
        {
            return; // skip silently when no test DB available
        }

        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        // Unique email each run to avoid uniqueness constraint collisions (if enforced) and permit repeated executions.
        var uniqueSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        var displayName = "TestUser_" + uniqueSuffix;
        var email = $"test_{uniqueSuffix}@example.invalid";

        var createInput = new CreateUserWithOutputInput(displayName, email);
        var createResult = await CreateUserWithOutputProcedure.ExecuteAsync(conn, createInput, CancellationToken.None);

        createResult.Success.ShouldBeTrue();
        createResult.Output.ShouldNotBeNull();
        createResult.Output?.UserId.ShouldNotBeNull();
        // unwrap nullable for Shouldly comparison
        var createdIdNullable = createResult.Output?.UserId;
        createdIdNullable.HasValue.ShouldBeTrue();
        createdIdNullable!.Value.ShouldBeGreaterThan(0);
        createResult.Result.ShouldNotBeNull();
        if (createResult.Result.Count > 0)
        {
            // Some dialects may echo CreatedUserId in result set, assert consistency if present
            var echoed = createResult.Result.First().CreatedUserId;
            if (echoed.HasValue)
                echoed.ShouldBe(createResult.Output?.UserId);
        }

        var userId = createdIdNullable!.Value;

        // Retrieve user via UserFind
        var findInput = new UserFindInput(userId);
        var findResult = await UserFindProcedure.ExecuteAsync(conn, findInput, CancellationToken.None);

        findResult.Success.ShouldBeTrue();
        findResult.Result.ShouldNotBeNull();
        findResult.Result.Count.ShouldBe(1); // Expect exactly one match by primary key
        var user = findResult.Result.First();
        user.UserId.ShouldBe(userId);
        user.DisplayName.ShouldBe(displayName);
        user.Email.ShouldBe(email);
        user.CreatedAt.ShouldBeLessThan(DateTime.UtcNow.AddMinutes(5));
        user.CreatedAt.ShouldBeGreaterThan(DateTime.UtcNow.AddYears(-5));
    }
}
