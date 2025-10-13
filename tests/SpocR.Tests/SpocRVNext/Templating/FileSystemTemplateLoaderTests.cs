using System;
using System.IO;
using System.Linq;
using SpocR.SpocRVNext.Engine;
using Xunit;
using Shouldly;

namespace SpocR.Tests.SpocRVNext.Templating;

public class FileSystemTemplateLoaderTests
{
    [Fact]
    public void Loader_Should_Read_All_Spt_Files()
    {
        var dir = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(dir.FullName, "Alpha.spt"), "A");
        File.WriteAllText(Path.Combine(dir.FullName, "Beta.spt"), "B");
        // noise file should be ignored
        File.WriteAllText(Path.Combine(dir.FullName, "ignore.txt"), "X");

        var loader = new FileSystemTemplateLoader(dir.FullName);
        var names = loader.ListNames().OrderBy(x => x).ToArray();
        names.ShouldBe(new[] { "Alpha", "Beta" });
    }

    [Fact]
    public void Loader_TryLoad_Returns_Content_When_Found()
    {
        var dir = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(dir.FullName, "Demo.spt"), "Hello");
        var loader = new FileSystemTemplateLoader(dir.FullName);
        loader.TryLoad("Demo", out var content).ShouldBeTrue();
        content.ShouldBe("Hello");
    }

    [Fact]
    public void Loader_TryLoad_Is_CaseInsensitive()
    {
        var dir = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(dir.FullName, "CaseTest.spt"), "X");
        var loader = new FileSystemTemplateLoader(dir.FullName);
        loader.TryLoad("casetest", out var content).ShouldBeTrue();
        content.ShouldBe("X");
    }

    [Fact]
    public void Loader_Throws_For_Missing_Directory()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var ex = Assert.Throws<DirectoryNotFoundException>(() => new FileSystemTemplateLoader(missing));
        ex.Message.ShouldContain(missing);
    }
}
