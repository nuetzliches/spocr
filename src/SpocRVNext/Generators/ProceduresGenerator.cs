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
                for (int i = 0; i < rs.Fields.Count; i++)
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
                for (int i = 0; i < proc.OutputFields.Count; i++)
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
            // Execution plan + wrapper
            var planTypeName = procedureTypeName + "Plan";
            var planSb = new StringBuilder();
            planSb.Append(header);
            planSb.AppendLine($"namespace {ns}.Procedures;");
            planSb.AppendLine();
            planSb.AppendLine("using System;\nusing System.Collections.Generic;\nusing System.Data;\nusing System.Data.Common;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing SpocR.SpocRVNext.Execution;");
            // Build parameters array
            planSb.AppendLine($"internal static partial class {planTypeName}\n{{");
            planSb.AppendLine("    private static SpocR.SpocRVNext.Execution.ProcedureExecutionPlan? _cached;");
            planSb.AppendLine("    public static SpocR.SpocRVNext.Execution.ProcedureExecutionPlan Instance => _cached ??= Create();");
            planSb.AppendLine("    private static SpocR.SpocRVNext.Execution.ProcedureExecutionPlan Create()\n    {");
            // Parameters
            planSb.AppendLine("        var parameters = new SpocR.SpocRVNext.Execution.ProcedureParameter[] {");
            foreach (var ip in proc.InputParameters)
            {
                planSb.AppendLine($"            new(\"@{ip.Name}\", null, null, false, {ip.IsNullable.ToString().ToLowerInvariant()}),");
            }
            foreach (var op in proc.OutputFields)
            {
                planSb.AppendLine($"            new(\"@{op.Name}\", null, null, true, {op.IsNullable.ToString().ToLowerInvariant()}),");
            }
            planSb.AppendLine("        };\n");
            // ResultSet materializers
            planSb.AppendLine("        var resultSets = new SpocR.SpocRVNext.Execution.ResultSetMapping[] {");
            foreach (var rs in proc.ResultSets.OrderBy(r => r.Index))
            {
                var rowTypeName = NamePolicy.Row(proc.OperationName, rs.Name);
                planSb.AppendLine("            new(\"" + rs.Name + "\", async (r, ct) => { var list = new System.Collections.Generic.List<object>(); while (await r.ReadAsync(ct).ConfigureAwait(false)) { list.Add(new " + rowTypeName + "(" + string.Join(", ", rs.Fields.Select(f => MaterializeFieldExpression(f))) + ")); } return list; }),");
            }
            planSb.AppendLine("        };\n");
            // Output factory
            if (proc.OutputFields.Count > 0)
            {
                var outputTypeName = NamePolicy.Output(proc.OperationName);
                planSb.AppendLine($"        object? OutputFactory(System.Collections.Generic.IReadOnlyDictionary<string, object?> values) => new {outputTypeName}(" + string.Join(", ", proc.OutputFields.Select(f => CastOutputValue(f))) + ");");
            }
            else
            {
                planSb.AppendLine("        object? OutputFactory(System.Collections.Generic.IReadOnlyDictionary<string, object?> values) => null;");
            }
            // Aggregate factory
            planSb.Append("        object AggregateFactory(bool success, string? error, object? output, System.Collections.Generic.IReadOnlyDictionary<string, object?> outputs, object[] rs) => new ");
            planSb.Append(aggregateResultType + " { Success = success, Error = error");
            if (proc.OutputFields.Count > 0)
            {
                planSb.Append(", Output = (" + NamePolicy.Output(proc.OperationName) + "?)output");
            }
            int rsCounter = 0;
            foreach (var rs in proc.ResultSets.OrderBy(r => r.Index))
            {
                var rowTypeName = NamePolicy.Row(proc.OperationName, rs.Name);
                planSb.Append($", {rs.Name} = rs.Length > {rsCounter} ? System.Array.ConvertAll(rs[{rsCounter}].ToArray(), o => ({rowTypeName})o).ToList() : System.Array.Empty<{rowTypeName}>() ");
                rsCounter++;
            }
            planSb.Append("};\n");
            planSb.AppendLine();
            planSb.AppendLine("        return new SpocR.SpocRVNext.Execution.ProcedureExecutionPlan(\"");
            planSb.AppendLine($"            \"{proc.Schema}.{proc.ProcedureName}\", parameters, resultSets, OutputFactory, AggregateFactory);\n    }}\n}}");
            File.WriteAllText(Path.Combine(rsDir, planTypeName + ".cs"), planSb.ToString());
            written++;

            // Wrapper
            var wrapperSb = new StringBuilder();
            wrapperSb.Append(header);
            wrapperSb.AppendLine($"namespace {ns}.Procedures;");
            wrapperSb.AppendLine();
            wrapperSb.AppendLine("using System.Data.Common;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing SpocR.SpocRVNext.Execution;");
            wrapperSb.AppendLine($"public static class {procedureTypeName}\n{{");
            wrapperSb.AppendLine($"    public const string Name = \"{proc.Schema}.{proc.ProcedureName}\";");
            // ExecuteAsync with input record (if any)
            var inputParamSignature = proc.InputParameters.Count > 0 ? $", {NamePolicy.Input(proc.OperationName)} input" : string.Empty;
            wrapperSb.AppendLine($"    public static Task<{aggregateResultType}> ExecuteAsync(DbConnection connection{inputParamSignature}, CancellationToken cancellationToken = default)\n    {{");
            // Parameter value binding (override defaults)
            if (proc.InputParameters.Count > 0)
            {
                // NOTE: Actual parameter value assignment will be injected in a later iteration.
            }
            wrapperSb.AppendLine($"        return ProcedureExecutor.ExecuteAsync<{aggregateResultType}>(connection, {planTypeName}.Instance, cancellationToken);");
            wrapperSb.AppendLine("    }");
            wrapperSb.AppendLine("}");
            File.WriteAllText(Path.Combine(rsDir, procedureTypeName + ".cs"), wrapperSb.ToString());
            written++;
        }
        return written;
    }

    private static string MaterializeFieldExpression(FieldDescriptor f)
    {
        var accessor = f.ClrType switch
        {
            "int" or "int?" => "GetInt32",
            "long" or "long?" => "GetInt64",
            "short" or "short?" => "GetInt16",
            "byte" or "byte?" => "GetByte",
            "bool" or "bool?" => "GetBoolean",
            "decimal" or "decimal?" => "GetDecimal",
            "double" or "double?" => "GetDouble",
            "float" or "float?" => "GetFloat",
            "DateTime" or "DateTime?" => "GetDateTime",
            "Guid" or "Guid?" => "GetGuid",
            _ => null
        };
        var prop = f.PropertyName;
        if (accessor == null)
        {
            // string, byte[], fallback
            if (f.ClrType.StartsWith("byte[]"))
                return $"r.IsDBNull(r.GetOrdinal(\"{f.Name}\")) ? System.Array.Empty<byte>() : (byte[])r[\"{f.Name}\"]";
            if (f.ClrType == "string")
                return $"r.IsDBNull(r.GetOrdinal(\"{f.Name}\")) ? string.Empty : r.GetString(r.GetOrdinal(\"{f.Name}\"))";
            if (f.ClrType == "string?")
                return $"r.IsDBNull(r.GetOrdinal(\"{f.Name}\")) ? null : r.GetString(r.GetOrdinal(\"{f.Name}\"))";
            return $"r[\"{f.Name}\"]"; // generic
        }
        var nullable = f.IsNullable && !f.ClrType.EndsWith("?") ? true : f.ClrType.EndsWith("?");
        if (nullable)
            return $"r.IsDBNull(r.GetOrdinal(\"{f.Name}\")) ? null : ({f.ClrType})r.{accessor}(r.GetOrdinal(\"{f.Name}\"))";
        return $"r.{accessor}(r.GetOrdinal(\"{f.Name}\"))";
    }

    private static string CastOutputValue(FieldDescriptor f)
    {
        var target = f.ClrType;
        var name = f.Name.TrimStart('@');
        return target switch
        {
            "string" => $"values.TryGetValue(\"{name}\", out var v_{name}) ? (string?)v_{name} ?? string.Empty : string.Empty",
            _ => $"values.TryGetValue(\"{name}\", out var v_{name}) ? ({target})v_{name} : default"
        };
    }
}
