using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace SpocR.Tests.Cli;

public class ExperimentalCliTests
{
    [Fact]
    public async Task GenerateDemo_Runs_When_Experimental_Flag_On()
    {
        // Arrange
        Environment.SetEnvironmentVariable("SPOCR_EXPERIMENTAL_CLI", "1");
        try
        {
            // Act
            var exit = await SpocR.Program.RunCliAsync(new[] { "generate-demo", "--mode", "next" });

            // Assert (System.CommandLine returns 0 on success)
            Assert.Equal(0, exit);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPOCR_EXPERIMENTAL_CLI", null);
        }
    }
}
