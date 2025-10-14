using System.IO;
using Xunit;
using SpocRVNext.Configuration;

namespace SpocR.Tests.SpocRVNext.Configuration;

public class NamespaceResolverTests
{
    [Fact]
    public void Resolve_UsesRestApiProjectRootNamespace_WhenAtRepoRoot()
    {
        // Arrange: gehe zum Repo Root (dieser Test geht davon aus, dass WorkingDirectory = Repo Root beim Testlauf)
        var repoRoot = FindRepoRoot();
    // README.md ist optional in Test-Kontext (kann bei reduzierter Arbeitskopie fehlen)
    var envCfg = EnvConfiguration.Load(repoRoot); // liest .env falls vorhanden
        var resolver = new NamespaceResolver(envCfg, msg => { /* optional logging */ });

        // Act
        var ns = resolver.Resolve(repoRoot);

        // Assert: Pivot Heuristik liefert 'RestApi' falls samples/restapi existiert, sonst fällt es ggf. auf Projekt-/Verzeichnisnamen zurück.
        if (Directory.Exists(Path.Combine(repoRoot, "samples", "restapi")))
        {
            Assert.Equal("RestApi", ns);
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(ns));
        }
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
