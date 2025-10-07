using Xunit;
using Shouldly;

namespace SpocR.Tests;

/// <summary>
/// Simple test to validate that the test framework wiring works
/// </summary>
public class SimpleTest
{
    [Fact]
    public void SimpleAssertion_ShouldWork()
    {
        var expected = "Hello World";
        var actual = "Hello World";
        actual.ShouldBe(expected);
    }
}
