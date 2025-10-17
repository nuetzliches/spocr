using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using SpocR.SpocRVNext.Engine;
using SpocR.SpocRVNext.Metadata;
using SpocRVNext.Configuration;
using SpocR.SpocRVNext.Utils;
using SpocR.Models;
using System.Text.Json;

namespace SpocR.SpocRVNext.Generators;

public sealed class ProceduresGenerator
{
    private readonly ITemplateRenderer _renderer;
    private readonly ITemplateLoader? _loader;
    private readonly Func<IReadOnlyList<ProcedureDescriptor>> _provider;
    private readonly EnvConfiguration? _cfg;
    private readonly string _projectRoot;

    public ProceduresGenerator(ITemplateRenderer renderer, Func<IReadOnlyList<ProcedureDescriptor>> provider, ITemplateLoader? loader = null, string? projectRoot = null, EnvConfiguration? cfg = null)
    {
        _renderer = renderer;
        _provider = provider;
        _loader = loader;
        _projectRoot = projectRoot ?? Directory.GetCurrentDirectory();
        _cfg = cfg; // may be null for legacy call sites
    }

    public int Generate(string ns, string baseOutputDir)
    {
        var procs = _provider();
        // 1) Positive allow-list: prefer EnvConfiguration.BuildSchemas; fallback to direct env var only if cfg missing
        HashSet<string>? buildSchemas = null;
        if (_cfg?.BuildSchemas is { Count: > 0 })
        {
            buildSchemas = new HashSet<string>(_cfg.BuildSchemas, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            var buildSchemasRaw = Environment.GetEnvironmentVariable("SPOCR_BUILD_SCHEMAS");
            if (!string.IsNullOrWhiteSpace(buildSchemasRaw))
            {
                buildSchemas = new HashSet<string>(buildSchemasRaw!
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0), StringComparer.OrdinalIgnoreCase);
            }
        }
        if (buildSchemas is { Count: > 0 })
        {
            var before = procs.Count;
            // Filter strictly by schema list
            procs = procs.Where(p => buildSchemas.Contains(p.Schema ?? "dbo")).ToList();
            var removed = before - procs.Count;
            try { Console.Out.WriteLine($"[spocr vNext] Info: BuildSchemas allow-list active -> {procs.Count} of {before} procedures retained. Removed: {removed}. Schemas: {string.Join(",", buildSchemas)}"); } catch { }
        }
        // 2) Dynamic negative filter only when no positive list is active
        //    Reads ignored schemas from spocr.json (if present) and/or ENV override
        List<string> ignoredSchemas = new();
        try
        {
            var configPath = Path.Combine(_projectRoot, "spocr.json");
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                ConfigurationModel? cfg = null;
                try { cfg = JsonSerializer.Deserialize<ConfigurationModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); } catch { /* ignore parse errors */ }
                if (cfg?.Project?.IgnoredSchemas != null)
                {
                    ignoredSchemas.AddRange(cfg.Project.IgnoredSchemas.Where(s => !string.IsNullOrWhiteSpace(s)));
                }
                else if (cfg?.Schema?.Any() == true)
                {
                    ignoredSchemas.AddRange(cfg.Schema.Where(s => s.Status == SchemaStatusEnum.Ignore).Select(s => s.Name));
                }
            }
            // ENV Override (Komma-separiert): SPOCR_IGNORED_SCHEMAS
            var envIgnored = Environment.GetEnvironmentVariable("SPOCR_IGNORED_SCHEMAS");
            if (!string.IsNullOrWhiteSpace(envIgnored))
            {
                ignoredSchemas.AddRange(envIgnored.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
            }
        }
        catch { /* silent */ }
        if ((buildSchemas == null || buildSchemas.Count == 0) && ignoredSchemas.Count > 0)
        {
            var ignoredSet = new HashSet<string>(ignoredSchemas, StringComparer.OrdinalIgnoreCase);
            var before = procs.Count;
            procs = procs.Where(p => !ignoredSet.Contains(p.Schema ?? "dbo")).ToList();
            var removed = before - procs.Count;
            if (removed > 0)
            {
                try { Console.Out.WriteLine($"[spocr vNext] Info: Filtered ignored schemas (dynamic): removed {removed} procedures (total now {procs.Count})."); } catch { }
            }
        }
        if (procs.Count == 0)
        {
            try { Console.Out.WriteLine("[spocr vNext] Info: ProceduresGenerator skipped – provider returned 0 procedures."); } catch { }
            return 0;
        }
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
        if (!hasUnifiedTemplate)
        {
            try { Console.Out.WriteLine("[spocr vNext] Warn: UnifiedProcedure.spt not found – generating fallback skeleton (check template path)"); } catch { }
        }
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
                var usingBlock = "using System;\nusing System.Collections.Generic;\nusing System.Linq;\nusing System.Data;\nusing System.Data.Common;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing " + ns + ";";

