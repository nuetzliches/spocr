using System.IO;
using System.Linq;
using Xunit;

namespace SpocR.Tests.SpocRVNext.Generation;

public class UnifiedResultTests
{
    private static string SampleDir => Path.Combine(FindRepoRoot(), "samples", "restapi", "SpocR", "samples");

    [Fact]
    public void CreateUserWithOutputResult_HasResult1Property_And_NoInlineOutputDuplicate()
    {
        var file = Path.Combine(SampleDir, "CreateUserWithOutputResult.cs");
        Assert.True(File.Exists(file), "Unified result file not generated");
        var text = File.ReadAllText(file);
        Assert.Contains("public IReadOnlyList<CreateUserWithOutputResultSet1Result> Result1", text);
        // Inline output duplicate should not exist (struct appears only once referencing external file)
        // Ensure we do NOT have two definitions of CreateUserWithOutputOutput in same file
        var occurrences = text.Split("CreateUserWithOutputOutput").Length - 1;
        // Should reference the type twice (property usage + cast) but not contain a record struct definition line
        Assert.DoesNotContain("record struct CreateUserWithOutputOutput", text);
        Assert.True(occurrences >= 1, "Expected at least one reference to Output type");
    }

    [Fact]
    public void OrderListAsJsonResult_PropertyRenamed()
    {
        var file = Path.Combine(SampleDir, "OrderListAsJsonResult.cs");
        var text = File.ReadAllText(file);
        Assert.DoesNotContain("ResultSet1 {", text);
        Assert.Contains("Result1 {", text);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SpocR.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}