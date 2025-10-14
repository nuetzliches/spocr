using System.IO;
using Xunit;
using SpocRVNext.Configuration;

namespace SpocR.Tests.SpocRVNext.Configuration;

public class NamespaceResolverTests
{
    [Fact]
    public void Resolve_ReturnsExplicitNamespace_WhenSPOCR_NAMESPACE_Set()
    {
        var repoRoot = FindRepoRoot();
        var overrides = new System.Collections.Generic.Dictionary<string, string?>
        {
            {"SPOCR_NAMESPACE", "Custom.Namespace"}
        };
        var envCfg = EnvConfiguration.Load(projectRoot: repoRoot, cliOverrides: overrides);
        var resolver = new NamespaceResolver(envCfg);
        var ns = resolver.Resolve(repoRoot);
        Assert.Equal("Custom.Namespace", ns);
    }

    [Fact]
    public void Load_Throws_WhenNamespaceMissing()
    {
        // Use isolated temp directory to avoid accidental .env with SPOCR_NAMESPACE at repo root
        var temp = System.IO.Directory.CreateTempSubdirectory();
        Assert.Throws<System.InvalidOperationException>(() => EnvConfiguration.Load(projectRoot: temp.FullName));
    }

    [Fact]
    public void Load_Throws_WhenNamespaceInvalid()
    {
        var repoRoot = FindRepoRoot();
        var overrides = new System.Collections.Generic.Dictionary<string, string?>
        {
            {"SPOCR_NAMESPACE", "1Bad"}
        };
        Assert.Throws<System.InvalidOperationException>(() => EnvConfiguration.Load(projectRoot: repoRoot, cliOverrides: overrides));
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
