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
        // Emit ExecutionSupport once (if template present and file missing or stale)
        if (_loader != null && _loader.TryLoad("ExecutionSupport", out var execTpl))
        {
            var execPath = Path.Combine(baseOutputDir, "ExecutionSupport.cs");
            bool write = !File.Exists(execPath);
            if (!write)
            {
                try
                {
                    var existing = File.ReadAllText(execPath);
                    if (!existing.Contains($"namespace {ns};")) write = true; // namespace mismatch
                }
                catch { write = true; }
            }
            if (write)
            {
                var execModel = new { Namespace = ns, HEADER = header };
                var code = _renderer.Render(execTpl, execModel);
                File.WriteAllText(execPath, code);
            }
        }
        // StoredProcedure template no longer used after consolidation
        var written = 0;
        string? unifiedTemplateRaw = null;
        bool hasUnifiedTemplate = _loader != null && _loader.TryLoad("UnifiedProcedure", out unifiedTemplateRaw);
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
            var inputTypeName = NamePolicy.Input(procPart);
            var outputTypeName = NamePolicy.Output(procPart);

            // Cleanup alte Dateien (<Proc>Result.cs, <Proc>Input.cs, <Proc>Output.cs) bevor neue Einzeldatei erstellt wird
            try
            {
                var legacyFiles = new[]
                {
                    Path.Combine(schemaDir, procPart + "Result.cs"),
                    Path.Combine(schemaDir, procPart + "Input.cs"),
                    Path.Combine(schemaDir, procPart + "Output.cs")
                };
                foreach (var lf in legacyFiles)
                {
                    if (File.Exists(lf)) File.Delete(lf);
                }
            }
            catch { }
            string finalCode;
            if (hasUnifiedTemplate && unifiedTemplateRaw != null)
            {
                // Build blocks
                // Use root namespace (ns) for execution support types (ExecutionSupport.cs) instead of internal engine namespace
                var usingBlock = "using System;\nusing System.Collections.Generic;\nusing System.Data;\nusing System.Data.Common;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing " + ns + ";";
                var inputBlock = new StringBuilder();
                if (proc.InputParameters.Count > 0)
                {
                    inputBlock.AppendLine($"public readonly record struct {inputTypeName}(");
                    for (int i = 0; i < proc.InputParameters.Count; i++)
                    {
                        var pdesc = proc.InputParameters[i];
                        var comma = i == proc.InputParameters.Count - 1 ? string.Empty : ",";
                        inputBlock.AppendLine($"    {pdesc.ClrType} {pdesc.PropertyName}{comma}");
                    }
                    inputBlock.AppendLine(");");
                }
                var outputBlock = new StringBuilder();
                if (proc.OutputFields.Count > 0)
                {
                    outputBlock.AppendLine($"public readonly record struct {outputTypeName}(");
                    for (int i = 0; i < proc.OutputFields.Count; i++)
                    {
                        var f = proc.OutputFields[i];
                        var comma = i == proc.OutputFields.Count - 1 ? string.Empty : ",";
                        outputBlock.AppendLine($"    {f.ClrType} {f.PropertyName}{comma}");
                    }
                    outputBlock.AppendLine(");");
                }
                var rsRecordsBlock = new StringBuilder();
                var resultClassBlock = new StringBuilder();
                resultClassBlock.AppendLine($"public sealed class {unifiedResultTypeName}");
                resultClassBlock.AppendLine("{");
                resultClassBlock.AppendLine("    public bool Success { get; init; }");
                resultClassBlock.AppendLine("    public string? Error { get; init; }");
                if (proc.OutputFields.Count > 0)
                    resultClassBlock.AppendLine($"    public {outputTypeName}? Output {{ get; init; }}");
                foreach (var rs in proc.ResultSets.OrderBy(r => r.Index))
                {
                    var originalName = rs.Name;
                    var propName = originalName.StartsWith("ResultSet", StringComparison.OrdinalIgnoreCase)
                        ? "Result" + originalName.Substring("ResultSet".Length)
                        : originalName;
                    var normalizedSetNameForType = originalName.StartsWith("ResultSet", StringComparison.OrdinalIgnoreCase) ? originalName : originalName;
                    var rsType = NamePolicy.ResultSet(procPart, normalizedSetNameForType);
                    resultClassBlock.AppendLine($"    public IReadOnlyList<{rsType}> {propName} {{ get; init; }} = Array.Empty<{rsType}>();");
                }
                resultClassBlock.AppendLine("}");
                foreach (var rs in proc.ResultSets.OrderBy(r => r.Index))
                {
                    var originalName = rs.Name;
                    var typeNameBase = originalName.StartsWith("ResultSet", StringComparison.OrdinalIgnoreCase) ? originalName : originalName;
                    var rsType = NamePolicy.ResultSet(procPart, typeNameBase);
                    rsRecordsBlock.AppendLine($"public readonly record struct {rsType}(");
                    for (int i = 0; i < rs.Fields.Count; i++)
                    {
                        var f = rs.Fields[i];
                        var comma = i == rs.Fields.Count - 1 ? string.Empty : ",";
                        rsRecordsBlock.AppendLine($"    {f.ClrType} {f.PropertyName}{comma}");
                    }
                    rsRecordsBlock.AppendLine(");");
                    rsRecordsBlock.AppendLine();
                }
                var planTypeName = procedureTypeName + "Plan";
                var planBlock = new StringBuilder();
                planBlock.AppendLine($"internal static partial class {planTypeName}");
                planBlock.AppendLine("{");
                planBlock.AppendLine("    private static ProcedureExecutionPlan? _cached;");
                planBlock.AppendLine("    public static ProcedureExecutionPlan Instance => _cached ??= Create();");
                planBlock.AppendLine("    private static ProcedureExecutionPlan Create()\n    {");
                planBlock.AppendLine("        var parameters = new ProcedureParameter[] {");
                foreach (var ip in proc.InputParameters)
                    planBlock.AppendLine($"            new(\"@{ip.Name}\", {MapDbType(ip.SqlTypeName)}, {EmitSize(ip)}, false, {ip.IsNullable.ToString().ToLowerInvariant()}),");
                foreach (var outParam in proc.OutputFields)
                    planBlock.AppendLine($"            new(\"@{outParam.Name}\", {MapDbType(outParam.SqlTypeName)}, {EmitSize(outParam)}, true, {outParam.IsNullable.ToString().ToLowerInvariant()}),");
                planBlock.AppendLine("        };\n");
                planBlock.AppendLine("        var resultSets = new ResultSetMapping[] {");
                foreach (var rs in proc.ResultSets.OrderBy(r => r.Index))
                {
                    var rsType = NamePolicy.ResultSet(procPart, rs.Name);
                    var ordinalDecls = string.Join(" ", rs.Fields.Select((f, idx) => $"int o{idx}=r.GetOrdinal(\"{f.Name}\");"));
                    var fieldExprs = string.Join(", ", rs.Fields.Select((f, idx) => MaterializeFieldExpressionCached(f, idx)));
                    planBlock.AppendLine("            new(\"" + rs.Name + "\", async (r, ct) => { var list = new List<object>(); " + ordinalDecls + " while (await r.ReadAsync(ct).ConfigureAwait(false)) { list.Add(new " + rsType + "(" + fieldExprs + ")); } return list; }),");
                }
                planBlock.AppendLine("        };\n");
                if (proc.OutputFields.Count > 0)
                    planBlock.AppendLine($"        object? OutputFactory(IReadOnlyDictionary<string, object?> values) => new {outputTypeName}(" + string.Join(", ", proc.OutputFields.Select(f => CastOutputValue(f))) + ");");
                else
                    planBlock.AppendLine("        object? OutputFactory(IReadOnlyDictionary<string, object?> values) => null;");
                planBlock.Append("        object AggregateFactory(bool success, string? error, object? output, IReadOnlyDictionary<string, object?> outputs, object[] rs) => new ");
                planBlock.Append(unifiedResultTypeName + " { Success = success, Error = error");
                if (proc.OutputFields.Count > 0) planBlock.Append(", Output = (" + NamePolicy.Output(procPart) + "?)output");
                int rsIndex = 0;
                foreach (var rs in proc.ResultSets.OrderBy(r => r.Index))
                {
                    var rsType = NamePolicy.ResultSet(procPart, rs.Name);
                    var originalName = rs.Name;
                    var propName = originalName.StartsWith("ResultSet", StringComparison.OrdinalIgnoreCase) ? "Result" + originalName.Substring("ResultSet".Length) : originalName;
                    planBlock.Append($", {propName} = rs.Length > {rsIndex} ? Array.ConvertAll(((System.Collections.Generic.List<object>)rs[{rsIndex}]).ToArray(), o => ({rsType})o).ToList() : Array.Empty<{rsType}>() ");
                    rsIndex++;
                }
                planBlock.Append("};\n");
                if (proc.InputParameters.Count > 0)
                {
                    var inputType = NamePolicy.Input(procPart);
                    planBlock.AppendLine($"        void Binder(DbCommand cmd, object? state) {{ var input = ({inputType})state!; ");
                    foreach (var ip in proc.InputParameters)
                        planBlock.AppendLine($"            cmd.Parameters[\"@{ip.Name}\"].Value = input.{ip.PropertyName};");
                    planBlock.AppendLine("        }");
                }
                else
                {
                    planBlock.AppendLine("        void Binder(DbCommand cmd, object? state) { }");
                }
                planBlock.AppendLine("        return new ProcedureExecutionPlan(");
                planBlock.AppendLine($"            \"{proc.Schema}.{proc.ProcedureName}\", parameters, resultSets, OutputFactory, AggregateFactory, Binder);\n    }}\n}}");
                var wrapperBlock = new StringBuilder();
                wrapperBlock.AppendLine($"public static class {procedureTypeName}");
                wrapperBlock.AppendLine("{");
                wrapperBlock.AppendLine($"    public const string Name = \"{proc.Schema}.{proc.ProcedureName}\";");
                var inputSignature2 = proc.InputParameters.Count > 0 ? $", {inputTypeName} input" : string.Empty;
                wrapperBlock.AppendLine($"    public static Task<{unifiedResultTypeName}> ExecuteAsync(DbConnection connection{inputSignature2}, CancellationToken cancellationToken = default)");
                wrapperBlock.AppendLine("    {");
                if (proc.InputParameters.Count > 0)
                    wrapperBlock.AppendLine($"        return ProcedureExecutor.ExecuteAsync<{unifiedResultTypeName}>(connection, {planTypeName}.Instance, input, cancellationToken);");
                else
                    wrapperBlock.AppendLine($"        return ProcedureExecutor.ExecuteAsync<{unifiedResultTypeName}>(connection, {planTypeName}.Instance, null, cancellationToken);");
                wrapperBlock.AppendLine("    }");
                wrapperBlock.AppendLine("}");

                var model = new
                {
                    Namespace = finalNs,
                    UsingDirectives = usingBlock,
                    InputRecordBlock = inputBlock.ToString().TrimEnd(),
                    OutputRecordBlock = outputBlock.ToString().TrimEnd(),
                    ResultSetRecordsBlock = rsRecordsBlock.ToString().TrimEnd(),
                    UnifiedResultClassBlock = resultClassBlock.ToString().TrimEnd(),
                    PlanClassBlock = planBlock.ToString().TrimEnd(),
                    WrapperClassBlock = wrapperBlock.ToString().TrimEnd(),
                    HEADER = header
                };
                finalCode = _renderer.Render(unifiedTemplateRaw!, model);
            }
            else
            {
                // Fallback: original inline build
                var fileSb = new StringBuilder();
                fileSb.Append(header);
                fileSb.AppendLine($"namespace {finalNs};");
                fileSb.AppendLine();
                fileSb.AppendLine("using System;\nusing System.Collections.Generic;\nusing System.Data;\nusing System.Data.Common;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing " + ns + ";");
                // (For brevity, we could replicate blocks, but template should normally exist now)
                finalCode = fileSb.ToString();
            }
            File.WriteAllText(Path.Combine(schemaDir, procPart + ".cs"), finalCode);
            written++;
        }
        return written;
    }

    private static string MapDbType(string sqlType)
    {
        if (string.IsNullOrWhiteSpace(sqlType)) return "System.Data.DbType.String";
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
