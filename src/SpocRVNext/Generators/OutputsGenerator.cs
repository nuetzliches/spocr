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

    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var parts = input.Split(new[] { '-', '_', ' ', '.', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Select(p => char.ToUpperInvariant(p[0]) + (p.Length > 1 ? p.Substring(1).ToLowerInvariant() : string.Empty));
        var candidate = string.Concat(parts);
        candidate = new string(candidate.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
        if (string.IsNullOrEmpty(candidate)) candidate = "Schema";
        if (char.IsDigit(candidate[0])) candidate = "N" + candidate;
        return candidate;
    }
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

    public int Generate(string ns, string baseOutputDir)
    {
        var outputs = _provider();
        if (outputs.Count == 0) return 0;
        string header = string.Empty;
        if (_loader != null && _loader.TryLoad("_Header", out var headerTpl)) header = headerTpl.TrimEnd() + Environment.NewLine;
        string? template = null;
        if (_loader != null && _loader.TryLoad("OutputRecord", out var tpl)) template = tpl;
        var written = 0;
        foreach (var output in outputs.OrderBy(o => o.OperationName))
        {
            var op = output.OperationName;
            string schemaPart = "dbo";
            string procPart = op;
            var idx = op.IndexOf('.');
            if (idx > 0)
            {
                schemaPart = op.Substring(0, idx);
                procPart = op[(idx + 1)..];
            }
            var schemaPascal = ToPascalCase(schemaPart);
            var schemaDir = Path.Combine(baseOutputDir, schemaPascal);
            Directory.CreateDirectory(schemaDir);
            var typeName = NamePolicy.Output(procPart);
            var model = new
            {
                Namespace = ns + "." + schemaPascal,
                OperationName = procPart,
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
                sb.AppendLine($"namespace {ns}.{schemaPascal};");
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
            File.WriteAllText(Path.Combine(schemaDir, procPart + "Output.cs"), code);
            written++;
        }
        return written;
    }
}
