using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SpocR.SpocRVNext.Engine;

/// <summary>
/// Simple file system based template loader. Looks for *.spt (SpocR Template) files in a root directory.
/// Naming convention: <LogicalName>.spt (e.g. DbContext.spt)
/// </summary>
public sealed class FileSystemTemplateLoader : ITemplateLoader
{
    private readonly string _root;
    private readonly Dictionary<string, string> _cache;

    public FileSystemTemplateLoader(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Root directory required", nameof(rootDirectory));
        _root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(_root))
            throw new DirectoryNotFoundException(_root);
        // Load top-level templates
        _cache = Directory.EnumerateFiles(_root, "*.spt", SearchOption.TopDirectoryOnly)
            .ToDictionary(
                f => Path.GetFileNameWithoutExtension(f),
                f => File.ReadAllText(f),
                StringComparer.OrdinalIgnoreCase);

        // Additionally load one level of subdirectory templates (names remain file name without extension, duplicates last-in wins)
        foreach (var subDir in Directory.EnumerateDirectories(_root))
        {
            foreach (var file in Directory.EnumerateFiles(subDir, "*.spt", SearchOption.TopDirectoryOnly))
            {
                var key = Path.GetFileNameWithoutExtension(file);
                _cache[key] = File.ReadAllText(file);
            }
        }
    }

    public bool TryLoad(string name, out string content)
        => _cache.TryGetValue(name, out content!);

    public IEnumerable<string> ListNames() => _cache.Keys;
}
