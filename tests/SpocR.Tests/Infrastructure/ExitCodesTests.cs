using Shouldly;
using SpocR.Infrastructure;
using Xunit;

namespace SpocR.Tests.Infrastructure;

public class ExitCodesTests
{
    [Fact]
    public void ExitCodeValues_ShouldMatchDocumentation()
    {
        ExitCodes.Success.ShouldBe(0);
        ExitCodes.ValidationError.ShouldBe(10);
        ExitCodes.GenerationError.ShouldBe(20);
        ExitCodes.DependencyError.ShouldBe(30);
        ExitCodes.TestFailure.ShouldBe(40);
        ExitCodes.BenchmarkFailure.ShouldBe(50);
        ExitCodes.RollbackFailure.ShouldBe(60);
        ExitCodes.ConfigurationError.ShouldBe(70);
        ExitCodes.InternalError.ShouldBe(80);
        ExitCodes.Reserved.ShouldBe(99);
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

        values.ShouldBe(values.Distinct());
    }
}
