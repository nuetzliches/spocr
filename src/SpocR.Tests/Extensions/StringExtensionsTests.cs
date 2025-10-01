using SpocR.Extensions;

namespace SpocR.Tests.Extensions;

public class StringExtensionsTests
{
    [Theory]
    [InlineData("hello world", "HelloWorld")]
    [InlineData("test_string", "TestString")]
    [InlineData("already-camelCase", "AlreadyCamelCase")]
    [InlineData("", "")]
    [InlineData("a", "A")]
    public void ToPascalCase_ShouldConvertCorrectly(string input, string expected)
    {
        // Act
        var result = input.ToPascalCase();

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("HelloWorld", "helloWorld")]
    [InlineData("TestString", "testString")]
    [InlineData("alreadyCamelCase", "alreadyCamelCase")]
    [InlineData("", "")]
    [InlineData("A", "a")]
    public void ToCamelCase_ShouldConvertCorrectly(string input, string expected)
    {
        // Act
        var result = input.ToCamelCase();

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsNullOrWhiteSpace_ShouldReturnTrue_ForNullOrWhitespace(string input)
    {
        // Act
        var result = input.IsNullOrWhiteSpace();

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("  hello  ")]
    [InlineData("123")]
    public void IsNullOrWhiteSpace_ShouldReturnFalse_ForValidStrings(string input)
    {
        // Act
        var result = input.IsNullOrWhiteSpace();

        // Assert
        result.Should().BeFalse();
    }
}