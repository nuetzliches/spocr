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
        var expected = "Hello World";
        var actual = "Hello World";
        actual.Should().Be(expected);
    }
}
