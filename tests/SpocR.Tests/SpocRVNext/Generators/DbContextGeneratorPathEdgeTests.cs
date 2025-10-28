using System.IO;
using System.Threading.Tasks;
using Xunit;
using SpocR.Utils;
using SpocR.SpocRVNext.Generators;
using Microsoft.Extensions.DependencyInjection;
using McMaster.Extensions.CommandLineUtils;
using SpocR.Extensions;
using SpocR.Commands;

namespace SpocR.Tests.SpocRVNext.Generators;

public class DbContextGeneratorPathEdgeTests
{
    [Fact]
    public async Task Generates_In_Directory_With_Dots()
    {
        var original = Directory.GetCurrentDirectory();
        var tempRoot = Directory.CreateTempSubdirectory();
        // create a dotted child directory
        var dotted = Path.Combine(tempRoot.FullName, "my.project.segment");
        Directory.CreateDirectory(dotted);
        try
        {
            Directory.SetCurrentDirectory(dotted);
            DirectoryUtils.SetBasePath(dotted);
            File.WriteAllText(Path.Combine(dotted, ".env"), "SPOCR_NAMESPACE=Edge.Dot\nSPOCR_GENERATOR_DB=Server=test;Database=db;\n");
            var gen = CreateGenerator();
            await gen.GenerateAsync(false);
            var spocrDir = Path.Combine(dotted, "SpocR");
            Assert.True(Directory.Exists(spocrDir), "SpocR Verzeichnis nicht erzeugt");
            Assert.True(File.Exists(Path.Combine(spocrDir, "SpocRDbContext.cs")), "DbContext nicht erzeugt");
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
        }
    }

    private static DbContextGenerator CreateGenerator()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConsole>(PhysicalConsole.Singleton);
        services.AddSingleton<ICommandOptions>(new TestOptionsVerbose());
        services.AddSingleton(sp => new CommandOptions(sp.GetRequiredService<ICommandOptions>()));
        services.AddSpocR();
        services.AddSingleton<SpocR.SpocRVNext.Engine.ITemplateRenderer, SpocR.SpocRVNext.Engine.SimpleTemplateEngine>();
        var provider = services.BuildServiceProvider();
        var fm = provider.GetRequiredService<SpocR.Managers.FileManager<SpocR.Models.ConfigurationModel>>();
        if (string.IsNullOrWhiteSpace(fm.Config.Project.Output.Namespace))
        {
            fm.Config.Project.Output.Namespace = "Edge.Dot";
        }
        if (fm.Config.Project?.Output?.DataContext?.Path == "./DataContext")
        {
            fm.Config.Project.Output.DataContext.Path = "DataContext";
        }
        return provider.GetRequiredService<DbContextGenerator>();
    }

    private sealed class TestOptionsVerbose : ICommandOptions
    {
        public string Path { get; set; } = string.Empty;
        public bool DryRun { get; set; }
        public bool Force { get; set; }
        public bool Quiet { get; set; }
        public bool Verbose { get; set; } = true;
        public bool NoVersionCheck { get; set; }
        public bool NoAutoUpdate { get; set; }
        public bool Debug { get; set; }
        public bool NoCache { get; set; }
        public string Procedure { get; set; } = string.Empty;
    }
}
