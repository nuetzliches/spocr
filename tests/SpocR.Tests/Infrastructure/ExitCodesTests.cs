using FluentAssertions;
using SpocR.Infrastructure;
using Xunit;

namespace SpocR.Tests.Infrastructure;

public class ExitCodesTests
{
    [Fact]
    public void ExitCodeValues_ShouldMatchDocumentation()
    {
        ExitCodes.Success.Should().Be(0);
        ExitCodes.ValidationError.Should().Be(10);
        ExitCodes.GenerationError.Should().Be(20);
        ExitCodes.DependencyError.Should().Be(30);
        ExitCodes.TestFailure.Should().Be(40);
        ExitCodes.BenchmarkFailure.Should().Be(50);
        ExitCodes.RollbackFailure.Should().Be(60);
        ExitCodes.ConfigurationError.Should().Be(70);
        ExitCodes.InternalError.Should().Be(80);
        ExitCodes.Reserved.Should().Be(99);
    }

    [Fact]
    public void ExitCodes_ShouldBeUnique()
    {
        var values = new[]
        {
            ExitCodes.Success,
            ExitCodes.ValidationError,
            ExitCodes.GenerationError,
            ExitCodes.DependencyError,
            ExitCodes.TestFailure,
            ExitCodes.BenchmarkFailure,
            ExitCodes.RollbackFailure,
            ExitCodes.ConfigurationError,
            ExitCodes.InternalError,
            ExitCodes.Reserved
        };

        values.Should().OnlyHaveUniqueItems();
    }
}
