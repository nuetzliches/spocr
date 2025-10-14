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

    public int Generate(string ns, string baseOutputDir)
    {
        var inputs = _provider();
        if (inputs.Count == 0) return 0;
        string header = string.Empty;
        if (_loader != null && _loader.TryLoad("_Header", out var headerTpl)) header = headerTpl.TrimEnd() + Environment.NewLine;
        string? template = null;
        if (_loader != null && _loader.TryLoad("InputRecord", out var tpl)) template = tpl;
        var written = 0;
        foreach (var input in inputs.OrderBy(i => i.OperationName))
        {
            // Expect OperationName encoded as Schema.ProcName or store schema separately (here assume Schema__Proc fallback)
            var op = input.OperationName;
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
            var typeName = NamePolicy.Input(procPart);
            var finalNs = ns + "." + schemaPascal;
            var model = new
            {
                Namespace = finalNs,
                OperationName = procPart,
                TypeName = typeName,
                ParameterCount = input.Fields.Count,
                Parameters = input.Fields.Select((f, idx2) => new { f.ClrType, f.PropertyName, Separator = idx2 == input.Fields.Count - 1 ? string.Empty : "," }).ToList(),
                HEADER = header
            };
            string code;
            if (template != null)
                code = _renderer.Render(template, model);
            else
            {
                var sb = new StringBuilder();
                sb.Append(header);
                sb.AppendLine($"namespace {finalNs};");
                sb.AppendLine();
                sb.AppendLine($"public readonly record struct {typeName}(");
                for (int i = 0; i < input.Fields.Count; i++)
                {
                    var f = input.Fields[i];
                    var comma = i == input.Fields.Count - 1 ? string.Empty : ",";
                    sb.AppendLine($"    {f.ClrType} {f.PropertyName}{comma}");
                }
                sb.AppendLine(");");
                code = sb.ToString();
            }
            // File pattern: [sp-name]Input.cs
            File.WriteAllText(Path.Combine(schemaDir, procPart + "Input.cs"), code);
            written++;
        }
        return written;
    }

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
}
