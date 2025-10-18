using System.IO;
using Xunit;

namespace SpocR.Tests.SpocRVNext.Generators;

public class UnifiedProcedureTemplateExtensionsTests
{
    [Fact]
    public void UnifiedProcedureTemplate_Has_Wrapper_And_Extension_With_New_Naming()
    {
        // Locate template relative to repo root (src/SpocRVNext/Templates/Procedures/UnifiedProcedure.spt)
        var root = Directory.GetCurrentDirectory();
        DirectoryInfo? dir = new DirectoryInfo(root);
        for (int i = 0; i < 6 && dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src")); i++) dir = dir.Parent;
        Assert.NotNull(dir);
        var tplPath = Path.Combine(dir!.FullName, "src", "SpocRVNext", "Templates", "Procedures", "UnifiedProcedure.spt");
        Assert.True(File.Exists(tplPath), "Template file not found: " + tplPath);
        var content = File.ReadAllText(tplPath);

        // Wrapper class declaration with explicit 'Procedure' suffix.
        Assert.Contains("public static class {{ ProcedureTypeName }}Procedure", content);

        // Extension class ends with 'Extensions' and method is '<ProcName>Async'.
        Assert.Contains("public static class {{ ProcedureTypeName }}Extensions", content);
        Assert.Contains("this ISpocRDbContext db", content); // extension signature marker
        Assert.Contains("{{ ProcedureTypeName }}Procedure.ExecuteAsync", content); // bridging call
        Assert.DoesNotContain("ProcedureAsync(this ISpocRDbContext db", content); // old style suffixed method name no longer present
    }
}