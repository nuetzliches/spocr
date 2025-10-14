using SpocR.SpocRVNext.Engine;
using SpocRVNext.Configuration; // note: EnvConfiguration lives in SpocRVNext.Configuration
using SpocR.SpocRVNext.Generators;
using SpocR.SpocRVNext.Metadata;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SpocR.SpocRVNext;

/// <summary>
/// Orchestrates future generation steps (placeholder implementation).
/// </summary>
public sealed class SpocRGenerator
{
    private readonly ITemplateRenderer _renderer;
    private readonly ITemplateLoader? _loader;
    private readonly Func<IReadOnlyList<InputDescriptor>> _inputs;
    private readonly Func<IReadOnlyList<OutputDescriptor>> _outputs;
    private readonly Func<IReadOnlyList<ResultDescriptor>> _results;
    private readonly Func<IReadOnlyList<ProcedureDescriptor>> _procedures;
    private readonly Func<ISchemaMetadataProvider>? _schemaProviderFactory;

    public SpocRGenerator(
        ITemplateRenderer renderer,
        ITemplateLoader? loader = null,
        Func<IReadOnlyList<InputDescriptor>>? inputsProvider = null,
        Func<IReadOnlyList<OutputDescriptor>>? outputsProvider = null,
        Func<IReadOnlyList<ResultDescriptor>>? resultsProvider = null,
        Func<IReadOnlyList<ProcedureDescriptor>>? proceduresProvider = null,
        Func<ISchemaMetadataProvider>? schemaProviderFactory = null)
    {
        _renderer = renderer;
        _loader = loader; // optional until full wiring
        _inputs = inputsProvider ?? (() => Array.Empty<InputDescriptor>());
        _outputs = outputsProvider ?? (() => Array.Empty<OutputDescriptor>());
        _results = resultsProvider ?? (() => Array.Empty<ResultDescriptor>());
        _procedures = proceduresProvider ?? (() => Array.Empty<ProcedureDescriptor>());
        _schemaProviderFactory = schemaProviderFactory;
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

    /// <summary>
    /// Full generation pipeline for vNext artifacts (idempotent per run). No legacy references.
    /// </summary>
    public int GenerateAll(EnvConfiguration cfg, string? projectRoot = null)
    {
        projectRoot ??= Directory.GetCurrentDirectory();
        if (cfg.GeneratorMode is not ("dual" or "next"))
            return 0; // vNext generation disabled in legacy mode
        // Namespace ableiten unter Berücksichtigung des Konfigurationspfades (-p)
        var resolver = new NamespaceResolver(cfg, msg => Console.Out.WriteLine(msg));
        var nsBase = resolver.ResolveForConfigPath(cfg.ConfigPath); // nutzt csproj nahe Konfiguration oder CWD
        // Compose final namespace: append output dir once
        var outSeg = string.IsNullOrWhiteSpace(cfg.OutputDir) ? "SpocR" : cfg.OutputDir!.Trim('.');
        var ns = nsBase.EndsWith('.' + outSeg, StringComparison.OrdinalIgnoreCase) ? nsBase : nsBase + '.' + outSeg;
        var total = 0;

        // If a schema provider factory is supplied and no explicit delegates were provided, use it to populate metadata.
        if (_schemaProviderFactory != null)
        {
            var schema = _schemaProviderFactory();
            if (_inputs == null || ReferenceEquals(_inputs, (Func<IReadOnlyList<InputDescriptor>>)(() => Array.Empty<InputDescriptor>())))
            {
                // no op - existing delegate already returns empty; we can't reassign readonly field, so rely on direct usage below
            }
            // Instead of attempting to mutate delegates, we will bypass and instantiate generators directly with schema collections when factory is present.
        }

        // Determine whether to emit minimal DbContext stub:
        // If a full vNext/legacy DbContext already exists under sample (e.g., SpocRDbContext.cs in any child 'SpocR' folder with endpoints/options), skip stub.
        bool dbContextAlreadyPresent = false;
        try
        {
            foreach (var file in Directory.EnumerateFiles(projectRoot, "SpocRDbContext.cs", SearchOption.AllDirectories))
            {
                // Heuristic: if sibling file 'SpocRDbContextOptions.cs' exists, treat as full context
                var dir = Path.GetDirectoryName(file)!;
                if (File.Exists(Path.Combine(dir, "SpocRDbContextOptions.cs")))
                {
                    dbContextAlreadyPresent = true;
                    break;
                }
            }
        }
        catch { }

        if (!dbContextAlreadyPresent)
        {
            string dbCtxOutDir;
            if (string.IsNullOrWhiteSpace(cfg.OutputDir))
            {
                dbCtxOutDir = Path.Combine(projectRoot, "SpocR");
            }
            else if (Path.IsPathRooted(cfg.OutputDir))
            {
                dbCtxOutDir = cfg.OutputDir;
            }
            else
            {
                dbCtxOutDir = Path.Combine(projectRoot, cfg.OutputDir);
            }
            GenerateMinimalDbContext(dbCtxOutDir, ns, "SpocRDbContext");
        }
        else
        {
            Console.Out.WriteLine("[spocr vNext] Info: Skipping minimal DbContext generation (full DbContext already present).");
        }

        // Mode-specific messaging if legacy config file still present
        try
        {
            var hasLegacyConfig = Directory.EnumerateFiles(projectRoot, "spocr.json", SearchOption.AllDirectories).Any();
            if (hasLegacyConfig)
            {
                if (cfg.GeneratorMode == "dual")
                    Console.Out.WriteLine("[spocr vNext] Info: spocr.json detected (dual mode) – legacy + vNext coexist.");
                else if (cfg.GeneratorMode == "next")
                    Console.Error.WriteLine("[spocr vNext] Warning: spocr.json detected while running in 'next' mode – consider removing or migrating configuration.");
            }
        }
        catch { }

        // Base output directory: ensure we point at .../SpocR for sample so schema folders appear beneath it
        var baseStructuredOut = projectRoot.EndsWith(Path.DirectorySeparatorChar + "SpocR", StringComparison.OrdinalIgnoreCase)
            ? projectRoot
            : Path.Combine(projectRoot, "SpocR");
        Directory.CreateDirectory(baseStructuredOut);
        // Konsolidierte Generierung: nur ProceduresGenerator (enthält künftig Input/Output/Result Konsolidierung) + evtl. TableTypes separat
        if (_schemaProviderFactory is null)
        {
            var procsGen = new ProceduresGenerator(_renderer, _procedures, _loader, projectRoot);
            total += procsGen.Generate(ns, baseStructuredOut);
        }
        else
        {
            var schema = _schemaProviderFactory();
            try
            {
                var dbgInputs = schema.GetInputs().Count;
                var dbgOutputs = schema.GetOutputs().Count;
                var dbgProcs = schema.GetProcedures().Count;
                Console.Out.WriteLine($"[spocr vNext] descriptor counts: inputs={dbgInputs} outputs={dbgOutputs} procedures={dbgProcs}");
            }
            catch { }
            var procsGen = new ProceduresGenerator(_renderer, () => schema.GetProcedures(), _loader, projectRoot);
            total += procsGen.Generate(ns, baseStructuredOut);
        }

        return total;
    }
}