using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Text;
using SpocR.CodeGenerators.Base;
using SpocR.Contracts;
using SpocR.Enums;
using SpocR.Extensions;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;
using SpocR.Utils;
using SpocR.CodeGenerators.Utils;

namespace SpocR.CodeGenerators.Models;

/// <summary>
/// Stored procedure generator
///  - Modern (net10+) one file per stored procedure in SpocR/Schema/ with: Input record, Output record (OUTPUT params), Result model (result set), partial SpocRDbContext method.
///  - Legacy extension classes (DataContext) retained when compatibility v4.5 active or framework is pre-modern.
/// Dual generation: Modern always for net10; legacy additionally when compatibility mode.
/// </summary>
public class StoredProcedureGenerator(
    FileManager<ConfigurationModel> configFile,
    OutputService outputService,
    IConsoleService console,
    TemplateManager templateManager,
    ISchemaMetadataProvider metadataProvider
) : GeneratorBase(configFile, outputService, console)
{
    private static readonly HashSet<string> SystemOutputNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "@ResultId","@RecordId","@RowVersion","@Result"
    };

    private bool IsModern() => IsModernTfm(ConfigFile.Config.TargetFramework);
    private bool HasCompatibility() => string.Equals(ConfigFile.Config.Project.Output?.CompatibilityMode, "v4.5", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Entry point used by orchestrator.
    /// </summary>
    public async Task GenerateDataContextStoredProceduresAsync(bool isDryRun)
    {
        var schemas = metadataProvider.GetSchemas()
            .Where(s => s.Status == SchemaStatusEnum.Build && (s.StoredProcedures?.Any() ?? false))
            .Select(Definition.ForSchema)
            .ToList();

        var modern = IsModern();
        var compat = HasCompatibility();

        if (modern)
        {
            await EnsureModernRootAsync(isDryRun);
        }

        foreach (var schema in schemas)
        {
            var procedures = schema.StoredProcedures?.ToList() ?? [];
            if (!procedures.Any()) continue;

            if (modern)
            {
                var modernSchemaDir = DirectoryUtils.GetWorkingDirectory("SpocR", schema.Path);
                if (!Directory.Exists(modernSchemaDir) && !isDryRun) Directory.CreateDirectory(modernSchemaDir);
            }

            if ((!modern || compat))
            {
                var legacySchemaDir = DirectoryUtils.GetWorkingDirectory(
                    ConfigFile.Config.Project.Output.DataContext.Path,
                    ConfigFile.Config.Project.Output.DataContext.StoredProcedures.Path,
                    schema.Path);
                if (!Directory.Exists(legacySchemaDir) && !isDryRun) Directory.CreateDirectory(legacySchemaDir);
            }

            foreach (var group in procedures.GroupBy(p => p.Name))
            {
                var name = group.Key;
                var sp = group.First(); // representative for unified file

                if (modern)
                {
                    var filePath = Path.Combine(DirectoryUtils.GetWorkingDirectory("SpocR", schema.Path), name + ".cs");
                    try
                    {
                        var content = await BuildUnifiedAsync(schema, sp);
                        await Output.WriteAsync(filePath, SourceText.From(content), isDryRun);
                        console.Verbose($"[sp-modern] {name}.cs");
                    }
                    catch (Exception ex)
                    {
                        console.Warn($"[sp-modern:fail] {name}: {ex.Message}");
                    }
                }

                if (!modern || compat)
                {
                    try
                    {
                        var legacyFile = Path.Combine(
                            DirectoryUtils.GetWorkingDirectory(
                                ConfigFile.Config.Project.Output.DataContext.Path,
                                ConfigFile.Config.Project.Output.DataContext.StoredProcedures.Path,
                                schema.Path),
                            name + "Extensions.cs");
                        var code = await GetStoredProcedureExtensionsCodeAsync(schema, group.ToList());
                        await Output.WriteAsync(legacyFile, code, isDryRun);
                        console.Verbose($"[sp-legacy] {name}Extensions.cs");
                    }
                    catch (Exception ex)
                    {
                        console.Warn($"[sp-legacy:fail] {name}: {ex.Message}");
                    }
                }
            }
        }
    }

    private async Task EnsureModernRootAsync(bool isDryRun)
    {
        var rootDir = DirectoryUtils.GetWorkingDirectory("SpocR");
        if (!Directory.Exists(rootDir) && !isDryRun) Directory.CreateDirectory(rootDir);
        var central = Path.Combine(rootDir, "SpocRDbContext.StoredProcedures.cs");
        if (!File.Exists(central))
        {
            var rootNs = ConfigFile.Config.Project.Output.Namespace ?? "SpocR.Generated";
            var nl = Environment.NewLine;
            var txt = $"namespace {rootNs}.SpocR;{nl}{nl}" +
                      "public class SpocRStoredProcedureCallOptions{ public int? CommandTimeout { get; set; } public ISpocRTransaction Transaction { get; set; } }" + nl +
                      "public partial class SpocRDbContext { /* partial methods generated per stored procedure file */ }" + nl;
            await Output.WriteAsync(central, SourceText.From(txt), isDryRun);
        }
    }

    // Unified file builder (modern)
    private async Task<string> BuildUnifiedAsync(Definition.Schema schema, Definition.StoredProcedure sp)
    {
        var raw = await templateManager.ReadTemplateRawAsync("StoredProcedure.Net100.cs.template")
                  ?? throw new InvalidOperationException("StoredProcedure.Net100.cs.template not found");

        var rootNs = ConfigFile.Config.Project.Output.Namespace ?? "SpocR.Generated";
        if (rootNs.EndsWith(".SpocR", StringComparison.Ordinal)) rootNs = rootNs[..^6];

        var inputParams = sp.Input.Where(p => !p.IsOutput).ToList();
        var allOutputs = sp.GetOutputs()?.ToList() ?? new List<StoredProcedureInputModel>();
        var customOutputs = allOutputs.Where(o => !SystemOutputNames.Contains(o.Name)).ToList();
        var firstSet = sp.ResultSets?.FirstOrDefault();
        var resultCols = firstSet?.Columns?.ToList() ?? new List<StoredProcedureContentModel.ResultColumn>();
        var returnsJson = firstSet?.ReturnsJson ?? false;

        bool hasResultSetModel = false;
        if (firstSet != null)
        {
            var hasCols = resultCols.Any();
            var scalarNonJson = hasCols && !returnsJson && resultCols.Count == 1; // treat single column, non-json as scalar -> no Result model
            hasResultSetModel = !scalarNonJson && (returnsJson || hasCols);
        }

        string methodReturnType;
        if (hasResultSetModel) methodReturnType = sp.Name + "Result";
        else if (customOutputs.Any()) methodReturnType = sp.Name + "Output";
        else methodReturnType = "int";

        raw = raw.Replace("{{Namespace}}", rootNs, StringComparison.Ordinal);
        raw = raw.Replace("{{SchemaName}}", schema.Name, StringComparison.Ordinal);
        raw = raw.Replace("{{FullSqlName}}", sp.SqlObjectName, StringComparison.Ordinal);
        raw = raw.Replace("{{Name}}", sp.Name, StringComparison.Ordinal);
        raw = raw.Replace("{{MethodReturnType}}", methodReturnType, StringComparison.Ordinal);

        // Evaluate top-level feature flags (supports optional {{else}} blocks)
        raw = EvaluateFlag(raw, "HasInputs", inputParams.Any());
        raw = EvaluateFlag(raw, "HasOutputParams", customOutputs.Any());
        raw = EvaluateFlag(raw, "HasResultSetModel", hasResultSetModel);

        raw = ApplyInputSection(raw, inputParams);
        raw = ApplyOutputParamsSection(raw, customOutputs);
        raw = ApplyResultSetSection(raw, hasResultSetModel, resultCols);

        // Post-process: fix leading / trailing commas inside generated parentheses (avoid touching null-coalescing ?? operators)
        raw = Regex.Replace(raw, @"\(\s*,", "("); // leading comma
        raw = Regex.Replace(raw, @",\s*\)", ")"); // trailing comma before )
                                                  // Remove orphan property lines without type (artifact of empty loops)
        raw = Regex.Replace(raw, "^\\s*\\{\\s*get;\\s*set;\\s*\\}\\s*$", string.Empty, RegexOptions.Multiline);
        // Collapse accidental double nullable markers like int?? or string??
        raw = Regex.Replace(raw, "([A-Za-z0-9_>])\\?\\?", "$1?");
        // Replace erroneous 'var effectiveTx = transaction ? opts.Transaction;' (if any slipped through) with null-coalescing
        raw = raw.Replace("var effectiveTx = transaction ? opts.Transaction;", "var effectiveTx = transaction ?? opts.Transaction;");

        raw = CleanupTemplate(raw);
        return raw;
    }

    private string ApplyInputSection(string template, List<StoredProcedureInputModel> inputs)
    {
        if (!inputs.Any())
        {
            return EvaluateFlag(template, "HasInputs", false);
        }
        template = EvaluateFlag(template, "HasInputs", true);
        var rows = inputs.Select(p =>
        {
            var baseType = ResolveClrType(p, false);
            var isRef = baseType == "string" || baseType == "object";
            var nullable = (p.IsNullable ?? true) && (!isRef || baseType == "string");
            var finalType = baseType + (nullable && !baseType.EndsWith("?") && baseType != "object" ? "?" : string.Empty);
            if (p.IsTableType == true && !string.IsNullOrWhiteSpace(p.TableTypeName) && !string.IsNullOrWhiteSpace(p.TableTypeSchemaName))
            {
                var tt = ResolveClrType(p, true);
                if (string.IsNullOrWhiteSpace(tt) || tt == "object") tt = "object"; // fallback
                finalType = $"IEnumerable<{tt}>"; // collections not marked nullable here
            }
            return new Dictionary<string, string>
            {
                ["IsTableType"] = (p.IsTableType == true).ToString(),
                ["TableTypeClr"] = ResolveClrType(p, true),
                ["ClrType"] = finalType,
                ["PropertyName"] = GetPropertyName(p.Name),
                ["Nullable"] = (p.IsNullable ?? true).ToString()
            };
        }).ToList();
        return ReplaceEach(template, "InputParameters", rows);
    }

    private string ApplyOutputParamsSection(string template, List<StoredProcedureInputModel> outputs)
    {
        if (!outputs.Any())
        {
            return EvaluateFlag(template, "HasOutputParams", false);
        }
        template = EvaluateFlag(template, "HasOutputParams", true);
        var rows = outputs.Select(o =>
        {
            var baseType = ResolveClrType(o, false);
            if (baseType.EndsWith("?", StringComparison.Ordinal)) baseType = baseType.TrimEnd('?'); // template drives nullable
            var isRef = baseType == "string" || baseType == "object";
            var nullable = (o.IsNullable ?? true) && (!isRef || baseType == "string");
            return new Dictionary<string, string>
            {
                ["ClrType"] = baseType,
                ["Nullable"] = nullable.ToString(),
                ["PropertyName"] = GetPropertyName(o.Name)
            };
        }).ToList();
        return ReplaceEach(template, "OutputParameters", rows);
    }

    private string ApplyResultSetSection(string template, bool hasResultModel, List<StoredProcedureContentModel.ResultColumn> cols)
    {
        if (!hasResultModel)
        {
            return EvaluateFlag(template, "HasResultSetModel", false);
        }
        template = EvaluateFlag(template, "HasResultSetModel", true);
        var rows = cols.Select(c =>
        {
            var baseType = ResolveColumnClr(c);
            if (baseType.EndsWith("?", StringComparison.Ordinal)) baseType = baseType.TrimEnd('?');
            var isRef = baseType == "string" || baseType == "object";
            var nullable = (c.IsNullable ?? true) && (!isRef || baseType == "string");
            return new Dictionary<string, string>
            {
                ["ClrType"] = baseType,
                ["Nullable"] = nullable.ToString(),
                ["PropertyName"] = c.Name.FirstCharToUpper()
            };
        }).ToList();
        return ReplaceEach(template, "ResultColumns", rows);
    }

    private string ResolveClrType(StoredProcedureInputModel p, bool tableTypeContext)
    {
        if (p.IsTableType == true && tableTypeContext)
        {
            return "object"; // TODO: map to generated table type class/record if available
        }
        return GetClrTypeNameFromSqlDbTypeName(p.SqlTypeName, p.IsNullable ?? true);
    }

    private string ResolveColumnClr(StoredProcedureContentModel.ResultColumn c) => GetClrTypeNameFromSqlDbTypeName(c.SqlTypeName, c.IsNullable ?? true);
    private static string GetPropertyName(string sqlName) => sqlName.StartsWith("@") ? sqlName[1..].FirstCharToUpper() : sqlName.FirstCharToUpper();

    #region Handlebars-lite helpers
    private static string EvaluateFlag(string template, string flag, bool value)
    {
        // Handles {{#if Flag}}...{{else}}...{{/if}} or without else
        var pattern = $"\\{{\\{{#if {flag}\\}}\\}}([\\s\\S]*?)(\\{{\\{{else\\}}\\}}([\\s\\S]*?))?\\{{\\{{/if\\}}\\}}";
        return Regex.Replace(template, pattern, m =>
        {
            if (value) return m.Groups[1].Value; // true branch
            if (m.Groups[3].Success) return m.Groups[3].Value; // else branch
            return string.Empty;
        }, RegexOptions.Singleline);
    }

    private static string ReplaceEach(string section, string eachName, IEnumerable<Dictionary<string, string>> rows)
    {
        var pattern = $"\\{{\\{{#each {eachName}\\}}\\}}([\\s\\S]*?)\\{{\\{{/each\\}}\\}}";
        return Regex.Replace(section, pattern, m =>
        {
            var inner = m.Groups[1].Value;
            var list = rows.ToList();
            return string.Join("", list.Select((row, idx) =>
            {
                var item = inner;
                // Evaluate row-level simple if/else blocks for any key present
                item = Regex.Replace(item, "\\{\\{#if (\\w+)\\}\\}([\\s\\S]*?)(\\{\\{else\\}\\}([\\s\\S]*?))?\\{\\{/if\\}\\}", mm =>
                {
                    var key = mm.Groups[1].Value;
                    var truePart = mm.Groups[2].Value;
                    var elsePart = mm.Groups[4].Success ? mm.Groups[4].Value : string.Empty;
                    if (row.TryGetValue(key, out var val) && bool.TryParse(val, out var b) && b)
                        return truePart;
                    return elsePart;
                }, RegexOptions.Singleline);

                foreach (var kv in row)
                {
                    item = item.Replace($"{{{{{kv.Key}}}}}", kv.Value);
                }
                // Nullable marker (post if-processing)
                item = Regex.Replace(item, "\\{\\{#if Nullable\\}}\\?\\{\\{/if\\}}", row.TryGetValue("Nullable", out var val) && bool.TryParse(val, out var b) && b ? "?" : "");
                // @last handling
                item = Regex.Replace(item, "\\{\\{#unless @last\\}\\}([\\s\\S]*?)\\{\\{/unless\\}\\}", idx < list.Count - 1 ? "$1" : string.Empty);
                return item;
            }));
        }, RegexOptions.Singleline);
    }

    private static string CleanupTemplate(string raw)
    {
        raw = Regex.Replace(raw, @"\{\{!--.*?--\}\}", string.Empty, RegexOptions.Singleline); // comments
        raw = Regex.Replace(raw, @"\{\{[#/][^}]+\}\}", string.Empty); // any remaining block markers
        raw = Regex.Replace(raw, @"\{\{[^}]+\}\}", string.Empty); // stray tokens
        raw = Regex.Replace(raw, "\n{3,}", "\n\n");
        return raw.Trim() + Environment.NewLine;
    }
    #endregion

    #region Legacy (extension) generation
    /// <summary>
    /// Simplified legacy extension generation supporting raw + JSON deserialize variants.
    /// Restores method consumed by existing tests.
    /// </summary>
    public async Task<SourceText> GetStoredProcedureExtensionsCodeAsync(Definition.Schema schema, List<Definition.StoredProcedure> procedures)
    {
        await Task.Yield(); // keep async signature
        var rootNs = ConfigFile.Config.Project.Output.Namespace ?? "SpocR.Generated";
        var schemaName = schema.Name;
        var nl = Environment.NewLine;
        var sb = new System.Text.StringBuilder();
        sb.Append("using System;\nusing System.Collections.Generic;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing Microsoft.Data.SqlClient;\n");
        sb.Append($"namespace {rootNs}.DataContext.StoredProcedures.{schemaName};{nl}{nl}");
        var className = procedures.First().Name + "Extensions";
        sb.Append("public static class ").Append(className).Append(nl).Append("{").Append(nl);

        foreach (var proc in procedures)
        {
            var hasInputs = proc.Input.Any(i => !i.IsOutput);
            var inputTypeName = proc.Name + "Input";
            var paramSig = hasInputs ? inputTypeName + " input, " : string.Empty;
            var firstSet = proc.ResultSets?.FirstOrDefault();
            var isJson = firstSet?.ReturnsJson ?? false;
            var isJsonArray = isJson && (firstSet?.ReturnsJsonArray ?? false);

            // Raw method
            sb.Append("    /// <summary>Executes ").Append(proc.SqlObjectName);
            if (isJson) sb.Append(" and returns the raw JSON string."); else sb.Append(" and returns CrudResult.");
            sb.Append("</summary>").Append(nl)
              .Append("    public static Task<").Append(isJson ? "string" : "CrudResult").Append("> ")
              .Append(proc.Name).Append("Async(this IAppDbContextPipe context, ")
              .Append(paramSig).Append("CancellationToken cancellationToken = default)").Append(nl)
              .Append("    {").Append(nl)
              .Append("        if(context==null) throw new ArgumentNullException(\"context\");").Append(nl)
                            .Append("        var parameters = new List<SqlParameter>()").Append(nl)
                            .Append("        {").Append(nl)
                            .Append(BuildLegacyParameterList(proc, hasInputs)).Append(nl)
                            .Append("        };").Append(nl);
            if (isJson)
                sb.Append("        return context.ReadJsonAsync(\"").Append(proc.SqlObjectName).Append("\", parameters, cancellationToken);").Append(nl);
            else
                sb.Append("        return context.ExecuteSingleAsync<CrudResult>(\"").Append(proc.SqlObjectName).Append("\", parameters, cancellationToken);").Append(nl);
            sb.Append("    }").Append(nl).Append(nl);

            if (isJson)
            {
                var modelName = proc.Name;
                var deserializeType = isJsonArray ? $"List<{modelName}>" : modelName;
                sb.Append("    /// <summary>Executes ").Append(proc.SqlObjectName).Append(" and deserializes the JSON response to ")
                    .Append(deserializeType).Append(".</summary>").Append(nl)
  .Append("    public static async Task<").Append(deserializeType).Append("> ")
  .Append(proc.Name).Append("DeserializeAsync(this IAppDbContextPipe context, ")
  .Append(paramSig).Append("CancellationToken cancellationToken = default)").Append(nl)
  .Append("    {").Append(nl)
  .Append("        if(context==null) throw new ArgumentNullException(\"context\");").Append(nl)
                  .Append("        var parameters = new List<SqlParameter>()\n        {\n")
                  .Append(BuildLegacyParameterList(proc, hasInputs)).Append("\n        };\n")
  .Append("        return await context.ReadJsonDeserializeAsync<")
  .Append(deserializeType).Append(">(\"").Append(proc.SqlObjectName).Append("\", parameters, cancellationToken);").Append(nl)
  .Append("    }").Append(nl).Append(nl);
            }
        }

        sb.Append("}").Append(nl);
        return SourceText.From(sb.ToString());
    }
    #endregion

    private string BuildLegacyParameterList(Definition.StoredProcedure proc, bool hasInputs)
    {
        var items = new List<string>();
        foreach (var p in proc.Input)
        {
            var name = p.Name;
            var clrName = GetPropertyName(name);
            var sqlDbType = MapSqlDbType(p.SqlTypeName);
            var sizeFragment = BuildSizeFragment(p.SqlTypeName, p.MaxLength);
            var direction = p.IsOutput ? "ParameterDirection.Output" : "ParameterDirection.Input";
            if (p.IsTableType == true)
            {
                // Placeholder for table type mapping - user must supply DataTable or structured value
                items.Add($"            new SqlParameter(\"{name}\", System.Data.SqlDbType.Structured) {{ TypeName = \"{p.TableTypeSchemaName}.{p.TableTypeName}\", Direction = {direction} }}");
            }
            else
            {
                var valueExpr = p.IsOutput ? "DBNull.Value" : (hasInputs ? $"(object)input.{clrName} ?? DBNull.Value" : "DBNull.Value");
                items.Add($"            new SqlParameter(\"{name}\", {valueExpr}) {{ SqlDbType = {sqlDbType}{sizeFragment}, Direction = {direction} }}");
            }
        }
        return string.Join(",\n", items);
    }

    private static string BuildSizeFragment(string sqlTypeName, int? maxLength)
    {
        if (string.IsNullOrWhiteSpace(sqlTypeName) || maxLength == null) return string.Empty;
        var typeLower = sqlTypeName.ToLowerInvariant();
        if (typeLower.Contains("char") || typeLower.Contains("binary"))
        {
            if (maxLength > 0 && maxLength != int.MaxValue)
            {
                return $", Size = {maxLength}";
            }
        }
        return string.Empty;
    }

    private static string MapSqlDbType(string sqlTypeName)
    {
        if (string.IsNullOrWhiteSpace(sqlTypeName)) return "System.Data.SqlDbType.Variant";
        var t = sqlTypeName.ToLowerInvariant();
        return t switch
        {
            var x when x.StartsWith("bigint") => "System.Data.SqlDbType.BigInt",
            var x when x.StartsWith("int") => "System.Data.SqlDbType.Int",
            var x when x.StartsWith("smallint") => "System.Data.SqlDbType.SmallInt",
            var x when x.StartsWith("tinyint") => "System.Data.SqlDbType.TinyInt",
            var x when x.StartsWith("bit") => "System.Data.SqlDbType.Bit",
            var x when x.StartsWith("nvarchar") => "System.Data.SqlDbType.NVarChar",
            var x when x.StartsWith("varchar") => "System.Data.SqlDbType.VarChar",
            var x when x.StartsWith("nchar") => "System.Data.SqlDbType.NChar",
            var x when x.StartsWith("char") => "System.Data.SqlDbType.Char",
            var x when x.StartsWith("text") => "System.Data.SqlDbType.Text",
            var x when x.StartsWith("ntext") => "System.Data.SqlDbType.NText",
            var x when x.StartsWith("datetimeoffset") => "System.Data.SqlDbType.DateTimeOffset",
            var x when x.StartsWith("datetime2") => "System.Data.SqlDbType.DateTime2",
            var x when x.StartsWith("datetime") => "System.Data.SqlDbType.DateTime",
            var x when x.StartsWith("smalldatetime") => "System.Data.SqlDbType.SmallDateTime",
            var x when x.StartsWith("date") => "System.Data.SqlDbType.Date",
            var x when x.StartsWith("time") => "System.Data.SqlDbType.Time",
            var x when x.StartsWith("decimal") || x.StartsWith("numeric") => "System.Data.SqlDbType.Decimal",
            var x when x.StartsWith("money") => "System.Data.SqlDbType.Money",
            var x when x.StartsWith("smallmoney") => "System.Data.SqlDbType.SmallMoney",
            var x when x.StartsWith("float") => "System.Data.SqlDbType.Float",
            var x when x.StartsWith("real") => "System.Data.SqlDbType.Real",
            var x when x.StartsWith("uniqueidentifier") => "System.Data.SqlDbType.UniqueIdentifier",
            var x when x.StartsWith("varbinary") => "System.Data.SqlDbType.VarBinary",
            var x when x.StartsWith("binary") => "System.Data.SqlDbType.Binary",
            var x when x.StartsWith("image") => "System.Data.SqlDbType.Image",
            var x when x.StartsWith("xml") => "System.Data.SqlDbType.Xml",
            var x when x.StartsWith("sql_variant") => "System.Data.SqlDbType.Variant",
            _ => "System.Data.SqlDbType.Variant"
        };
    }
}
