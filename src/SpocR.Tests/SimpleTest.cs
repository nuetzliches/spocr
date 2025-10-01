using Xunit;
using FluentAssertions;

namespace SpocR.Tests;

/// <summary>
/// Einfacher Test um zu validieren, dass das Test-Framework funktioniert
/// </summary>
public class SimpleTest
{
    [Fact]
    public void SimpleAssertion_ShouldWork()
    {
        // Arrange
        var expected = "Hello World";
        
        // Act
        var actual = "Hello World";
        
        // Assert
        actual.Should().Be(expected);
    }
    
    [Theory]
    [InlineData(1, 2, 3)]
    [InlineData(5, 5, 10)]
    [InlineData(-1, 1, 0)]
    public void Addition_ShouldReturnCorrectSum(int a, int b, int expected)
    {
        // Act
        var result = a + b;
        
        // Assert
        result.Should().Be(expected);
    }
}