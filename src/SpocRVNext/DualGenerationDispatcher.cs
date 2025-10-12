using System;
using SpocR.SpocRVNext.Engine;
using SpocRVNext.Configuration;

namespace SpocR.SpocRVNext;

/// <summary>
/// Placeholder service for future dual-generation orchestration.
/// Will later:
///  - Invoke legacy generator (existing pathways) and new generator side-by-side when mode=dual.
///  - Collect hashes & produce diff metrics.
///  - Respect allow-list for benign differences.
/// Currently only demonstrates mode branching.
/// </summary>
public sealed class DualGenerationDispatcher
{
    private readonly EnvConfiguration _cfg;
    private readonly ITemplateRenderer _renderer;

    public DualGenerationDispatcher(EnvConfiguration cfg, ITemplateRenderer renderer)
    {
        _cfg = cfg;
        _renderer = renderer;
    }

    public string ExecuteDemo()
    {
        return _cfg.GeneratorMode switch
        {
            "legacy" => "[legacy-only demo placeholder]",
            "next" => new SpocRGenerator(_renderer).RenderDemo(),
            "dual" => $"[dual] legacy+next => {new SpocRGenerator(_renderer).RenderDemo()}",
            _ => throw new InvalidOperationException($"Unknown mode '{_cfg.GeneratorMode}'")
        };
    }
}