using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SpocR.SpocRVNext.Generators;
using SpocR.SpocRVNext.Extensions;
using SpocR.SpocRVNext.Cli;
using SpocR.SpocRVNext.Engine;
using SpocR.Utils;
using SpocR.SpocRVNext.Infrastructure;
using Xunit;

namespace SpocR.Tests.SpocRVNext.Generators;

public class DbContextGeneratorTests
{
    private static void Fail(string message) => throw new Xunit.Sdk.XunitException(message);
    [Fact]
    public async Task DbContextGenerator_Generates_In_Next_Mode()
    {
        var originalCwd = Directory.GetCurrentDirectory();
        var temp = Directory.CreateTempSubdirectory();
        try
        {
            Directory.SetCurrentDirectory(temp.FullName);
            DirectoryUtils.SetBasePath(temp.FullName);
            File.WriteAllText(Path.Combine(temp.FullName, ".env"), "SPOCR_NAMESPACE=Test.App\nSPOCR_GENERATOR_DB=Server=test;Database=db;\n");
            var gen = CreateGenerator();
            await gen.GenerateAsync(isDryRun: false);
            // Diagnose: liste alle Dateien unter temp
            var allFiles = Directory.GetFiles(temp.FullName, "*", SearchOption.AllDirectories);
            // Rekursiv nach generierten Artefakten suchen (Generator kann Pfadstruktur aus Config ableiten)
            var generated = Directory.GetFiles(temp.FullName, "SpocRDbContext.cs", SearchOption.AllDirectories);
            if (generated.Length == 0)
            {
                var allCs = Directory.GetFiles(temp.FullName, "*.cs", SearchOption.AllDirectories);
                var all = string.Join("\n", allCs);
                var manifest = string.Join("\n", allFiles);
                var expectedSpocrDir = Path.Combine(temp.FullName, "SpocR");
                var spocrExists = Directory.Exists(expectedSpocrDir);
                var probePath = Path.Combine(temp.FullName, "probe_write_check.txt");
                File.WriteAllText(probePath, "ok");
                var probeExists = File.Exists(probePath);
                Fail("SpocRDbContext.cs nicht gefunden. SpocRDirExists=" + spocrExists + " ProbeWrite=" + probeExists + "\nAlle .cs Dateien:\n" + all + "\n--- Manifest aller Dateien ---\n" + manifest + "\nCWD=" + Directory.GetCurrentDirectory());
            }
            // Leite Basisverzeichnis von erster Fundstelle ab
            var ctxFile = generated[0];
            var outDir = Path.GetDirectoryName(ctxFile)!;
            bool MustExist(string name) => File.Exists(Path.Combine(outDir, name));
            Assert.True(MustExist("ISpocRDbContext.cs"), "ISpocRDbContext.cs fehlt bei " + outDir);
            Assert.True(MustExist("SpocRDbContextOptions.cs"), "SpocRDbContextOptions.cs fehlt bei " + outDir);
            Assert.True(MustExist("SpocRDbContextServiceCollectionExtensions.cs"), "Extensions fehlt bei " + outDir);
        }
        finally { Directory.SetCurrentDirectory(originalCwd); }
    }

    private static DbContextGenerator CreateGenerator()
    {
        var services = new ServiceCollection();
        // Registriere zuerst unsere eigene ICommandOptions Instanz mit Verbose=true
        services.AddSingleton<ICommandOptions>(new TestOptionsVerbose());
        // Danach den CommandOptions Wrapper, damit ConsoleService.Verbose greift
        services.AddSingleton(sp => new CommandOptions(sp.GetRequiredService<ICommandOptions>()));
        services.AddSpocR();
        var tempTemplates = Path.Combine(Path.GetTempPath(), "spocr_test_templates");
        Directory.CreateDirectory(tempTemplates);
        File.WriteAllText(Path.Combine(tempTemplates, "DbContext.spt"), "// test template\nnamespace {{ Namespace }};\npublic class SpocRDbContext { }");
        services.AddSingleton<SpocR.SpocRVNext.Engine.ITemplateRenderer, SpocR.SpocRVNext.Engine.SimpleTemplateEngine>();
        services.AddSingleton<SpocR.SpocRVNext.Engine.ITemplateLoader>(_ => new SpocR.SpocRVNext.Engine.FileSystemTemplateLoader(tempTemplates));
        var provider = services.BuildServiceProvider();
        var fm = provider.GetRequiredService<FileManager<SpocR.SpocRVNext.Models.ConfigurationModel>>();
        if (string.IsNullOrWhiteSpace(fm.Config.Project.Output.Namespace))
        {
            fm.Config.Project.Output.Namespace = "Test.App";
        }
        // Normalize DataContext.Path to avoid parent resolution quirks with leading './'
        if (fm.Config.Project?.Output?.DataContext?.Path == "./DataContext")
        {
            fm.Config.Project.Output.DataContext.Path = "DataContext"; // drop leading './' for deterministic parent
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
