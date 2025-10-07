using System;
using System.IO;
using System.Linq;

namespace SpocR.TestFramework;

/// <summary>
/// Provides stable resolution of repository root and artifacts paths independent of the current working directory
/// or test host base directory.
/// </summary>
public static class TestPaths
{
    private static readonly Lazy<string> _repoRoot = new(() =>
    {
        bool LooksLikeRoot(DirectoryInfo d) =>
            d.GetFiles("SpocR.sln").Any() || File.Exists(Path.Combine(d.FullName, "src", "SpocR.csproj"));

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        DirectoryInfo? lastGood = null;
        while (dir != null)
        {
            if (LooksLikeRoot(dir)) { lastGood = dir; break; }
            dir = dir.Parent;
        }
        // Fallback: if not found, walk up again storing highest existing parent, then return original base
        return (lastGood ?? new DirectoryInfo(AppContext.BaseDirectory)).FullName;
    });

    public static string RepoRoot => _repoRoot.Value;

    public static string Artifacts(params string[] parts)
    {
        var all = new string[] { RepoRoot, ".artifacts" }.Concat(parts).ToArray();
        return Path.Combine(all);
    }
}