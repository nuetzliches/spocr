using SpocR.SpocRVNext.Engine;
using System;
using System.IO;

namespace SpocR.SpocRVNext;

/// <summary>
/// Orchestrates future generation steps (placeholder implementation).
/// </summary>
public sealed class SpocRGenerator
{
    private readonly ITemplateRenderer _renderer;
    private readonly ITemplateLoader? _loader;

    public SpocRGenerator(ITemplateRenderer renderer, ITemplateLoader? loader = null)
    {
        _renderer = renderer;
        _loader = loader; // optional until full wiring
    }

    /// <summary>
    /// Temporary demo method.
    /// </summary>
    public string RenderDemo() => _renderer.Render("// Demo {{ Name }}", new { Name = "SpocR" });

    /// <summary>
    /// Minimal next-gen generation: renders DbContext template (if available) into target directory.
    /// </summary>
    /// <param name="outputDir">Directory to place generated file.</param>
    /// <param name="namespaceRoot">Namespace root (fallback: SpocR.Generated).</param>
    /// <param name="className">Class name (fallback: SpocRDbContext).</param>
    /// <returns>Path of generated file or null if template missing.</returns>
    public string? GenerateMinimalDbContext(string outputDir, string? namespaceRoot = null, string? className = null)
    {
        if (_loader == null)
            return null; // loader not provided yet
        // Accept either new canonical name or legacy placeholder for early rename flexibility
        if (!(_loader.TryLoad("SpocRDbContext", out var tpl) || _loader.TryLoad("DbContext", out tpl)))
            return null; // template not present

        var ns = string.IsNullOrWhiteSpace(namespaceRoot) ? "SpocR.Generated" : namespaceRoot!;
        var cls = string.IsNullOrWhiteSpace(className) ? "SpocRDbContext" : className!;
        var rendered = _renderer.Render(tpl, new { Namespace = ns, ClassName = cls });
        Directory.CreateDirectory(outputDir);
        var file = Path.Combine(outputDir, cls + ".cs");
        File.WriteAllText(file, rendered);
        return file;
    }
}