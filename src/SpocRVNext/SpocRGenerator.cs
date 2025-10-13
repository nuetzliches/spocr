using SpocR.SpocRVNext.Engine;
using SpocRVNext.Configuration; // note: EnvConfiguration lives in SpocRVNext.Configuration
using SpocR.SpocRVNext.Generators;
using SpocR.SpocRVNext.Metadata;
using System;
using System.Collections.Generic;
using System.IO;

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

        var ns = cfg.NamespaceRoot ?? "SpocR.Generated";
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

        // DbContext (if template present)
        var dbCtxOutDir = Path.Combine(projectRoot, cfg.OutputDir ?? "SpocR");
        GenerateMinimalDbContext(dbCtxOutDir, ns, "SpocRDbContext");

        if (_schemaProviderFactory is null)
        {
            // Legacy path using provided delegates
            var inputsGen = new InputsGenerator(_renderer, _inputs ?? (() => Array.Empty<InputDescriptor>()), _loader, projectRoot);
            total += inputsGen.Generate(ns);
            var outputsGen = new OutputsGenerator(_renderer, _outputs, _loader, projectRoot);
            total += outputsGen.Generate(ns);
            var resultsGen = new ResultsGenerator(_renderer, _results, _loader, projectRoot);
            total += resultsGen.Generate(ns);
            var procsGen = new ProceduresGenerator(_renderer, _procedures, _loader, projectRoot);
            total += procsGen.Generate(ns);
        }
        else
        {
            var schema = _schemaProviderFactory();
            var inputsGen = new InputsGenerator(_renderer, () => schema.GetInputs(), _loader, projectRoot);
            total += inputsGen.Generate(ns);
            var outputsGen = new OutputsGenerator(_renderer, () => schema.GetOutputs(), _loader, projectRoot);
            total += outputsGen.Generate(ns);
            // Result type generation may rely on result sets; for now feed empty until ResultDescriptor strategy defined
            var resultsGen = new ResultsGenerator(_renderer, () => schema.GetResults(), _loader, projectRoot);
            total += resultsGen.Generate(ns);
            var procsGen = new ProceduresGenerator(_renderer, () => schema.GetProcedures(), _loader, projectRoot);
            total += procsGen.Generate(ns);
        }

        return total;
    }
}