using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SpocR.SpocRVNext.Utils;

internal static class DirectoryUtils
{
    private static string BasePath;

    internal static void SetBasePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            BasePath = null;
            return;
        }

        // If a file (ends with .json, .csproj etc.) was passed, use its directory; otherwise treat as directory path
        var candidate = path;
        try
        {
            // Expand relative inputs (e.g. ./debug/spocr.json or debug) against current directory
            if (!Path.IsPathRooted(candidate))
            {
                candidate = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), candidate));
            }

            if (File.Exists(candidate))
            {
                candidate = Path.GetDirectoryName(candidate);
            }
            else if (Directory.Exists(candidate))
            {
                // It's an existing directory (even if it contains dots) – keep as-is.
            }
            else
            {
                // If it does not exist yet, heuristically check if it looks like a file by extension.
                // BUT only treat it as a file path if the parent directory exists and the extension does NOT simply come from a directory name with dots.
                var ext = Path.GetExtension(candidate);
                if (!string.IsNullOrEmpty(ext))
                {
                    var parentDir = Path.GetDirectoryName(candidate);
                    if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                    {
                        candidate = parentDir; // treat as file path
                    }
                    // else: leave candidate unchanged – likely a directory to be created later
                }
            }

            // Normalize trailing directory separator
            if (!string.IsNullOrEmpty(candidate))
            {
                candidate = Path.GetFullPath(candidate);
            }

            BasePath = candidate;
        }
        catch
        {
            // Fallback: reset BasePath so legacy fallback logic applies
            BasePath = null;
        }
    }

    internal static string GetApplicationRoot()
    {
#if DEBUG
        return Directory.GetCurrentDirectory();
#else
        var codeBase = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        return Regex.Replace(codeBase, @"^(file\:\\)", string.Empty);
#endif
    }

    internal static string GetAppDataDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "spocr");
    }

    internal static string GetWorkingDirectory(params string[] paths)
    {
        var pathList = new List<string>();

        if (!string.IsNullOrEmpty(BasePath))
        {
            pathList.Add(BasePath);
        }
        else
        {
#if DEBUG
            pathList.Add(Path.Combine(Directory.GetCurrentDirectory(), "..", "debug"));
#else
            pathList.Add(Directory.GetCurrentDirectory());
#endif
        }

        pathList.AddRange(paths);

        return Path.Combine(pathList.ToArray()).ToString();
    }

    internal static bool IsPath(string input)
    {
        if (input == null) return false;
        return input.Contains('/') || input.Contains('\\');
    }
}
