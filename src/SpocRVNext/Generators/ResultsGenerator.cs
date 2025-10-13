using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using SpocR.SpocRVNext.Engine;
using SpocR.SpocRVNext.Metadata;
using SpocR.SpocRVNext.Utils;

namespace SpocR.SpocRVNext.Generators;

public sealed class ResultsGenerator
{
    private readonly ITemplateRenderer _renderer;
    private readonly ITemplateLoader? _loader;
    private readonly Func<IReadOnlyList<ResultDescriptor>> _provider;
    private readonly string _projectRoot;

    public ResultsGenerator(ITemplateRenderer renderer, Func<IReadOnlyList<ResultDescriptor>> provider, ITemplateLoader? loader = null, string? projectRoot = null)
    {
        _renderer = renderer;
        _provider = provider;
        _loader = loader;
        _projectRoot = projectRoot ?? Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// Generates high-level Result records (one per stored procedure logical operation) into per-schema folders.
    /// File naming: [ProcedureName]Result.cs (distinct from row result set records named _[ResultSetName]Result.cs).
    /// Namespace: {ns}.{schema}
    /// </summary>
    public int Generate(string ns, string baseOutputDir)
    {
        var results = _provider();
        if (results.Count == 0) return 0;
        string header = string.Empty;
        if (_loader != null && _loader.TryLoad("_Header", out var headerTpl)) header = headerTpl.TrimEnd() + Environment.NewLine;
        string? template = null;
        if (_loader != null && _loader.TryLoad("ResultRecord", out var tpl)) template = tpl;
        var written = 0;
        foreach (var res in results.OrderBy(r => r.OperationName))
        {
            var split = NamePolicy.SplitSchema(res.OperationName);
            var schema = split.Schema;
            var procPart = split.Operation;
            var typeName = procPart + "Result"; // differentiate from _[ResultSetName]Result row types
            var schemaDir = Path.Combine(baseOutputDir, schema);
            Directory.CreateDirectory(schemaDir);
            var model = new
            {
                Namespace = ns + "." + schema,
                OperationName = res.OperationName,
                TypeName = typeName,
                PayloadType = res.PayloadType,
                HEADER = header
            };
            string code;
            if (template != null)
            {
                code = _renderer.Render(template, model);
            }
            else
            {
                var sb = new StringBuilder();
                sb.Append(header);
                sb.AppendLine($"namespace {ns}.{schema};");
                sb.AppendLine();
                sb.AppendLine($"public readonly record struct {typeName}(bool Success, string? Error, {res.PayloadType}? Value);");
                code = sb.ToString();
            }
            File.WriteAllText(Path.Combine(schemaDir, typeName + ".cs"), code);
            written++;
        }
        return written;
    }
}
