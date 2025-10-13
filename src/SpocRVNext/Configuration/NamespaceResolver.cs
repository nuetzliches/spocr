using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace SpocRVNext.Configuration;

/// <summary>
/// Resolves a namespace for generated code using a compositional strategy:
/// 1. If EnvConfiguration provides an explicit NamespaceRoot (SPOCR_NAMESPACE) -> return it verbatim.
/// 2. Locate the nearest (upward) *.csproj starting from searchRoot (or current directory if null).
///    - Base name precedence inside the csproj: &lt;RootNamespace&gt; | &lt;AssemblyName&gt; | file name (without extension).
/// 3. Compute the relative path from the csproj directory to the working directory (searchRoot) and
///    append each segment (PascalCase) as nested namespace components.
/// 4. (No automatic suffix) – physical output directory (e.g. /SpocR) provides separation; we don't duplicate in namespace.
/// 5. Fallback: If no csproj is found, use last directory name (PascalCase).
/// This creates stable, hierarchy-reflecting namespaces like: RootProject.FeatureX.Api
/// </summary>
public sealed class NamespaceResolver
{
    private readonly EnvConfiguration _cfg;
    private readonly Action<string>? _logWarn;

    public NamespaceResolver(EnvConfiguration cfg, Action<string>? logWarn = null)
    {
        _cfg = cfg;
        _logWarn = logWarn;
    }

    public string Resolve(string? searchRoot = null)
    {
        // 1. Explicit override
        if (!string.IsNullOrWhiteSpace(_cfg.NamespaceRoot)) return _cfg.NamespaceRoot!;

        searchRoot ??= Directory.GetCurrentDirectory();
        var startDir = new DirectoryInfo(searchRoot);
        if (!startDir.Exists)
        {
            _logWarn?.Invoke($"[spocr namespace] searchRoot '{searchRoot}' does not exist. Using fallback.");
            return "SpocR.SpocR"; // kept consistent with suffix rule
        }

        // 2. Walk upwards to find nearest csproj
        FileInfo? csprojFile = null;
        var probe = startDir;
        while (probe != null && csprojFile == null)
        {
            var match = probe.GetFiles("*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (match != null) csprojFile = match;
            else probe = probe.Parent;
        }

        string baseName = "SpocR"; // fallback base
        DirectoryInfo baseDirForRel = startDir; // directory from which relative path segments are computed

        if (csprojFile != null)
        {
            baseDirForRel = csprojFile.Directory!;
            try
            {
                var doc = XDocument.Load(csprojFile.FullName);
                baseName = doc.Descendants("RootNamespace").FirstOrDefault()?.Value?.Trim()
                           ?? doc.Descendants("AssemblyName").FirstOrDefault()?.Value?.Trim()
                           ?? Path.GetFileNameWithoutExtension(csprojFile.Name);
            }
            catch (Exception ex)
            {
                _logWarn?.Invoke($"[spocr namespace] Failed to parse {csprojFile.Name}: {ex.Message}. Using file name.");
                baseName = Path.GetFileNameWithoutExtension(csprojFile.Name);
            }
        }
        else
        {
            // No csproj: treat the starting directory name as base
            baseName = ToPascalCase(startDir.Name);
            baseDirForRel = startDir.Parent ?? startDir; // relative segments will be just startDir.Name
            _logWarn?.Invoke("[spocr namespace] No .csproj found upward. Using directory-based base name.");
        }

        // 3. Compute relative path from csproj directory to searchRoot (if different) and add segments
        var nsParts = baseName.Split('.').Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (!PathsEqual(baseDirForRel.FullName, startDir.FullName))
        {
            var relPath = GetRelativePath(baseDirForRel.FullName, startDir.FullName);
            if (!string.IsNullOrEmpty(relPath))
            {
                var segments = relPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(ToPascalCase);
                nsParts.AddRange(segments);
            }
        }

        // 4. No enforced suffix; OUTPUT_DIR may differ and is a filesystem concern.
        // Normalize combined
        var combined = string.Join('.', nsParts.Where(p => p.Length > 0));
        return combined;
    }

    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var parts = input.Split(new[] { '-', '_', ' ', '.', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Select(p => char.ToUpperInvariant(p[0]) + (p.Length > 1 ? p.Substring(1).ToLowerInvariant() : string.Empty));
        var candidate = string.Concat(parts);
        candidate = new string(candidate.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
    if (string.IsNullOrEmpty(candidate)) candidate = "SpocR";
        if (char.IsDigit(candidate[0])) candidate = "N" + candidate;
        return candidate;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static string GetRelativePath(string fromPath, string toPath)
    {
        var fromUri = new Uri(AppendDirectorySeparatorChar(Path.GetFullPath(fromPath)));
        var toUri = new Uri(AppendDirectorySeparatorChar(Path.GetFullPath(toPath)));
        if (fromUri.Scheme != toUri.Scheme) return toPath; // cannot relativize (different volumes)
        var relativeUri = fromUri.MakeRelativeUri(toUri);
        var path = Uri.UnescapeDataString(relativeUri.ToString());
        return path.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
    }

    private static string AppendDirectorySeparatorChar(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}