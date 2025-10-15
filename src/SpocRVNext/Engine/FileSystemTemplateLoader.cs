using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SpocR.SpocRVNext.Engine;

/// <summary>
/// Simple file system based template loader. Looks for *.spt (SpocR Template) files in a root directory.
/// Naming convention base: <LogicalName>.spt (e.g. DbContext.spt)
/// Versioned override (TFM major) convention: <LogicalName>.net10.spt, <LogicalName>.net9.spt, ... (higher preferred)
/// </summary>
public sealed class FileSystemTemplateLoader : ITemplateLoader
{
    private readonly string _root;
    private readonly Dictionary<string, Dictionary<string, string>> _byLogical; // logicalName -> variantKey -> content
    private readonly string _currentTfmMajor;

    public FileSystemTemplateLoader(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Root directory required", nameof(rootDirectory));
        _root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(_root))
            throw new DirectoryNotFoundException(_root);
        _currentTfmMajor = ResolveCurrentTfmMajor();
        _byLogical = new(StringComparer.OrdinalIgnoreCase);
        void AddFile(string file)
        {
            var fileName = Path.GetFileName(file);
            var logical = Path.GetFileNameWithoutExtension(fileName);
            // strip version suffix if pattern matches *.net<digits>
            string variantKey = "base";
            var idx = logical.LastIndexOf('.');
            if (idx > 0 && idx < logical.Length - 1)
            {
                var tail = logical.Substring(idx + 1);
                if (tail.StartsWith("net", StringComparison.OrdinalIgnoreCase) && tail.Length > 3 && tail.Skip(3).All(char.IsDigit))
                {
                    variantKey = tail.ToLowerInvariant();
                    logical = logical.Substring(0, idx); // remove suffix
                }
            }
            if (!_byLogical.TryGetValue(logical, out var variants))
            {
                variants = new(StringComparer.OrdinalIgnoreCase);
                _byLogical[logical] = variants;
            }
            variants[variantKey] = File.ReadAllText(file);
        }
        foreach (var f in Directory.EnumerateFiles(_root, "*.spt", SearchOption.TopDirectoryOnly)) AddFile(f);
        foreach (var subDir in Directory.EnumerateDirectories(_root))
            foreach (var f in Directory.EnumerateFiles(subDir, "*.spt", SearchOption.TopDirectoryOnly)) AddFile(f);
    }

    public bool TryLoad(string name, out string content)
    {
        if (_byLogical.TryGetValue(name, out var variants))
        {
            // precedence: exact currentTfmMajor (e.g. net10) -> base
            if (variants.TryGetValue(_currentTfmMajor, out content!)) return true;
            if (variants.TryGetValue("base", out content!)) return true;
            // fallback: highest net* available (sorted desc)
            var netVariant = variants.Keys
                .Where(k => k.StartsWith("net"))
                .OrderByDescending(k => k.Length) // net10 > net9 (string length diff) safe for net10/net8
                .ThenByDescending(k => k)
                .FirstOrDefault();
            if (netVariant != null)
            {
                content = variants[netVariant];
                return true;
            }
        }
        content = null!;
        return false;
    }

    public IEnumerable<string> ListNames() => _byLogical.Keys;

    private static string ResolveCurrentTfmMajor()
    {
        // Attempt to detect via compiled assemblies (multi-target scenario: environment variable or constants usually used).
        // Simplify: inspect environment variable SPOCR_TFM if provided else default net10 preferred for forward features.
        var tfm = Environment.GetEnvironmentVariable("SPOCR_TFM");
        if (!string.IsNullOrWhiteSpace(tfm))
        {
            var major = ExtractMajor(tfm!);
            if (major != null) return major;
        }
        // Fallback: prefer latest known (net10) to allow advanced templates; can be overridden by env.
        return "net10";
    }

    private static string? ExtractMajor(string tfm)
    {
        tfm = tfm.Trim().ToLowerInvariant();
        if (tfm.StartsWith("net"))
        {
            // net8.0 / net9.0 / net10.0 => net8 / net9 / net10
            var digits = new string(tfm.Skip(3).TakeWhile(c => char.IsDigit(c)).ToArray());
            if (digits.Length > 0) return "net" + digits;
        }
        return null;
    }
}
