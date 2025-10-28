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
    const string DemoConnection = @"Server=(localdb)\MSSQLLocalDB;Database=SpocR_Demo;Integrated Security=true;";
    var previousExperimental = Environment.GetEnvironmentVariable("SPOCR_EXPERIMENTAL_CLI");
    var previousGeneratorDb = Environment.GetEnvironmentVariable("SPOCR_GENERATOR_DB");
    Environment.SetEnvironmentVariable("SPOCR_EXPERIMENTAL_CLI", "1");
    Environment.SetEnvironmentVariable("SPOCR_GENERATOR_DB", DemoConnection);
        try
        {
            // Ensure .env exists with marker so namespace prompt stays silent
            var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
            if (!File.Exists(envPath))
            {
                File.WriteAllText(envPath, $"SPOCR_GENERATOR_DB={DemoConnection}\n# SPOCR_NAMESPACE placeholder\n");
            }
            else
            {
                var content = File.ReadAllText(envPath);
                if (!content.Contains("SPOCR_GENERATOR_DB", StringComparison.OrdinalIgnoreCase))
                {
                    File.AppendAllText(envPath, $"SPOCR_GENERATOR_DB={DemoConnection}\n");
                }
            }
            // Act
            var exit = await SpocR.Program.RunCliAsync(new[] { "generate-demo" });

            // Assert (System.CommandLine returns 0 on success)
            Assert.Equal(0, exit);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPOCR_EXPERIMENTAL_CLI", previousExperimental);
            Environment.SetEnvironmentVariable("SPOCR_GENERATOR_DB", previousGeneratorDb);
        }
    }
}
