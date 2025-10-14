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
            var procedureTypeName = NamePolicy.Procedure(procPart);
            var unifiedResultTypeName = NamePolicy.Result(procPart); // <Proc>Result
            var fileSb = new StringBuilder();
            fileSb.Append(header);
            fileSb.AppendLine($"namespace {finalNs};");
            fileSb.AppendLine();
            fileSb.AppendLine("using System;\nusing System.Collections.Generic;\nusing System.Data;\nusing System.Data.Common;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing SpocR.SpocRVNext.Execution;");
            // Begin unified result container
            fileSb.AppendLine($"public sealed class {unifiedResultTypeName}");
            fileSb.AppendLine("{");
            fileSb.AppendLine("    public bool Success { get; init; }");
            fileSb.AppendLine("    public string? Error { get; init; }");
            if (proc.OutputFields.Count > 0)
            {
                var outputTypeName = NamePolicy.Output(procPart);
                // inline output record type inside same file (still public for reuse)
                fileSb.AppendLine($"    public {outputTypeName}? Output {{ get; init; }}");
            }
            foreach (var rs in proc.ResultSets.OrderBy(r => r.Index))
            {
                var rsType = NamePolicy.ResultSet(procPart, rs.Name);
                fileSb.AppendLine($"    public IReadOnlyList<{rsType}> {rs.Name} {{ get; init; }} = Array.Empty<{rsType}>();");
            }
            fileSb.AppendLine("}");
            fileSb.AppendLine();
            // Inline output record if present
            if (proc.OutputFields.Count > 0)
            {
                var outputTypeName = NamePolicy.Output(procPart);
                fileSb.AppendLine($"public readonly record struct {outputTypeName}(");
                for (int i = 0; i < proc.OutputFields.Count; i++)
                {
                    var f = proc.OutputFields[i];
                    var comma = i == proc.OutputFields.Count - 1 ? string.Empty : ",";
                    fileSb.AppendLine($"    {f.ClrType} {f.PropertyName}{comma}");
                }
                fileSb.AppendLine(");");
                fileSb.AppendLine();
            }
            // Inline result set record structs
            foreach (var rs in proc.ResultSets.OrderBy(r => r.Index))
            {
                var rsType = NamePolicy.ResultSet(procPart, rs.Name);
                fileSb.AppendLine($"public readonly record struct {rsType}(");
                for (int i = 0; i < rs.Fields.Count; i++)
                {
                    var f = rs.Fields[i];
                    var comma = i == rs.Fields.Count - 1 ? string.Empty : ",";
                    fileSb.AppendLine($"    {f.ClrType} {f.PropertyName}{comma}");
                }
                fileSb.AppendLine(");");
                fileSb.AppendLine();
            }
            // Execution plan (internal) + wrapper for ExecuteAsync at bottom of same file
            var planTypeName = procedureTypeName + "Plan";
            fileSb.AppendLine($"internal static partial class {planTypeName}\n{{");
            fileSb.AppendLine("    private static ProcedureExecutionPlan? _cached;");
            fileSb.AppendLine("    public static ProcedureExecutionPlan Instance => _cached ??= Create();");
            fileSb.AppendLine("    private static ProcedureExecutionPlan Create()\n    {");
            fileSb.AppendLine("        var parameters = new ProcedureParameter[] {");
            foreach (var ip in proc.InputParameters)
            {
                fileSb.AppendLine($"            new(\"@{ip.Name}\", {MapDbType(ip.SqlTypeName)}, {EmitSize(ip)}, false, {ip.IsNullable.ToString().ToLowerInvariant()}),");
            }
            foreach (var outParam in proc.OutputFields)
            {
                fileSb.AppendLine($"            new(\"@{outParam.Name}\", {MapDbType(outParam.SqlTypeName)}, {EmitSize(outParam)}, true, {outParam.IsNullable.ToString().ToLowerInvariant()}),");
            }
            fileSb.AppendLine("        };\n");
            fileSb.AppendLine("        var resultSets = new ResultSetMapping[] {");
            foreach (var rs in proc.ResultSets.OrderBy(r => r.Index))
            {
                var rsType = NamePolicy.ResultSet(procPart, rs.Name);
                var ordinalDecls = string.Join(" ", rs.Fields.Select((f, idx) => $"int o{idx}=r.GetOrdinal(\"{f.Name}\");"));
                var fieldExprs = string.Join(", ", rs.Fields.Select((f, idx) => MaterializeFieldExpressionCached(f, idx)));
                fileSb.AppendLine("            new(\"" + rs.Name + "\", async (r, ct) => { var list = new List<object>(); " + ordinalDecls + " while (await r.ReadAsync(ct).ConfigureAwait(false)) { list.Add(new " + rsType + "(" + fieldExprs + ")); } return list; }),");
            }
            fileSb.AppendLine("        };\n");
            if (proc.OutputFields.Count > 0)
            {
                var outputTypeName = NamePolicy.Output(procPart);
                fileSb.AppendLine($"        object? OutputFactory(IReadOnlyDictionary<string, object?> values) => new {outputTypeName}(" + string.Join(", ", proc.OutputFields.Select(f => CastOutputValue(f))) + ");");
            }
            else
            {
                fileSb.AppendLine("        object? OutputFactory(IReadOnlyDictionary<string, object?> values) => null;");
            }
            fileSb.Append("        object AggregateFactory(bool success, string? error, object? output, IReadOnlyDictionary<string, object?> outputs, object[] rs) => new ");
            fileSb.Append(unifiedResultTypeName + " { Success = success, Error = error");
            if (proc.OutputFields.Count > 0)
            {
                fileSb.Append(", Output = (" + NamePolicy.Output(procPart) + "?)output");
            }
            int unifiedCounter = 0;
            foreach (var rs in proc.ResultSets.OrderBy(r => r.Index))
            {
                var rsType = NamePolicy.ResultSet(procPart, rs.Name);
                fileSb.Append($", {rs.Name} = rs.Length > {unifiedCounter} ? Array.ConvertAll(rs[{unifiedCounter}].ToArray(), o => ({rsType})o).ToList() : Array.Empty<{rsType}>() ");
                unifiedCounter++;
            }
            fileSb.Append("};\n");
            if (proc.InputParameters.Count > 0)
            {
                var inputType = NamePolicy.Input(procPart);
                fileSb.AppendLine($"        void Binder(DbCommand cmd, object? state) {{ var input = ({inputType})state!; ");
                foreach (var ip in proc.InputParameters)
                {
                    fileSb.AppendLine($"            cmd.Parameters[\"@{ip.Name}\"].Value = input.{ip.PropertyName};");
                }
                fileSb.AppendLine("        }");
            }
            else
            {
                fileSb.AppendLine("        void Binder(DbCommand cmd, object? state) { }");
            }
            fileSb.AppendLine("        return new ProcedureExecutionPlan(");
            fileSb.AppendLine($"            \"{proc.Schema}.{proc.ProcedureName}\", parameters, resultSets, OutputFactory, AggregateFactory, Binder);\n    }}\n}}");
            fileSb.AppendLine();
            // Wrapper
            fileSb.AppendLine($"public static class {procedureTypeName}\n{{");
            fileSb.AppendLine($"    public const string Name = \"{proc.Schema}.{proc.ProcedureName}\";");
            var inputSignature = proc.InputParameters.Count > 0 ? $", {NamePolicy.Input(procPart)} input" : string.Empty;
            fileSb.AppendLine($"    public static Task<{unifiedResultTypeName}> ExecuteAsync(DbConnection connection{inputSignature}, CancellationToken cancellationToken = default)\n    {{");
            if (proc.InputParameters.Count > 0)
            {
                fileSb.AppendLine($"        return ProcedureExecutor.ExecuteAsync<{unifiedResultTypeName}>(connection, {planTypeName}.Instance, input, cancellationToken);");
            }
            else
            {
                fileSb.AppendLine($"        return ProcedureExecutor.ExecuteAsync<{unifiedResultTypeName}>(connection, {planTypeName}.Instance, null, cancellationToken);");
            }
            fileSb.AppendLine("    }");
            fileSb.AppendLine("}");
            File.WriteAllText(Path.Combine(schemaDir, procPart + "Result.cs"), fileSb.ToString());
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
