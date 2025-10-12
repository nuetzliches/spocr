using SpocR.SpocRVNext.Engine;

namespace SpocR.SpocRVNext;

/// <summary>
/// Orchestrates future generation steps (placeholder implementation).
/// </summary>
public sealed class SpocRGenerator
{
    private readonly ITemplateRenderer _renderer;

    public SpocRGenerator(ITemplateRenderer renderer)
    {
        _renderer = renderer;
    }

    /// <summary>
    /// Temporary demo method.
    /// </summary>
    public string RenderDemo() => _renderer.Render("// Demo {{ Name }}", new { Name = "SpocR" });
}