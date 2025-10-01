using FluentAssertions;
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
        connectionString.Should().NotBeNullOrEmpty();
        connectionString.Should().Contain("SpocRTest");
    }

    [Fact]
    public void SpocRValidator_ShouldValidateEmptyCode()
    {
        // Arrange
        var emptyCode = string.Empty;

        // Act
        var isValid = SpocRValidator.ValidateGeneratedCodeSyntax(emptyCode, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().NotBeEmpty();
        errors.Should().Contain("Generated code is empty or null");
    }
}