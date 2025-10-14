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

    public int Generate(string ns, string baseOutputDir)
    {
        var procs = _provider();
        if (procs.Count == 0) return 0;
        string header = string.Empty;
        if (_loader != null && _loader.TryLoad("_Header", out var headerTpl)) header = headerTpl.TrimEnd() + Environment.NewLine;
        string? procedureTemplate = null;
        if (_loader != null && _loader.TryLoad("StoredProcedure", out var spTpl)) procedureTemplate = spTpl;
        var written = 0;
        foreach (var proc in procs.OrderBy(p => p.OperationName))
        {
            var op = proc.OperationName;
            string schemaPart = proc.Schema ?? "dbo";
            string procPart = op;
            var idx = op.IndexOf('.');
            if (idx > 0)
            {
                schemaPart = op.Substring(0, idx);
                procPart = op[(idx + 1)..];
            }
            var schemaPascal = ToPascalCase(schemaPart);
            var finalNs = ns + "." + schemaPascal;
            var schemaDir = Path.Combine(baseOutputDir, schemaPascal);
            Directory.CreateDirectory(schemaDir);
            var aggregateResultType = NamePolicy.Result(procPart) + "Aggregate"; // separate from simple Result record
            var procedureTypeName = NamePolicy.Procedure(procPart);
            // Generate row records per ResultSet
            var rsDir = schemaDir;
            foreach (var rs in proc.ResultSets.OrderBy(r => r.Index))
            {
                // Row type name keeps existing logic (Proc + RsName + Row)
                var rowTypeName = NamePolicy.Row(procPart, rs.Name);
                var sbRow = new StringBuilder();
                sbRow.Append(header);
                sbRow.AppendLine($"namespace {finalNs};");
                sbRow.AppendLine();
                sbRow.AppendLine($"public readonly record struct {rowTypeName}(");
                for (int i = 0; i < rs.Fields.Count; i++)
                {
                    var f = rs.Fields[i];
                    var comma = i == rs.Fields.Count - 1 ? string.Empty : ",";
                    sbRow.AppendLine($"    {f.ClrType} {f.PropertyName}{comma}");
                }
                sbRow.AppendLine(");");
                // File name rule refinement:
                // Old: _[ResultSetName]Result.cs
                // New: [ProcName][ResultSetNameOrIndex].cs (no leading underscore, no 'ResultSet' text in fallback)
                // If rs.Name was an auto fallback like ResultSet1 keep only the index portion (1)
                string simplifiedRsName = rs.Name.StartsWith("ResultSet", StringComparison.OrdinalIgnoreCase)
                    ? rs.Name.Substring("ResultSet".Length) // e.g. ResultSet1 -> 1
                    : rs.Name;
                var fileBase = procPart + simplifiedRsName + "Result"; // ensure distinct from high-level [Proc]Result and includes 'Result' suffix
                File.WriteAllText(Path.Combine(rsDir, fileBase + ".cs"), sbRow.ToString());
                written++;
            }
            // Optional Output record
            if (proc.OutputFields.Count > 0)
            {
                var outputTypeName = NamePolicy.Output(procPart);
                var sbOut = new StringBuilder();
                sbOut.Append(header);
                sbOut.AppendLine($"namespace {finalNs};");
                sbOut.AppendLine();
                sbOut.AppendLine($"public readonly record struct {outputTypeName}(");
                for (int i = 0; i < proc.OutputFields.Count; i++)
                {
                    var f = proc.OutputFields[i];
                    var comma = i == proc.OutputFields.Count - 1 ? string.Empty : ",";
                    sbOut.AppendLine($"    {f.ClrType} {f.PropertyName}{comma}");
                }
                sbOut.AppendLine(");");
                File.WriteAllText(Path.Combine(rsDir, procPart + "Output.cs"), sbOut.ToString());
                written++;
            }
            // Aggregate result class
            var aggSb = new StringBuilder();
            aggSb.Append(header);
            aggSb.AppendLine($"namespace {finalNs};");
            aggSb.AppendLine();
            aggSb.AppendLine($"public sealed class {aggregateResultType}");
            aggSb.AppendLine("{");
            aggSb.AppendLine("    public bool Success { get; init; }");
            aggSb.AppendLine("    public string? Error { get; init; }");
            if (proc.OutputFields.Count > 0)
            {
                var outputTypeName = NamePolicy.Output(procPart);
                aggSb.AppendLine($"    public {outputTypeName}? Output {{ get; init; }}");
            }
            foreach (var rs in proc.ResultSets.OrderBy(r => r.Index))
            {
                var rowTypeName = NamePolicy.Row(procPart, rs.Name);
                aggSb.AppendLine($"    public System.Collections.Generic.IReadOnlyList<{rowTypeName}> {rs.Name} {{ get; init; }} = System.Array.Empty<{rowTypeName}>();");
            }
            aggSb.AppendLine("}");
            File.WriteAllText(Path.Combine(rsDir, procPart + "Aggregate.cs"), aggSb.ToString());
            written++;
            // Procedure wrapper
            // Execution plan + wrapper
            var planTypeName = procedureTypeName + "Plan";
            var planSb = new StringBuilder();
            planSb.Append(header);
            planSb.AppendLine($"namespace {finalNs};");
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
                planSb.AppendLine($"            new(\"@{ip.Name}\", {MapDbType(ip.SqlTypeName)}, {EmitSize(ip)}, false, {ip.IsNullable.ToString().ToLowerInvariant()}),");
            }
            foreach (var outParam in proc.OutputFields)
            {
                planSb.AppendLine($"            new(\"@{outParam.Name}\", {MapDbType(outParam.SqlTypeName)}, {EmitSize(outParam)}, true, {outParam.IsNullable.ToString().ToLowerInvariant()}),");
            }
            planSb.AppendLine("        };\n");
            // ResultSet materializers (with ordinal caching)
            planSb.AppendLine("        var resultSets = new SpocR.SpocRVNext.Execution.ResultSetMapping[] {");
            foreach (var rs in proc.ResultSets.OrderBy(r => r.Index))
            {
                var rowTypeName = NamePolicy.Row(procPart, rs.Name);
                var ordinalDecls = string.Join(" ", rs.Fields.Select((f, idx) => $"int o{idx}=r.GetOrdinal(\"{f.Name}\");"));
                var fieldExprs = string.Join(", ", rs.Fields.Select((f, idx) => MaterializeFieldExpressionCached(f, idx)));
                planSb.AppendLine("            new(\"" + rs.Name + "\", async (r, ct) => { var list = new System.Collections.Generic.List<object>(); " + ordinalDecls + " while (await r.ReadAsync(ct).ConfigureAwait(false)) { list.Add(new " + rowTypeName + "(" + fieldExprs + ")); } return list; }),");
            }
            planSb.AppendLine("        };\n");
            // Output factory
            if (proc.OutputFields.Count > 0)
            {
                var outputTypeName = NamePolicy.Output(procPart);
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
                planSb.Append(", Output = (" + NamePolicy.Output(procPart) + "?)output");
            }
            int rsCounter = 0;
            foreach (var rs in proc.ResultSets.OrderBy(r => r.Index))
            {
                var rowTypeName = NamePolicy.Row(procPart, rs.Name);
                planSb.Append($", {rs.Name} = rs.Length > {rsCounter} ? System.Array.ConvertAll(rs[{rsCounter}].ToArray(), o => ({rowTypeName})o).ToList() : System.Array.Empty<{rowTypeName}>() ");
                rsCounter++;
            }
            planSb.Append("};\n");
            planSb.AppendLine();
            // Input binder lambda (object? state expected to be input record)
            if (proc.InputParameters.Count > 0)
            {
                var inputType = NamePolicy.Input(procPart);
                planSb.AppendLine($"        void Binder(DbCommand cmd, object? state) {{ var input = ({inputType})state!; ");
                foreach (var ip in proc.InputParameters)
                {
                    planSb.AppendLine($"            cmd.Parameters[\"@{ip.Name}\"].Value = input.{ip.PropertyName};");
                }
                planSb.AppendLine("        }");
            }
            else
            {
                planSb.AppendLine("        void Binder(DbCommand cmd, object? state) { }");
            }
            planSb.AppendLine("        return new SpocR.SpocRVNext.Execution.ProcedureExecutionPlan(");
            planSb.AppendLine($"            \"{proc.Schema}.{proc.ProcedureName}\", parameters, resultSets, OutputFactory, AggregateFactory, Binder);\n    }}\n}}");
            File.WriteAllText(Path.Combine(rsDir, procPart + "Plan.cs"), planSb.ToString());
            written++;

            // Wrapper
            var wrapperSb = new StringBuilder();
            wrapperSb.Append(header);
            wrapperSb.AppendLine($"namespace {finalNs};");
            wrapperSb.AppendLine();
            wrapperSb.AppendLine("using System.Data.Common;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing SpocR.SpocRVNext.Execution;");
            wrapperSb.AppendLine($"public static class {procedureTypeName}\n{{");
            wrapperSb.AppendLine($"    public const string Name = \"{proc.Schema}.{proc.ProcedureName}\";");
            // ExecuteAsync with input record (if any)
            var inputParamSignature = proc.InputParameters.Count > 0 ? $", {NamePolicy.Input(procPart)} input" : string.Empty;
            wrapperSb.AppendLine($"    public static Task<{aggregateResultType}> ExecuteAsync(DbConnection connection{inputParamSignature}, CancellationToken cancellationToken = default)\n    {{");
            // Parameter value binding (override defaults)
            var stateArg = proc.InputParameters.Count > 0 ? "input" : "null";
            if (proc.InputParameters.Count > 0)
            {
                wrapperSb.AppendLine($"        return ProcedureExecutor.ExecuteAsync<{aggregateResultType}>(connection, {planTypeName}.Instance, input, cancellationToken);");
            }
            else
            {
                wrapperSb.AppendLine($"        return ProcedureExecutor.ExecuteAsync<{aggregateResultType}>(connection, {planTypeName}.Instance, null, cancellationToken);");
            }
            wrapperSb.AppendLine("    }");
            wrapperSb.AppendLine("}");
            File.WriteAllText(Path.Combine(rsDir, procPart + "Procedure.cs"), wrapperSb.ToString());
            written++;
        }
        return written;
    }

    private static string MapDbType(string sqlType)
    {
        if (string.IsNullOrWhiteSpace(sqlType)) return "null";
        var t = sqlType.ToLowerInvariant();
        // normalize common parentheses like nvarchar(50)
        if (t.Contains('(')) t = t[..t.IndexOf('(')];
        return t switch
        {
            "int" => "System.Data.DbType.Int32",
            "bigint" => "System.Data.DbType.Int64",
            "smallint" => "System.Data.DbType.Int16",
            "tinyint" => "System.Data.DbType.Byte",
            "bit" => "System.Data.DbType.Boolean",
            "decimal" or "numeric" or "money" or "smallmoney" => "System.Data.DbType.Decimal",
            "float" => "System.Data.DbType.Double",
            "real" => "System.Data.DbType.Single",
            "date" or "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" or "time" => "System.Data.DbType.DateTime2",
            "uniqueidentifier" => "System.Data.DbType.Guid",
            "varbinary" or "binary" or "image" => "System.Data.DbType.Binary",
            "xml" => "System.Data.DbType.Xml",
            // treat all character & text types as string
            "varchar" or "nvarchar" or "char" or "nchar" or "text" or "ntext" => "System.Data.DbType.String",
            _ => "System.Data.DbType.String"
        };
    }

    private static string EmitSize(FieldDescriptor f)
        => f.MaxLength.HasValue && f.MaxLength.Value > 0 ? f.MaxLength.Value.ToString() : "null";

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

    private static string MaterializeFieldExpressionCached(FieldDescriptor f, int ordinalIndex)
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
        if (accessor == null)
        {
            if (f.ClrType.StartsWith("byte[]"))
                return $"r.IsDBNull(o{ordinalIndex}) ? System.Array.Empty<byte>() : (byte[])r.GetValue(o{ordinalIndex})";
            if (f.ClrType == "string")
                return $"r.IsDBNull(o{ordinalIndex}) ? string.Empty : r.GetString(o{ordinalIndex})";
            if (f.ClrType == "string?")
                return $"r.IsDBNull(o{ordinalIndex}) ? null : r.GetString(o{ordinalIndex})";
            return $"r.GetValue(o{ordinalIndex})";
        }
        var nullable = f.IsNullable && !f.ClrType.EndsWith("?") ? true : f.ClrType.EndsWith("?");
        if (nullable)
            return $"r.IsDBNull(o{ordinalIndex}) ? null : ({f.ClrType})r.{accessor}(o{ordinalIndex})";
        return $"r.{accessor}(o{ordinalIndex})";
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
