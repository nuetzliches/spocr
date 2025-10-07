using Shouldly;
using SpocR.TestFramework;
using Xunit;

namespace SpocR.IntegrationTests;

/// <summary>
/// Basic integration test to verify the test framework is working
/// </summary>
public class BasicIntegrationTests : SpocRTestBase
{
    [Fact]
    public void TestFramework_ShouldBeAvailable()
    {
        // Arrange & Act
        var connectionString = GetTestConnectionString();

        // Assert
        connectionString.ShouldNotBeNull();
        connectionString.ShouldContain("SpocRTest");
    }

    [Fact]
    public void SpocRValidator_ShouldValidateEmptyCode()
    {
        // Arrange
        var emptyCode = string.Empty;

        // Act
        var isValid = SpocRValidator.ValidateGeneratedCodeSyntax(emptyCode, out var errors);

        // Assert
        isValid.ShouldBeFalse();
        errors.ShouldNotBeEmpty();
        errors.ShouldContain("Generated code is empty or null");
    }
}