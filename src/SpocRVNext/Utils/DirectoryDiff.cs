using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SpocR.SpocRVNext.Utils;

public sealed record DiffSummary(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Changed,
    int TotalLegacy,
    int TotalNext
)
{
    public bool HasStructuralIssues => Added.Count + Removed.Count > 0; // changed might be benign
}

public static class DirectoryDiff
{
    public static DiffSummary Compare(string legacyDir, string nextDir, IEnumerable<string>? allowListGlobs = null)
    {
        var legacyFiles = ListRelativeFiles(legacyDir);
        var nextFiles = ListRelativeFiles(nextDir);

        var all = new HashSet<string>(legacyFiles.Concat(nextFiles), StringComparer.OrdinalIgnoreCase);
        var added = new List<string>();
        var removed = new List<string>();
        var changed = new List<string>();

        var allow = BuildAllowMatchers(allowListGlobs);

        foreach (var file in all.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var inLegacy = legacyFiles.Contains(file);
            var inNext = nextFiles.Contains(file);
            if (!inLegacy && inNext)
            {
                if (!IsAllowed(file, allow)) added.Add(file);
                continue;
            }
            if (inLegacy && !inNext)
            {
                if (!IsAllowed(file, allow)) removed.Add(file);
                continue;
            }
            // both exist: compare content hash
            var legacyHash = HashFile(Path.Combine(legacyDir, file));
            var nextHash = HashFile(Path.Combine(nextDir, file));
            if (!legacyHash.Equals(nextHash, StringComparison.OrdinalIgnoreCase) && !IsAllowed(file, allow))
            {
                changed.Add(file);
            }
        }

        return new DiffSummary(added, removed, changed, legacyFiles.Count, nextFiles.Count);
    }

    private static HashSet<string> ListRelativeFiles(string root)
    {
        if (!Directory.Exists(root)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string HashFile(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs));
    }

    private sealed record Pattern(Func<string, bool> MatchFn);

    private static List<Pattern> BuildAllowMatchers(IEnumerable<string>? globs)
    {
        var list = new List<Pattern>();
        if (globs == null) return list;
        foreach (var g in globs)
        {
            if (string.IsNullOrWhiteSpace(g)) continue;
            var rx = GlobToRegex(g.Trim());
            list.Add(new Pattern(s => Regex.IsMatch(s, rx, RegexOptions.IgnoreCase)));
        }
        return list;
    }

    private static bool IsAllowed(string path, List<Pattern> patterns) => patterns.Any(p => p.MatchFn(path));

    private static string GlobToRegex(string pattern)
    {
        // Very small glob -> regex translation (* => .*  ? => .)
        var escaped = Regex.Escape(pattern)
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", ".");
        return "^" + escaped + "$";
    }
}