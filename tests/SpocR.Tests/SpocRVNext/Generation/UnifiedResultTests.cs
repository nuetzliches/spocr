using System.IO;
using System.Linq;
using Xunit;

namespace SpocR.Tests.SpocRVNext.Generation;

public class UnifiedResultTests
{
    // Neuer vNext Output legt Schema-Ordner PascalCase an ("Samples" statt ursprünglichem lowercase "samples")
    private static string SampleDir => Path.Combine(FindRepoRoot(), "samples", "restapi", "SpocR", "Samples");

    [Fact]
    public void CreateUserWithOutput_HasResult1Property_And_InlineIOPresentOnce()
    {
        var file = Path.Combine(SampleDir, "CreateUserWithOutput.cs");
        Assert.True(File.Exists(file), "Consolidated proc file not generated");
        var text = File.ReadAllText(file);
        Assert.Contains("public IReadOnlyList<CreateUserWithOutputResultSet1Result> Result1", text);
        // Konsolidiert: Input & Output Record sollen im selben File genau einmal definiert sein
        var outputDefCount = text.Split("record struct CreateUserWithOutputOutput").Length - 1;
        Assert.Equal(1, outputDefCount);
        var inputDefCount = text.Split("record struct CreateUserWithOutputInput").Length - 1;
        Assert.Equal(1, inputDefCount);
    }

    [Fact]
    public void OrderListAsJson_PropertyRenamed()
    {
        var file = Path.Combine(SampleDir, "OrderListAsJson.cs");
        var text = File.ReadAllText(file);
        Assert.DoesNotContain("ResultSet1 {", text);
        Assert.Contains("Result1 {", text);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            bool marker = File.Exists(Path.Combine(dir.FullName, "README.md"))
                          && Directory.Exists(Path.Combine(dir.FullName, "samples", "restapi", "SpocR"))
                          && Directory.Exists(Path.Combine(dir.FullName, "src"));
            if (marker) return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }
}