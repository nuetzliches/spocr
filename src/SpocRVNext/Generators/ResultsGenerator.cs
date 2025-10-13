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

    public int Generate(string ns, string outputSubDir = "Results")
    {
        var results = _provider();
        if (results.Count == 0) return 0;
        string header = string.Empty;
        if (_loader != null && _loader.TryLoad("_Header", out var headerTpl)) header = headerTpl.TrimEnd() + Environment.NewLine;
        string? template = null;
        if (_loader != null && _loader.TryLoad("ResultRecord", out var tpl)) template = tpl;
        var outDir = Path.Combine(_projectRoot, ns.Split('.').First(), outputSubDir);
        Directory.CreateDirectory(outDir);
        var written = 0;
        foreach (var res in results.OrderBy(r => r.OperationName))
        {
            var typeName = NamePolicy.Result(res.OperationName);
            var model = new
            {
                Namespace = ns,
                OperationName = res.OperationName,
                TypeName = typeName,
                PayloadType = res.PayloadType,
                HEADER = header
            };
            string code;
            if (template != null)
                code = _renderer.Render(template, model);
            else
            {
                var sb = new StringBuilder();
                sb.Append(header);
                sb.AppendLine($"namespace {ns}.Results;");
                sb.AppendLine();
                sb.AppendLine($"public readonly record struct {typeName}(bool Success, string? Error, {res.PayloadType}? Value);");
                code = sb.ToString();
            }
            File.WriteAllText(Path.Combine(outDir, typeName + ".cs"), code);
            written++;
        }
        return written;
    }
}
