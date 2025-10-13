using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using SpocR.SpocRVNext.Engine;
using SpocR.SpocRVNext.Metadata;
using SpocR.SpocRVNext.Utils;

namespace SpocR.SpocRVNext.Generators;

public sealed class ProceduresGenerator
{
    private readonly ITemplateRenderer _renderer;
    private readonly ITemplateLoader? _loader;
    private readonly Func<IReadOnlyList<ProcedureDescriptor>> _provider;
    private readonly string _projectRoot;

    public ProceduresGenerator(ITemplateRenderer renderer, Func<IReadOnlyList<ProcedureDescriptor>> provider, ITemplateLoader? loader = null, string? projectRoot = null)
    {
        _renderer = renderer;
        _provider = provider;
        _loader = loader;
        _projectRoot = projectRoot ?? Directory.GetCurrentDirectory();
    }

    public int Generate(string ns, string outputSubDir = "Procedures")
    {
        var procs = _provider();
        if (procs.Count == 0) return 0;
        string header = string.Empty;
        if (_loader != null && _loader.TryLoad("_Header", out var headerTpl)) header = headerTpl.TrimEnd() + Environment.NewLine;
        string? procedureTemplate = null;
        if (_loader != null && _loader.TryLoad("StoredProcedure", out var spTpl)) procedureTemplate = spTpl;
        var rootDir = Path.Combine(_projectRoot, ns.Split('.').First(), outputSubDir);
        Directory.CreateDirectory(rootDir);
        var written = 0;
        foreach (var proc in procs.OrderBy(p => p.OperationName))
        {
            var aggregateResultType = NamePolicy.Result(proc.OperationName) + "Aggregate"; // separate from simple Result record
            var procedureTypeName = NamePolicy.Procedure(proc.OperationName);
            // Generate row records per ResultSet
            var rsDir = rootDir; // same folder for now
            foreach (var rs in proc.ResultSets.OrderBy(r => r.Index))
            {
                var rowTypeName = NamePolicy.Row(proc.OperationName, rs.Name);
                var sbRow = new StringBuilder();
                sbRow.Append(header);
                sbRow.AppendLine($"namespace {ns}.Procedures;");
                sbRow.AppendLine();
                sbRow.AppendLine($"public readonly record struct {rowTypeName}(");
                for (int i=0;i<rs.Fields.Count;i++)
                {
                    var f = rs.Fields[i];
                    var comma = i == rs.Fields.Count - 1 ? string.Empty : ",";
                    sbRow.AppendLine($"    {f.ClrType} {f.PropertyName}{comma}");
                }
                sbRow.AppendLine(");");
                File.WriteAllText(Path.Combine(rsDir, rowTypeName + ".cs"), sbRow.ToString());
                written++;
            }
            // Optional Output record
            if (proc.OutputFields.Count > 0)
            {
                var outputTypeName = NamePolicy.Output(proc.OperationName);
                var sbOut = new StringBuilder();
                sbOut.Append(header);
                sbOut.AppendLine($"namespace {ns}.Procedures;");
                sbOut.AppendLine();
                sbOut.AppendLine($"public readonly record struct {outputTypeName}(");
                for (int i=0;i<proc.OutputFields.Count;i++)
                {
                    var f = proc.OutputFields[i];
                    var comma = i == proc.OutputFields.Count - 1 ? string.Empty : ",";
                    sbOut.AppendLine($"    {f.ClrType} {f.PropertyName}{comma}");
                }
                sbOut.AppendLine(");");
                File.WriteAllText(Path.Combine(rsDir, outputTypeName + ".cs"), sbOut.ToString());
                written++;
            }
            // Aggregate result class
            var aggSb = new StringBuilder();
            aggSb.Append(header);
            aggSb.AppendLine($"namespace {ns}.Procedures;");
            aggSb.AppendLine();
            aggSb.AppendLine($"public sealed class {aggregateResultType}");
            aggSb.AppendLine("{");
            aggSb.AppendLine("    public bool Success { get; init; }");
            aggSb.AppendLine("    public string? Error { get; init; }");
            if (proc.OutputFields.Count > 0)
            {
                var outputTypeName = NamePolicy.Output(proc.OperationName);
                aggSb.AppendLine($"    public {outputTypeName}? Output {{ get; init; }}");
            }
            foreach (var rs in proc.ResultSets.OrderBy(r => r.Index))
            {
                var rowTypeName = NamePolicy.Row(proc.OperationName, rs.Name);
                aggSb.AppendLine($"    public System.Collections.Generic.IReadOnlyList<{rowTypeName}> {rs.Name} {{ get; init; }} = System.Array.Empty<{rowTypeName}>();");
            }
            aggSb.AppendLine("}");
            File.WriteAllText(Path.Combine(rsDir, aggregateResultType + ".cs"), aggSb.ToString());
            written++;
            // Procedure wrapper
            string procCode;
            if (procedureTemplate != null)
            {
                var model = new { Namespace = ns, ProcedureName = proc.ProcedureName, Schema = proc.Schema, TypeName = procedureTypeName, AggregateResultType = aggregateResultType, HEADER = header };
                procCode = _renderer.Render(procedureTemplate, model);
            }
            else
            {
                var body = new StringBuilder();
                body.AppendLine("        // TODO: generated execution logic (ADO.NET) will be inserted here");
                body.AppendLine($"        return System.Threading.Tasks.Task.FromResult(new {aggregateResultType} {{ Success = true }});");
                procCode = $"{header}namespace {ns}.Procedures;\n\npublic static class {procedureTypeName}\n{{\n    public const string Name = \"{proc.Schema}.{proc.ProcedureName}\";\n    public static System.Threading.Tasks.Task<{aggregateResultType}> ExecuteAsync(DbConnection connection, System.Threading.CancellationToken cancellationToken = default)\n    {{\n{body.ToString()}    }}\n}}\n";
            }
            File.WriteAllText(Path.Combine(rsDir, procedureTypeName + ".cs"), procCode);
            written++;
        }
        return written;
    }
}
