using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace SpocR.SpocRVNext.Configuration;

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

    /// <summary>
    /// Resolve namespace. Only rule: look for a *.csproj in (configPath directory if provided, else searchRoot/current directory).
    /// No upward/downward/extended scans; no relative path segment appending. Pure, deterministic.
    /// </summary>
    public string Resolve(string? searchRoot = null)
    {
        // 1. Explicit override
        if (!string.IsNullOrWhiteSpace(_cfg.NamespaceRoot)) return _cfg.NamespaceRoot!;

        searchRoot ??= Directory.GetCurrentDirectory();
        var startDir = new DirectoryInfo(searchRoot);
        if (!startDir.Exists)
        {
            _logWarn?.Invoke($"[spocr namespace] searchRoot '{searchRoot}' does not exist. Using fallback.");
            return "";
        }

        string? configPath = _cfg.ConfigPath;

        // Derive effective directory (configPath directory wins)
        DirectoryInfo effectiveDir = startDir;
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            try
            {
                var cp = Path.GetFullPath(configPath);

                if (File.Exists(cp))
                {
                    var cdir = Path.GetDirectoryName(cp)!;
                    if (Directory.Exists(cdir)) effectiveDir = new DirectoryInfo(cdir);
                }
                else if (Directory.Exists(cp))
                {
                    effectiveDir = new DirectoryInfo(cp);
                }
            }
            catch (Exception ex)
            {
                _logWarn?.Invoke($"[spocr namespace] configPath resolution failed: {ex.Message}");
            }
        }

        string baseName;
        try
        {
            var proj = effectiveDir.GetFiles("*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (proj != null)
            {
                baseName = TryReadRootNamespace(proj.FullName) ?? Path.GetFileNameWithoutExtension(proj.Name);
            }
            else
            {
                baseName = ToPascalCase(effectiveDir.Name);
                // silent fallback to directory name
            }
        }
        catch (Exception ex)
        {
            baseName = ToPascalCase(effectiveDir.Name);
            _logWarn?.Invoke($"[spocr namespace] scan error: {ex.Message}");
        }

        var ns = string.Join('.', baseName.Split('.').Where(p => !string.IsNullOrWhiteSpace(p)));
        _logWarn?.Invoke($"[spocr namespace] ");

        return ns;
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

    // Removed path/relative/repo root helpers after simplification

    private static string? TryReadRootNamespace(string csprojPath)
    {
        try
        {
            var doc = XDocument.Load(csprojPath);
            return doc.Descendants("RootNamespace").FirstOrDefault()?.Value?.Trim()
                   ?? doc.Descendants("AssemblyName").FirstOrDefault()?.Value?.Trim();
        }
        catch { return null; }
    }
}