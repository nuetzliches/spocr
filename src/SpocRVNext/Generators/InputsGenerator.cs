using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using SpocR.SpocRVNext.Engine;
using SpocR.SpocRVNext.Metadata;
using SpocR.SpocRVNext.Utils;

namespace SpocR.SpocRVNext.Generators;

public sealed class InputsGenerator
{
    private readonly ITemplateRenderer _renderer;
    private readonly ITemplateLoader? _loader;
    private readonly Func<IReadOnlyList<InputDescriptor>> _provider;
    private readonly string _projectRoot;

    public InputsGenerator(ITemplateRenderer renderer, Func<IReadOnlyList<InputDescriptor>> provider, ITemplateLoader? loader = null, string? projectRoot = null)
    {
        _renderer = renderer;
        _provider = provider;
        _loader = loader;
        _projectRoot = projectRoot ?? Directory.GetCurrentDirectory();
    }

    public int Generate(string ns, string outputSubDir = "Inputs")
    {
        var inputs = _provider();
        if (inputs.Count == 0) return 0;
        string header = string.Empty;
        if (_loader != null && _loader.TryLoad("_Header", out var headerTpl)) header = headerTpl.TrimEnd() + Environment.NewLine;
        string? template = null;
        if (_loader != null && _loader.TryLoad("InputRecord", out var tpl)) template = tpl;
        var outDir = Path.Combine(_projectRoot, ns.Split('.').First(), outputSubDir); // simple path base
        Directory.CreateDirectory(outDir);
        var written = 0;
        foreach (var input in inputs.OrderBy(i => i.OperationName))
        {
            var typeName = NamePolicy.Input(input.OperationName);
            var model = new {
                Namespace = ns,
                OperationName = input.OperationName,
                TypeName = typeName,
                ParameterCount = input.Fields.Count,
                Parameters = input.Fields.Select((f,idx) => new { f.ClrType, f.PropertyName, Separator = idx == input.Fields.Count - 1 ? string.Empty : "," }).ToList(),
                HEADER = header
            };
            string code;
            if (template != null)
                code = _renderer.Render(template, model);
            else
            {
                var sb = new StringBuilder();
                sb.Append(header);
                sb.AppendLine($"namespace {ns}.Inputs;");
                sb.AppendLine();
                sb.AppendLine($"public readonly record struct {typeName}(");
                for (int i=0;i<input.Fields.Count;i++)
                {
                    var f = input.Fields[i];
                    var comma = i == input.Fields.Count - 1 ? string.Empty : ",";
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
