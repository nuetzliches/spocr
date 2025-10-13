using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using McMaster.Extensions.CommandLineUtils;
using SpocR.SpocRVNext.Generators;
using SpocR.Extensions;
using SpocR.CodeGenerators;
using SpocR.Commands;
using Xunit;

namespace SpocR.Tests.SpocRVNext.Generators;

public class DbContextGeneratorTests
{
    [Fact]
    public async Task DbContextGenerator_Skips_In_Legacy_Mode()
    {
        Environment.SetEnvironmentVariable("SPOCR_GENERATOR_MODE", "legacy");
        var gen = CreateGenerator();
        await gen.GenerateAsync(isDryRun: false);
        Assert.False(File.Exists(Path.Combine(GetOutputRoot(), "SpocR", "SpocRDbContext.cs")));
    }

    [Fact]
    public async Task DbContextGenerator_Generates_In_Dual_Mode()
    {
        Environment.SetEnvironmentVariable("SPOCR_GENERATOR_MODE", "dual");
        try
        {
            var gen = CreateGenerator();
            await gen.GenerateAsync(isDryRun: false);
            Assert.True(File.Exists(Path.Combine(GetOutputRoot(), "SpocR", "SpocRDbContext.cs")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPOCR_GENERATOR_MODE", null);
        }
    }

    private static DbContextGenerator CreateGenerator()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConsole>(PhysicalConsole.Singleton);
        services.AddSingleton(new CommandOptions());
        services.AddSpocR();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<DbContextGenerator>();
    }

    private static string GetOutputRoot()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConsole>(PhysicalConsole.Singleton);
        services.AddSingleton(new CommandOptions());
        services.AddSpocR();
        var provider = services.BuildServiceProvider();
        var outputService = provider.GetRequiredService<SpocR.Services.OutputService>();
        var dir = outputService.GetOutputRootDir().FullName;
        // DbContext artifacts now reside directly under root/SpocR
        return Path.Combine(dir, "SpocR");
    }
}
