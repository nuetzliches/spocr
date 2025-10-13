using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using SpocR.SpocRVNext.Engine;
using SpocR.SpocRVNext.Metadata;
using SpocR.SpocRVNext.Utils;

namespace SpocR.SpocRVNext.Generators;

public sealed class OutputsGenerator
{
    private readonly ITemplateRenderer _renderer;
    private readonly ITemplateLoader? _loader;
    private readonly Func<IReadOnlyList<OutputDescriptor>> _provider;
    private readonly string _projectRoot;

    public OutputsGenerator(ITemplateRenderer renderer, Func<IReadOnlyList<OutputDescriptor>> provider, ITemplateLoader? loader = null, string? projectRoot = null)
    {
        _renderer = renderer;
        _provider = provider;
        _loader = loader;
        _projectRoot = projectRoot ?? Directory.GetCurrentDirectory();
    }

    public int Generate(string ns, string outputSubDir = "Outputs")
    {
        var outputs = _provider();
        if (outputs.Count == 0) return 0;
        string header = string.Empty;
        if (_loader != null && _loader.TryLoad("_Header", out var headerTpl)) header = headerTpl.TrimEnd() + Environment.NewLine;
        string? template = null;
        if (_loader != null && _loader.TryLoad("OutputRecord", out var tpl)) template = tpl;
        var outDir = Path.Combine(_projectRoot, ns.Split('.').First(), outputSubDir);
        Directory.CreateDirectory(outDir);
        var written = 0;
        foreach (var output in outputs.OrderBy(o => o.OperationName))
        {
            var typeName = NamePolicy.Output(output.OperationName);
            var model = new
            {
                Namespace = ns,
                OperationName = output.OperationName,
                TypeName = typeName,
                FieldCount = output.Fields.Count,
                Fields = output.Fields.Select((f, idx) => new { f.ClrType, f.PropertyName, Separator = idx == output.Fields.Count - 1 ? string.Empty : "," }).ToList(),
                HEADER = header
            };
            string code;
            if (template != null)
                code = _renderer.Render(template, model);
            else
            {
                var sb = new StringBuilder();
                sb.Append(header);
                sb.AppendLine($"namespace {ns}.Outputs;");
                sb.AppendLine();
                sb.AppendLine($"public readonly record struct {typeName}(");
                for (int i = 0; i < output.Fields.Count; i++)
                {
                    var f = output.Fields[i];
                    var comma = i == output.Fields.Count - 1 ? string.Empty : ",";
                    sb.AppendLine($"    {f.ClrType} {f.PropertyName}{comma}");
                }
                sb.AppendLine(");");
                code = sb.ToString();
            }
            File.WriteAllText(Path.Combine(outDir, typeName + ".cs"), code);
            written++;
        }
        return written;
    }
}
