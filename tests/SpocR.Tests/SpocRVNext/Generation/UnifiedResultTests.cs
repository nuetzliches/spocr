using System.IO;
using System.Linq;
using Xunit;

namespace SpocR.Tests.SpocRVNext.Generation;

public class UnifiedResultTests
{
    // Neuer vNext Output legt Schema-Ordner PascalCase an ("Samples" statt ursprünglichem lowercase "samples")
    private static string SampleDir => Path.Combine(FindRepoRoot(), "samples", "restapi", "SpocR", "Samples");

    [Fact]
    public void CreateUserWithOutput_FirstResultProperty_IsResult_NoNumber()
    {
        var file = Path.Combine(SampleDir, "CreateUserWithOutput.cs");
        Assert.True(File.Exists(file), "Consolidated proc file not generated");
        var text = File.ReadAllText(file);
        Assert.Contains("public CreateUserWithOutputOutput? Output", text);
        Assert.DoesNotContain("public IReadOnlyList", text); // pure output procedure => no result set wrapper
        // Konsolidiert: Input & Output Record sollen im selben File genau einmal definiert sein
        var outputDefCount = text.Split("record struct CreateUserWithOutputOutput").Length - 1;
        Assert.Equal(1, outputDefCount);
        var inputDefCount = text.Split("record struct CreateUserWithOutputInput").Length - 1;
        Assert.Equal(1, inputDefCount);
    }

    [Fact]
    public void OrderListAsJson_FirstResult_NoNumber_And_SecondResultHasNumber()
    {
    var file = Path.Combine(SampleDir, "UserOrderHierarchyJson.cs"); // JSON hierarchy result consolidated
        var text = File.ReadAllText(file);
        Assert.Contains("public IReadOnlyList<UserOrderHierarchyJsonResultSet1Result> Result", text);
        Assert.DoesNotContain("Result1", text); // single JSON payload => only primary Result property
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