using System;
using System.IO;

namespace SpocR.SpocRVNext.Utils;

/// <summary>
/// Centralized resolution of the active project root for vNext generation.
/// Order of precedence:
/// 1. Explicit environment variable SPOCR_CONFIG_PATH (file) or SPOCR_PROJECT_ROOT (directory)
/// 2. Command line -p resolved earlier via DirectoryUtils.SetBasePath (stored as working directory)
/// 3. Current working directory
/// Falls back gracefully and never throws.
/// Provides also a heuristic for the solution root (first parent containing src/SpocR.csproj or .git folder).
/// </summary>
internal static class ProjectRootResolver
{
    public static string ResolveCurrent()
    {
        try
        {
            // If process env passes explicit config path, prefer its directory
            var cfgPath = Environment.GetEnvironmentVariable("SPOCR_CONFIG_PATH");
            if (!string.IsNullOrWhiteSpace(cfgPath) && File.Exists(cfgPath))
            {
                return Path.GetDirectoryName(Path.GetFullPath(cfgPath)) ?? Directory.GetCurrentDirectory();
            }
            var projRoot = Environment.GetEnvironmentVariable("SPOCR_PROJECT_ROOT");
            if (!string.IsNullOrWhiteSpace(projRoot) && Directory.Exists(projRoot))
            {
                return Path.GetFullPath(projRoot);
            }
        }
        catch { }
        // Fallback: current working directory (already adjusted by -p via DirectoryUtils.SetBasePath in CommandBase)
        try { return Directory.GetCurrentDirectory(); } catch { return AppContext.BaseDirectory; }
    }

    public static string GetSolutionRootOrCwd()
    {
        var start = ResolveCurrent();
        try
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "SpocR.sln")) || Directory.Exists(Path.Combine(dir.FullName, ".git")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
        }
        catch { }
        return start;
    }
}