                // Structured metadata for template-driven record generation
                var rsMeta = new List<object>();
                int rsIdx = 0;
                foreach (var rs in proc.ResultSets.OrderBy(r => r.Index))
                {
                    var rsType = NamePolicy.ResultSet(procPart, rs.Name);
                    // Build plan meta for template
                    var ordinalDecls = string.Join(" ", rs.Fields.Select((f, idx) => $"int o{idx}=r.GetOrdinal(\"{f.Name}\");"));
                    var fieldExprs = string.Join(", ", rs.Fields.Select((f, idx) => MaterializeFieldExpressionCached(f, idx)));
                    var propName = rs.Name.StartsWith("ResultSet", StringComparison.OrdinalIgnoreCase) ? "Result" + rs.Name.Substring("ResultSet".Length) : rs.Name;
                    var assignment = $", {propName} = rs.Length > {rsIdx} ? Array.ConvertAll(((System.Collections.Generic.List<object>)rs[{rsIdx}]).ToArray(), o => ({rsType})o).ToList() : Array.Empty<{rsType}>()";
                    var fieldsBlock = string.Join(Environment.NewLine, rs.Fields.Select((f, i) => $"    {f.ClrType} {f.PropertyName}{(i == rs.Fields.Count - 1 ? string.Empty : ",")}"));
                    rsMeta.Add(new
                    {
                        Name = rs.Name,
                        TypeName = rsType,
                        PropName = propName,
                        OrdinalDecls = ordinalDecls,
                        FieldExprs = fieldExprs,
                        Index = rsIdx,
                        AggregateAssignment = assignment,
                        FieldsBlock = fieldsBlock
                    });
                    rsIdx++;
                }

                // Parameters meta
                var paramLines = new List<string>();
                foreach (var ip in proc.InputParameters)
                    paramLines.Add($"new(\"@{ip.Name}\", {MapDbType(ip.SqlTypeName)}, {EmitSize(ip)}, false, {ip.IsNullable.ToString().ToLowerInvariant()})");
                foreach (var opf in proc.OutputFields)
                    paramLines.Add($"new(\"@{opf.Name}\", {MapDbType(opf.SqlTypeName)}, {EmitSize(opf)}, true, {opf.IsNullable.ToString().ToLowerInvariant()})");

                // Output factory args
                string outputFactoryArgs = proc.OutputFields.Count > 0 ? string.Join(", ", proc.OutputFields.Select(f => CastOutputValue(f))) : string.Empty;

                // Aggregate assignments
                var aggregateAssignments = rsMeta.Select(m => ((dynamic)m).AggregateAssignment as string).ToList();

                var model = new
                {
                    Namespace = finalNs,
                    UsingDirectives = usingBlock,
                    HEADER = header,
                    HasInput = proc.InputParameters.Count > 0,
                    HasOutput = proc.OutputFields.Count > 0,
                    HasResultSets = proc.ResultSets.Count > 0,
                    InputParameters = proc.InputParameters.Select((p, i) => new { p.ClrType, p.PropertyName, Comma = i == proc.InputParameters.Count - 1 ? string.Empty : "," }).ToList(),
                    OutputFields = proc.OutputFields.Select((f, i) => new { f.ClrType, f.PropertyName, Comma = i == proc.OutputFields.Count - 1 ? string.Empty : "," }).ToList(),
                    ProcedureFullName = proc.Schema + "." + proc.ProcedureName,
                    ProcedureTypeName = procedureTypeName,
                    UnifiedResultTypeName = unifiedResultTypeName,
                    OutputTypeName = outputTypeName,
                    PlanTypeName = procedureTypeName + "Plan",
                    InputTypeName = inputTypeName,
                    ParameterLines = paramLines,
                    InputAssignments = proc.InputParameters.Select(ip => $"cmd.Parameters[\"@{ip.Name}\"].Value = input.{ip.PropertyName};").ToList(),
                    ResultSets = rsMeta,
                    OutputFactoryArgs = outputFactoryArgs,
                    HasAggregateOutput = proc.OutputFields.Count > 0,
                    AggregateAssignments = aggregateAssignments
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
            finalCode = NormalizeWhitespace(finalCode);
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

    private static readonly Regex MultiBlankLines = new("(\r?\n){3,}", RegexOptions.Compiled);
    private static string NormalizeWhitespace(string code)
    {
        if (string.IsNullOrEmpty(code)) return code;
        // Collapse 3+ consecutive newlines to exactly two (i.e., one blank line)
        code = MultiBlankLines.Replace(code, match => match.Value.StartsWith("\r\n\r\n") ? "\r\n\r\n" : "\n\n");
        // Ensure exactly one trailing newline
        if (!code.EndsWith("\n")) code += Environment.NewLine;
        return code;
    }
}
