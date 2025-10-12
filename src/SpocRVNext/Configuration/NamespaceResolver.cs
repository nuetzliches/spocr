using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace SpocRVNext.Configuration;

/// <summary>
/// Resolves a root namespace using this precedence:
/// 1. Explicit override from EnvConfiguration (SPOCR_NAMESPACE)
/// 2. <RootNamespace> in the nearest .csproj
/// 3. <AssemblyName> in the .csproj
/// 4. Project file name (without extension)
/// 5. Fallback constant "SpocR.Generated"
/// Emits warnings (via provided action) when falling back.
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
        var csproj = Directory.EnumerateFiles(searchRoot, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (csproj == null)
        {
            _logWarn?.Invoke("[spocr namespace] No .csproj found, using fallback 'SpocR.Generated'.");
            return "SpocR.Generated";
        }

        try
        {
            var doc = XDocument.Load(csproj);
            var rootNs = doc.Descendants("RootNamespace").FirstOrDefault()?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(rootNs)) return rootNs!;

            var asm = doc.Descendants("AssemblyName").FirstOrDefault()?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(asm)) return asm!;

            var fileName = Path.GetFileNameWithoutExtension(csproj);
            if (!string.IsNullOrWhiteSpace(fileName)) return fileName!;
        }
        catch (Exception ex)
        {
            _logWarn?.Invoke($"[spocr namespace] Failed to parse {csproj}: {ex.Message}. Using fallback.");
        }

        _logWarn?.Invoke("[spocr namespace] Falling back to 'SpocR.Generated'.");
        return "SpocR.Generated";
    }
}